using System;
using System.IO;
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
    {
        // Event to notify when a decoded market data payload is received
        public event Action<byte[]>? OnMarketData;
        // Event to request recovery (e.g., snapshot/replay) on sequence gap
        public event Action<string?, long, long>? OnRecoveryRequested;
        private readonly string _multicastIp;
        private readonly int _port;
        private UdpClient? _udpClient;
        private readonly FastFrameDecoder _decoder = new();
        private bool _disposed;
        // Sequence tracking
        private long _lastApplSeqNum = 0;
        private readonly Dictionary<string, long> _lastRptSeq = new();
        // File logging for raw data capture
        private StreamWriter? _rawDataLog;
        private StreamWriter? _decodedDataLog;
        private readonly bool _enableFileLogging;
        private int _packetCount = 0;
        private int _messageCount = 0;

        public UdpMulticastSession(string multicastIp, int port, bool enableFileLogging = true)
        {
            _multicastIp = multicastIp;
            _port = port;
            _enableFileLogging = enableFileLogging;
            
            if (_enableFileLogging)
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var logDir = Path.Combine(Directory.GetCurrentDirectory(), "market_data_logs");
                Directory.CreateDirectory(logDir);
                
                _rawDataLog = new StreamWriter(Path.Combine(logDir, $"raw_udp_{timestamp}.bin"));
                _decodedDataLog = new StreamWriter(Path.Combine(logDir, $"decoded_messages_{timestamp}.txt"));
                
                ConsoleLogger.Info($"File logging enabled. Logs will be saved to: {logDir}");
            }
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
            _packetCount++;
            
            // Log raw packet data
            if (_enableFileLogging && _rawDataLog != null)
            {
                _rawDataLog.WriteLine($"=== Packet {_packetCount} at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
                _rawDataLog.WriteLine($"Length: {data.Length} bytes");
                _rawDataLog.WriteLine($"Hex: {BitConverter.ToString(data).Replace("-", " ")}");
                _rawDataLog.WriteLine();
                _rawDataLog.Flush();
            }
            
            foreach (var payload in _decoder.Feed(data, data.Length))
            {
                _messageCount++;
                OnMarketData?.Invoke(payload);
                var rawFix = Encoding.ASCII.GetString(payload);
                
                // Log decoded message
                if (_enableFileLogging && _decodedDataLog != null)
                {
                    _decodedDataLog.WriteLine($"=== Message {_messageCount} at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
                    _decodedDataLog.WriteLine($"Raw FIX: {rawFix.Replace('\u0001', '|')}");
                    _decodedDataLog.WriteLine();
                    _decodedDataLog.Flush();
                }
                
                ConsoleLogger.FixIn(rawFix);
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
            
            ConsoleLogger.Info($"UDP session closing. Received {_packetCount} packets, {_messageCount} messages.");
            
            _rawDataLog?.Close();
            _decodedDataLog?.Close();
            _udpClient?.Dispose();
            _disposed = true;
        }
    }
}
