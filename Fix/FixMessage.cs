using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BseMarketDataClient.Fix
{
    public class FixMessage
    {
        private readonly List<KeyValuePair<int, string>> _fields = new();

        public void Set(int tag, string value)
        {
            var index = _fields.FindIndex(f => f.Key == tag);
            if (index >= 0)
                _fields[index] = new KeyValuePair<int, string>(tag, value);
            else
                _fields.Add(new KeyValuePair<int, string>(tag, value));
        }

        public void Set(int tag, int value) => Set(tag, value.ToString());

        public string? Get(int tag)
        {
            return _fields.FirstOrDefault(f => f.Key == tag).Value;
        }

        public string GetMsgType() => Get(35) ?? string.Empty;

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var field in _fields)
            {
                sb.Append(field.Key).Append('=').Append(field.Value).Append('\u0001');
            }
            return sb.ToString();
        }

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
