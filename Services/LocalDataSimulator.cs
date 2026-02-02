using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using BseDashboard.Models;
using BseDashboard.Decoder;

namespace BseDashboard.Services
{
    public class LocalDataSimulator : BackgroundService
    {
        private readonly MarketDataService _dataService;
        private readonly string _filePath = "offline_data.txt";

        public LocalDataSimulator(MarketDataService dataService)
        {
            _dataService = dataService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!File.Exists(_filePath)) return;

            Console.WriteLine("Offline Simulator Started. Looping offline_data.txt...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try 
                {
                    var lines = await File.ReadAllLinesAsync(_filePath, stoppingToken);
                    foreach (var line in lines)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            // Strip log timestamp if present [12:00:00]
                            string hexRaw = line;
                            if (line.Contains(']')) hexRaw = line.Split(']').Last();
                            
                            string cleanHex = hexRaw.Trim().Replace("-", "").Replace(" ", "");
                            if (string.IsNullOrEmpty(cleanHex) || cleanHex.Length % 2 != 0) continue;

                            byte[] data = HexToBytes(cleanHex);
                            var entry = FastDecoder.Decode(data);
                            await _dataService.Notify(entry);
                        }
                        catch { /* Skip specific line error */ }

                        await Task.Delay(1000, stoppingToken); 
                    }
                }
                catch { /* Handle file access issues */ }
                
                await Task.Delay(2000, stoppingToken); 
            }
        }

        private byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }
    }
}
