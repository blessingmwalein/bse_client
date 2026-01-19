using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BseMarketDataClient.Logging;

namespace BseMarketDataClient.Networking
{
    public class TcpSession : IDisposable
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly string _ip;
        private readonly int _port;
        private bool _disposed;

        public event Action<byte[], int>? DataReceived;
        public event Action? Disconnected;

        public TcpSession(string ip, int port)
        {
            _ip = ip;
            _port = port;
        }

        public async Task ConnectAsync(CancellationToken ct)
        {
            _client = new TcpClient();
            ConsoleLogger.Info($"Connecting to {_ip}:{_port}...");
            await _client.ConnectAsync(_ip, _port, ct);
            _stream = _client.GetStream();
            ConsoleLogger.Success("Connected.");
            
            _ = ReceiveLoopAsync(ct);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            try
            {
                while (!ct.IsCancellationRequested && _client?.Connected == true)
                {
                    int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (bytesRead == 0) break; // Server disconnected

                    DataReceived?.Invoke(buffer, bytesRead);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                ConsoleLogger.Error($"TCP Receive error: {ex.Message}");
            }
            finally
            {
                Disconnected?.Invoke();
            }
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (_stream == null) return;
            await _stream.WriteAsync(data, 0, data.Length, ct);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _client?.Dispose();
            _stream?.Dispose();
            _disposed = true;
        }
    }
}
