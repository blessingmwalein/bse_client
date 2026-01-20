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
                string username = config.GetValueOrDefault("user", "");
                string password = config.GetValueOrDefault("pass", "");

                string? mIp = config.GetValueOrDefault("m-ip");
                int mPort = int.TryParse(config.GetValueOrDefault("m-port"), out var mp) ? mp : 0;

                using var cts = new System.Threading.CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    ConsoleLogger.Info("Shutdown requested...");
                };

                var tasks = new List<Task>();

                // 1. TCP Snapshot Session (FIX Logon optional)
                var snapshotSession = new SnapshotSession(ip, port,
                    string.IsNullOrEmpty(username) ? null : username,
                    string.IsNullOrEmpty(password) ? null : password);
                tasks.Add(snapshotSession.StartAsync(cts.Token));
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    ConsoleLogger.Warning("FIX credentials not provided. Attempting TCP Snapshot session without authentication.");
                }

                // 2. UDP Multicast Session (Incremental)
                if (!string.IsNullOrEmpty(mIp) && mPort > 0)
                {
                    var multicastSession = new UdpMulticastSession(mIp, mPort);
                    // Wire up recovery: on sequence gap, trigger snapshot/replay via SnapshotSession
                    multicastSession.OnRecoveryRequested += async (symbol, fromSeq, toSeq) =>
                    {
                        if (symbol == null)
                        {
                            ConsoleLogger.Warning($"[RECOVERY] Channel-level gap detected: Requesting snapshot for ApplSeqNum {fromSeq} to {toSeq}");
                            await snapshotSession.RequestFullBookSnapshotAsync(cts.Token);
                        }
                        else
                        {
                            ConsoleLogger.Warning($"[RECOVERY] Instrument-level gap detected for {symbol}: Requesting snapshot for RptSeq {fromSeq} to {toSeq}");
                            await snapshotSession.RequestInstrumentSnapshotAsync(symbol, cts.Token);
                        }
                    };
                    tasks.Add(multicastSession.StartAsync(cts.Token));
                }
                else
                {
                    ConsoleLogger.Warning("UDP Multicast parameters missing. Incremental feed disabled.");
                }

                if (tasks.Count == 0)
                {
                    ConsoleLogger.Error("No valid session parameters provided. Exiting.");
                    return;
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
