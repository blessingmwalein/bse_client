using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BseMarketDataClient.Fast;
using BseMarketDataClient.Fix;
using BseMarketDataClient.Logging;
using BseMarketDataClient.Networking;

namespace BseMarketDataClient.Session
{
    public class SnapshotSession
    {
        private readonly string _ip;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        
        private readonly TcpSession _tcp;
        private readonly FastFrameDecoder _decoder = new();
        private bool _isLoggedIn = false;
        private DateTime _lastReceivedTime = DateTime.MinValue;
        private DateTime _lastSentTime = DateTime.MinValue;
        private readonly int _heartBtInt = 30;

        public SnapshotSession(string ip, int port, string username, string password)
        {
            _ip = ip;
            _port = port;
            _username = username;
            _password = password;
            
            _tcp = new TcpSession(_ip, _port);
            _tcp.DataReceived += OnDataReceived;
            _tcp.Disconnected += OnDisconnected;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            try
            {
                await _tcp.ConnectAsync(ct);
                
                // 1. Send Logon immediately
                await SendLogonAsync(ct);
                
                // 2. Start background loops
                var hbLoop = SendHeartbeatLoopAsync(ct);
                
                // 3. Wait for liveness (Heartbeats, etc.)
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(1000, ct);
                    
                    if (_isLoggedIn)
                    {
                        var idleTime = (DateTime.Now - _lastReceivedTime).TotalSeconds;
                        if (idleTime > _heartBtInt + 5)
                        {
                            ConsoleLogger.Warning($"Session idle for {idleTime:F1}s. Sending TestRequest...");
                            await SendTestRequestAsync(ct);
                        }
                        
                        if (idleTime > (_heartBtInt * 2))
                        {
                            ConsoleLogger.Error("Session timeout - no data received. Reconnecting...");
                            break; 
                        }
                    }
                }
                
                await hbLoop;
            }
            catch (OperationCanceledException)
            {
                if (_isLoggedIn) await SendLogoutAsync(CancellationToken.None);
            }
            finally
            {
                _tcp.Dispose();
            }
        }

        private async Task SendLogonAsync(CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "A"); // Logon
            msg.Set(52, DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff"));
            msg.Set(108, 30); // HeartBtInt
            msg.Set(553, _username);
            msg.Set(554, _password);

            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("Logon sent.");
        }

        private async Task SendLogoutAsync(CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "5"); // Logout
            msg.Set(52, DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff"));
            
            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("Logout sent.");
        }

        private async Task SendHeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
                
                if (_isLoggedIn && (DateTime.Now - _lastSentTime).TotalSeconds >= _heartBtInt)
                {
                    await SendHeartbeatAsync(null, ct);
                }
            }
        }

        private async Task SendHeartbeatAsync(string? testReqId, CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "0"); // Heartbeat
            msg.Set(52, DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff"));
            if (!string.IsNullOrEmpty(testReqId))
            {
                msg.Set(112, testReqId);
            }

            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("Heartbeat sent.");
        }

        private async Task SendTestRequestAsync(CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "1"); // TestRequest
            msg.Set(52, DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff"));
            msg.Set(112, DateTime.Now.Ticks.ToString());

            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("TestRequest sent.");
        }

        private async Task SendFixMessageAsync(FixMessage msg, CancellationToken ct)
        {
            var rawFix = msg.Serialize();
            ConsoleLogger.FixOut(rawFix);
            
            var payload = Encoding.ASCII.GetBytes(rawFix);
            var framed = FastFrameEncoder.Encode(payload);
            
            await _tcp.SendAsync(framed, ct);
            _lastSentTime = DateTime.Now;
        }

        private void OnDataReceived(byte[] data, int length)
        {
            foreach (var payload in _decoder.Feed(data, length))
            {
                var rawFix = Encoding.ASCII.GetString(payload);
                ConsoleLogger.FixIn(rawFix);
                
                ProcessFixMessage(FixParser.Parse(rawFix));
            }
        }

        private void ProcessFixMessage(FixMessage msg)
        {
            _lastReceivedTime = DateTime.Now;
            var msgType = msg.GetMsgType();

            switch (msgType)
            {
                case "A": // Logon response
                    HandleLogonResponse(msg);
                    break;
                case "0": // Heartbeat
                    ConsoleLogger.Info("Heartbeat received.");
                    break;
                case "1": // TestRequest
                    ConsoleLogger.Info("TestRequest received, responding with Heartbeat.");
                    var testReqId = msg.Get(112);
                    _ = SendHeartbeatAsync(testReqId, CancellationToken.None);
                    break;
                case "W": // Market Data Snapshot Full Refresh
                    HandleMarketDataSnapshot(msg);
                    break;
                case "5": // Logout
                    ConsoleLogger.Warning("Logout received from server.");
                    break;
                default:
                    ConsoleLogger.Warning($"Unexpected MsgType: {msgType}");
                    break;
            }
        }

        private void HandleMarketDataSnapshot(FixMessage msg)
        {
            var symbol = msg.Get(55);
            var lastPrice = msg.Get(270);
            ConsoleLogger.Success($"[SNAPSHOT] Symbol: {symbol}, LastPrice: {lastPrice}");
            // In a real app, you'd parse MDEntries (tag 268) and notify the UI
        }

        private void HandleLogonResponse(FixMessage msg)
        {
            var status = msg.Get(1409);
            if (status == "0")
            {
                _isLoggedIn = true;
                ConsoleLogger.Success("Logon successful! Session is active.");
            }
            else
            {
                ConsoleLogger.Error($"Logon failed. SessionStatus: {status}");
            }
        }

        private void OnDisconnected()
        {
            _isLoggedIn = false;
            ConsoleLogger.Error("Disconnected from server.");
        }
    }
}
