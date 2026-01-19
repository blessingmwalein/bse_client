using System;

namespace BseMarketDataClient.Logging
{
    public static class ConsoleLogger
    {
        public static void Info(string message) => Log("INFO", message, ConsoleColor.White);
        public static void Success(string message) => Log("SUCCESS", message, ConsoleColor.Green);
        public static void Warning(string message) => Log("WARN", message, ConsoleColor.Yellow);
        public static void Error(string message) => Log("ERROR", message, ConsoleColor.Red);

        public static void FixIn(string rawFix) => Log("FIX IN", FormatFix(rawFix), ConsoleColor.Cyan);
        public static void FixOut(string rawFix) => Log("FIX OUT", FormatFix(rawFix), ConsoleColor.Magenta);

        private static void Log(string level, string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ");
            Console.ForegroundColor = color;
            Console.Write($"[{level}] ");
            Console.ForegroundColor = originalColor;
            Console.WriteLine(message);
        }

        private static string FormatFix(string rawFix)
        {
            // Replace SOH characters with | for readability
            return rawFix.Replace('\u0001', '|');
        }
    }
}
