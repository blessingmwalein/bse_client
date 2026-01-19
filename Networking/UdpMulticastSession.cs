using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BseMarketDataClient.Fast;
using BseMarketDataClient.Fix;
using BseMarketDataClient.Logging;

namespace BseMarketDataClient.Networking
{
    public class UdpMulticastSession : IDisposable
        // Event to notify when a decoded market data payload is received
        public event Action<byte[]>? OnMarketData;
    {
        private readonly string _multicastIp;
        private readonly int _port;
        private UdpClient? _udpClient;
        private readonly FastFrameDecoder _decoder = new();
        private bool _disposed;

        public UdpMulticastSession(string multicastIp, int port)
        {
            _multicastIp = multicastIp;
            _port = port;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            try
            {
                _udpClient = new UdpClient(_port, AddressFamily.InterNetwork);
                var multicastAddress = IPAddress.Parse(_multicastIp);
                _udpClient.JoinMulticastGroup(multicastAddress);

                ConsoleLogger.Success($"Joined multicast group {_multicastIp}:{_port}");

                while (!ct.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync(ct);
                    ProcessData(result.Buffer);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                ConsoleLogger.Error($"UDP Multicast error: {ex.Message}");
            }
            finally
            {
                Dispose();
            }
        }

        private void ProcessData(byte[] data)
        {
            foreach (var payload in _decoder.Feed(data, data.Length))
            {
                OnMarketData?.Invoke(payload);
                var rawFix = Encoding.ASCII.GetString(payload);
                // ConsoleLogger.FixIn(rawFix); // Can be very noisy for multicast
                var msg = FixParser.Parse(rawFix);
                HandleIncrementalUpdate(msg);
            }
        }

        private void HandleIncrementalUpdate(FixMessage msg)
        {
            var msgType = msg.GetMsgType();
            if (msgType == "X") // Market Data Incremental Refresh
            {
                var symbol = msg.Get(55);
                var lastPrice = msg.Get(270);
                if (symbol != null)
                {
                    ConsoleLogger.Info($"[UDP-INC] Symbol: {symbol}, LastPrice: {lastPrice}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _udpClient?.Dispose();
            _disposed = true;
        }
    }
}
