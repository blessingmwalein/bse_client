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
        // Event to notify when a market data snapshot is received
        public event Action<FixMessage>? OnMarketDataSnapshot;
        private readonly string _ip;
        private readonly int _port;
        private readonly string? _username;
        private readonly string? _password;
        
        private readonly TcpSession _tcp;
        private readonly FastFrameDecoder _decoder = new();
        private bool _isLoggedIn = false;
        private bool _hasRequestedData = false;
        private DateTime _lastReceivedTime = DateTime.MinValue;
        private DateTime _lastSentTime = DateTime.MinValue;
        private readonly int _heartBtInt = 30;

        public SnapshotSession(string ip, int port, string? username = null, string? password = null)
        {
            _ip = ip;
            _port = port;
            _username = username;
            _password = password;
            _tcp = new TcpSession(_ip, _port);
            _tcp.DataReceived += OnDataReceived;
            _tcp.Disconnected += OnDisconnected;
        }

        /// <summary>
        /// Request all security definitions (instrument list).
        /// </summary>
        public async Task RequestSecurityDefinitionsAsync(CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "c"); // Security Definition Request
            msg.Set(320, Guid.NewGuid().ToString()); // SecurityReqID
            msg.Set(321, "8"); // SecurityRequestType = All Securities
            msg.Set(263, "0"); // SubscriptionRequestType = Snapshot
            
            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("Requested Security Definitions (instrument list)");
        }

        /// <summary>
        /// Request market data snapshot for all instruments.
        /// </summary>
        public async Task RequestMarketDataSnapshotAsync(CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "V"); // Market Data Request
            msg.Set(262, Guid.NewGuid().ToString()); // MDReqID
            msg.Set(263, "0"); // SubscriptionRequestType = Snapshot
            msg.Set(264, "0"); // MarketDepth = Full Book
            msg.Set(267, "2"); // NoMDEntryTypes = 2
            msg.Set(269, "0"); // MDEntryType = Bid
            msg.Set(269, "1"); // MDEntryType = Offer
            
            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("Requested Market Data Snapshot (order book)");
        }

        /// <summary>
        /// Request a market data snapshot for a specific symbol (instrument-level recovery).
        /// </summary>
        public async Task RequestInstrumentSnapshotAsync(string symbol, CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "V"); // Market Data Request
            msg.Set(262, Guid.NewGuid().ToString()); // MDReqID
            msg.Set(263, "1"); // Snapshot
            msg.Set(264, "0"); // No updates
            msg.Set(146, "1"); // NoRelatedSym
            msg.Set(55, symbol); // Symbol
            // Add other required tags as per BSE spec
            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info($"Requested snapshot for symbol: {symbol}");
        }

        /// <summary>
        /// Request a full book snapshot (channel-level recovery).
        /// </summary>
        public async Task RequestFullBookSnapshotAsync(CancellationToken ct)
        {
            var msg = new FixMessage();
            msg.Set(35, "V"); // Market Data Request
            msg.Set(262, Guid.NewGuid().ToString()); // MDReqID
            msg.Set(263, "1"); // Snapshot
            msg.Set(264, "0"); // No updates
            // Add other required tags as per BSE spec for full book
            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("Requested full book snapshot");
        }


        public async Task StartAsync(CancellationToken ct)
        {
            try
            {
                ConsoleLogger.Info("Starting Snapshot Session...");
                await _tcp.ConnectAsync(ct);
                ConsoleLogger.Info("TCP connection established. Sending Logon...");
                await SendLogonAsync(ct);
                ConsoleLogger.Info("Waiting for Logon response...");
                var hbLoop = SendHeartbeatLoopAsync(ct);

                // Heartbeat/TestRequest/Timeout logic
                bool testRequestSent = false;
                DateTime? testRequestTime = null;

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(1000, ct);

                    if (_isLoggedIn)
                    {
                        // Request market data once after successful login
                        if (!_hasRequestedData)
                        {
                            _hasRequestedData = true;
                            ConsoleLogger.Info("Logon successful! Requesting market data...");
                            
                            // Request Security Definitions (instrument list)
                            await RequestSecurityDefinitionsAsync(ct);
                            
                            // Wait a bit, then request market data snapshot
                            await Task.Delay(2000, ct);
                            await RequestMarketDataSnapshotAsync(ct);
                        }
                        
                        var idleTime = (DateTime.Now - _lastReceivedTime).TotalSeconds;

                        // If no heartbeat for > 60s, send TestRequest
                        if (!testRequestSent && idleTime > 60)
                        {
                            ConsoleLogger.Warning($"No Heartbeat for {idleTime:F1}s. Sending TestRequest...");
                            await SendTestRequestAsync(ct);
                            testRequestSent = true;
                            testRequestTime = DateTime.Now;
                        }

                        // If no response for > 90s, disconnect and reconnect
                        if (testRequestSent && testRequestTime.HasValue && (DateTime.Now - testRequestTime.Value).TotalSeconds > 30)
                        {
                            ConsoleLogger.Error("No response to TestRequest. Disconnecting and reconnecting...");
                            break;
                        }

                        // Reset if heartbeat received
                        if (!testRequestSent && idleTime <= 60)
                        {
                            testRequestSent = false;
                            testRequestTime = null;
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
            if (!string.IsNullOrEmpty(_username))
                msg.Set(553, _username);
            if (!string.IsNullOrEmpty(_password))
                msg.Set(554, _password);

            await SendFixMessageAsync(msg, ct);
            ConsoleLogger.Info("Logon sent. " +
                (string.IsNullOrEmpty(_username) ? "(No credentials sent)" : "(Credentials sent)"));
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
            ConsoleLogger.Info($"Received {length} bytes from server");
            
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
            // Raise event for application
            OnMarketDataSnapshot?.Invoke(msg);
            var lastPrice = msg.Get(270);
            ConsoleLogger.Success($"[SNAPSHOT] Symbol: {symbol}, LastPrice: {lastPrice}");
            // In a real app, you'd parse MDEntries (tag 268) and notify the UI
        }

        private void HandleLogonResponse(FixMessage msg)
        {
            var status = msg.Get(1409);
            ConsoleLogger.Info($"Processing Logon response. SessionStatus field (1409): {status ?? "not present"}");
            
            if (status == "0")
            {
                _isLoggedIn = true;
                ConsoleLogger.Success("Logon successful! Session is active.");
            }
            else if (status != null)
            {
                ConsoleLogger.Error($"Logon failed. SessionStatus: {status}");
            }
            else
            {
                // SessionStatus not present - might still be successful
                _isLoggedIn = true;
                ConsoleLogger.Warning("Logon response received without SessionStatus field. Assuming success.");
            }
        }

        private void OnDisconnected()
        {
            _isLoggedIn = false;
            ConsoleLogger.Error("Disconnected from server.");
        }
    }
}
