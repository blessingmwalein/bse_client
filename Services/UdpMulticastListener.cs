using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Hosting;
using BseDashboard.Models;
using BseDashboard.Decoder;

namespace BseDashboard.Services
{
    public class MarketDataService
    {
        public event Action<MarketDataEntry>? OnNewMessage;
        public void Notify(MarketDataEntry message) => OnNewMessage?.Invoke(message);
    }

    public class UdpMulticastListener : BackgroundService
    {
        private readonly string _mcastGroup = "239.255.190.100";
        private readonly int _port = 30540;
        private readonly MarketDataService _dataService;
        private readonly string _logPath = "raw_feed_log.txt";

        public UdpMulticastListener(MarketDataService dataService)
        {
            _dataService = dataService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));

            try
            {
                udp.JoinMulticastGroup(IPAddress.Parse(_mcastGroup));
                Console.WriteLine($"Listening for BSE Market Data on {_mcastGroup}:{_port}...");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync(stoppingToken);
                    var entry = FastDecoder.Decode(result.Buffer);
                    
                    // Log raw hex for offline decoding work
                    try {
                        string hex = BitConverter.ToString(result.Buffer) + Environment.NewLine;
                        await File.AppendAllTextAsync(_logPath, hex, stoppingToken);
                    } catch { /* Suppress logging errors to keep feed live */ }

                    _dataService.Notify(entry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP Error: {ex.Message}");
            }
            finally
            {
                udp.DropMulticastGroup(IPAddress.Parse(_mcastGroup));
            }
        }
    }
}
