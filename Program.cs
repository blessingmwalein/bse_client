using System;
using System.Threading.Tasks;
using BseMarketDataClient.Session;
using BseMarketDataClient.Logging;
using BseMarketDataClient.Networking;
using System.Collections.Generic;
using System.Linq;

namespace BseMarketDataClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ConsoleLogger.Info("BSE Market Data Client starting...");
            
            var config = ParseArgs(args);
            if (config.ContainsKey("help"))
            {
                PrintHelp();
                return;
            }

            try
            {
                string ip = config.GetValueOrDefault("ip", "192.168.90.18");
                int port = int.Parse(config.GetValueOrDefault("port", "30540"));
                string username = config.GetValueOrDefault("user", "YOUR_USERNAME");
                string password = config.GetValueOrDefault("pass", "YOUR_PASSWORD");
                
                string? mIp = config.GetValueOrDefault("m-ip");
                int mPort = int.Parse(config.GetValueOrDefault("m-port", "0"));

                using var cts = new System.Threading.CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    ConsoleLogger.Info("Shutdown requested...");
                };

                var tasks = new List<Task>();

                // 1. TCP Snapshot Session
                var snapshotSession = new SnapshotSession(ip, port, username, password);
                tasks.Add(snapshotSession.StartAsync(cts.Token));

                // 2. UDP Multicast Session (Incremental)
                if (!string.IsNullOrEmpty(mIp) && mPort > 0)
                {
                    var multicastSession = new UdpMulticastSession(mIp, mPort);
                    tasks.Add(multicastSession.StartAsync(cts.Token));
                }
                else
                {
                    ConsoleLogger.Warning("UDP Multicast parameters missing. Incremental feed disabled.");
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                ConsoleLogger.Error($"Application terminated unexpectedly: {ex.Message}");
            }
            
            ConsoleLogger.Info("BSE Market Data Client stopped.");
        }

        static Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--") && i + 1 < args.Length)
                {
                    dict[args[i].Substring(2)] = args[i + 1];
                    i++;
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    dict["help"] = "true";
                }
            }
            return dict;
        }

        static void PrintHelp()
        {
            Console.WriteLine("Usage: dotnet run -- [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --ip <ip>          TCP Snapshot Server IP");
            Console.WriteLine("  --port <port>      TCP Snapshot Server Port");
            Console.WriteLine("  --user <user>      FIX Logon Username");
            Console.WriteLine("  --pass <pass>      FIX Logon Password");
            Console.WriteLine("  --m-ip <ip>        UDP Multicast Group IP");
            Console.WriteLine("  --m-port <port>    UDP Multicast Port");
            Console.WriteLine("  --help, -h         Show this message");
        }
    }
}
