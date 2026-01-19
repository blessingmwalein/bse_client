using System;

namespace BseMarketDataClient.Fix
{
    public static class FixParser
    {
        public static FixMessage Parse(string rawFix)
        {
            var msg = new FixMessage();
            var pairs = rawFix.Split('\u0001', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && int.TryParse(kv[0], out int tag))
                {
                    msg.Set(tag, kv[1]);
                }
            }
            return msg;
        }
    }
}
