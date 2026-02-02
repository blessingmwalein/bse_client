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
        private readonly string _logsDir = "Logs";
        public event Action<MarketDataEntry>? OnNewMessage;

        public MarketDataService()
        {
            if (!Directory.Exists(_logsDir)) Directory.CreateDirectory(_logsDir);
        }

        public async Task Notify(MarketDataEntry entry)
        {
            try {
                string today = DateTime.Now.ToString("yyyyMMdd");
                string csvPath = Path.Combine(_logsDir, $"decoded_{today}.csv");
                string rawLog = Path.Combine(_logsDir, $"raw_{today}.log");

                // Write header if new file
                if (!File.Exists(csvPath))
                {
                    await File.WriteAllTextAsync(csvPath, "Time,Symbol,Type,Price,Size,TemplateId,RawHex" + Environment.NewLine);
                }

                // Log Raw
                string hexLog = $"[{DateTime.Now:HH:mm:ss}] {entry.RawHex}{Environment.NewLine}";
                await File.AppendAllTextAsync(rawLog, hexLog);

                // Log Decoded
                if (entry.Symbol != "-")
                {
                    string csvEntry = $"{entry.SendingTime:HH:mm:ss.fff},{entry.Symbol},{entry.EntryType},{entry.Price},{entry.Size},{entry.TemplateId},{entry.RawHex}{Environment.NewLine}";
                    await File.AppendAllTextAsync(csvPath, csvEntry);
                }
            } catch { /* Suppress logging errors */ }

            OnNewMessage?.Invoke(entry);
        }
    }

    public class UdpMulticastListener : BackgroundService
    {
        private readonly string _mcastGroup = "239.255.190.100";
        private readonly int _port = 30540;
        private readonly MarketDataService _dataService;
        private readonly string _logsDir = "Logs";

        public UdpMulticastListener(MarketDataService dataService)
        {
            _dataService = dataService;
            if (!Directory.Exists(_logsDir)) Directory.CreateDirectory(_logsDir);
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
                    await _dataService.Notify(entry);
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
