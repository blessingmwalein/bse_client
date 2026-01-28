using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using BseDashboard.Models;

namespace BseDashboard.Decoder
{
    public class FastDecoder
    {
        private static Dictionary<int, FastTemplate> _templates = new();
        private static Dictionary<string, object> _dictionary = new();
        private static readonly object _lock = new();

        static FastDecoder()
        {
            LoadTemplates();
        }

        private static void LoadTemplates()
        {
            // Heartbeat (Template 64)
            // Layout: PMap(1), TID(1), SeqNum(1), MsgType(1), SendingTime(String)
            var hb = new FastTemplate(64, "Heartbeat");
            hb.Fields.Add(new FastField(34, "SeqNum", FieldType.UInt32, FieldOperator.None));
            hb.Fields.Add(new FastField(35, "MsgType", FieldType.ASCII, FieldOperator.None)); 
            hb.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.None));
            _templates[64] = hb;

            // Application Templates (16320, 16352)
            // They appear to have a different header structure
            _templates[16320] = CreateAppTemplate(16320, "MarketData");
            _templates[16352] = CreateAppTemplate(16352, "SecurityDef");
        }

        private static FastTemplate CreateAppTemplate(int id, string name)
        {
            var t = new FastTemplate(id, name);
            // Based on hex EC-7F-E0-94-32...
            // 94 is SeqNum, 32... is SendingTime
            t.Fields.Add(new FastField(34, "SeqNum", FieldType.UInt32, FieldOperator.None));
            t.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.None));
            return t;
        }

        public static MarketDataEntry Decode(byte[] data)
        {
            var entry = new MarketDataEntry { 
                RawHex = BitConverter.ToString(data),
                SendingTime = DateTime.Now 
            };
            int offset = 0;

            try
            {
                // 1. Read Presence Map (PMap)
                List<bool> pMapBits = new();
                while (offset < data.Length)
                {
                    byte b = data[offset++];
                    for (int i = 6; i >= 0; i--) pMapBits.Add((b & (1 << i)) != 0);
                    if ((b & 0x80) != 0) break; // Last bit of PMap
                }

                // 2. Read Template ID
                int templateId = (int)ReadInteger(data, ref offset);
                entry.TemplateId = templateId;

                Console.WriteLine($"[FAST] TID:{templateId} PMapLen:{pMapBits.Count} Offset:{offset}");

                if (_templates.TryGetValue(templateId, out var template))
                {
                    entry.MsgType = template.Name;
                    int bitIndex = 0;

                    foreach (var field in template.Fields)
                    {
                        bool isPresent = field.Operator == FieldOperator.None || 
                                       (bitIndex < pMapBits.Count && pMapBits[bitIndex++]);

                        object value = null;
                        if (isPresent)
                        {
                            if (field.Type == FieldType.ASCII) value = ReadString(data, ref offset);
                            else value = ReadInteger(data, ref offset);
                        }
                        ApplyFieldToEntry(entry, field, value);
                    }
                }
                
                // Final Fallback: Scrape for symbol and time regardless of template success
                DeepScrape(data, entry);
            }
            catch { DeepScrape(data, entry); }

            return entry;
        }

        private static void DeepScrape(byte[] data, MarketDataEntry entry)
        {
            try {
                string content = Encoding.ASCII.GetString(data);
                
                // Symbol Discovery (BSE instruments are usually 3-8 chars + -EQ)
                var match = Regex.Match(content, @"[A-Z0-9]{2,8}-[A-Z]{2}");
                if (match.Success) entry.Symbol = match.Value;
                else if (content.Contains("FNBB-EQ")) entry.Symbol = "FNBB-EQ";

                // Time Discovery
                var timeMatch = Regex.Match(content, @"\d{8}-\d{2}:\d{2}:\d{2}");
                if (timeMatch.Success) entry.SendingTime = ParseBseTime(timeMatch.Value);

                // Entry Type (Discovery of Bid/Offer/Trade)
                if (content.Contains("Bid")) entry.EntryType = "Bid";
                else if (content.Contains("Offer")) entry.EntryType = "Offer";
                else if (content.Contains("Trade")) entry.EntryType = "Trade";
                
                if (entry.TemplateId == 64) {
                    entry.MsgTypeCode = "0";
                    entry.EntryType = "Admin";
                }
            } catch {}
        }

        private static void ApplyFieldToEntry(MarketDataEntry entry, FastField field, object value)
        {
            if (value == null) return;
            try {
                switch (field.Id)
                {
                    case 35: entry.MsgTypeCode = value.ToString(); break;
                    case 52: entry.SendingTime = ParseBseTime(value.ToString()); break;
                    case 55: entry.Symbol = value.ToString(); break;
                    case 270: entry.Price = SafeDecimal(value) / 10000m; break;
                    case 271: entry.Size = SafeLong(value); break;
                }
            } catch {}
        }

        private static decimal SafeDecimal(object val) 
        {
            if (decimal.TryParse(val?.ToString(), out var d)) return d;
            return 0;
        }

        private static long SafeLong(object val)
        {
            if (long.TryParse(val?.ToString(), out var l)) return l;
            return 0;
        }

        private static long ReadInteger(byte[] data, ref int offset)
        {
            long value = 0;
            while (offset < data.Length)
            {
                byte b = data[offset++];
                value = (value << 7) | (byte)(b & 0x7F);
                if ((b & 0x80) != 0) break;
            }
            return value;
        }

        private static string ReadString(byte[] data, ref int offset)
        {
            StringBuilder sb = new StringBuilder();
            while (offset < data.Length)
            {
                if (offset >= data.Length) break;
                byte b = data[offset++];
                char c = (char)(b & 0x7F);
                if (c >= 32 && c <= 126) sb.Append(c);
                if ((b & 0x80) != 0) break;
            }
            return sb.ToString();
        }

        private static DateTime ParseBseTime(string s)
        {
            try {
                if (string.IsNullOrEmpty(s)) return DateTime.Now;
                if (s.Length >= 17) {
                    if (DateTime.TryParseExact(s.Substring(0, 17), "yyyyMMdd-HH:mm:ss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                        return dt;
                }
                return DateTime.Now;
            } catch { return DateTime.Now; }
        }
    }

    public enum FieldType { ASCII, UInt32, Int32, Decimal }
    public enum FieldOperator { None, Constant, Copy, Tail, Delta }

    public class FastField
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public FieldType Type { get; set; }
        public FieldOperator Operator { get; set; }
        public FastField(int id, string name, FieldType type, FieldOperator op)
        {
            Id = id; Name = name; Type = type; Operator = op;
        }
    }

    public class FastTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<FastField> Fields { get; set; } = new();
        public FastTemplate(int id, string name) { Id = id; Name = name; }
    }
}
