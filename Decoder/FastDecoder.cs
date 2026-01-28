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
            var hb = new FastTemplate(64, "Heartbeat");
            hb.Fields.Add(new FastField(34, "SeqNum", FieldType.UInt32, FieldOperator.None));
            hb.Fields.Add(new FastField(35, "MsgType", FieldType.ASCII, FieldOperator.None)); 
            hb.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.None));
            _templates[64] = hb;

            // BSE Template 16320 & 16352 (Observed IDs for IncRefresh/SecDef)
            _templates[16320] = CreateAppTemplate(16320, "MarketData");
            _templates[16352] = CreateAppTemplate(16352, "SecurityDef");
        }

        private static FastTemplate CreateAppTemplate(int id, string name)
        {
            var t = new FastTemplate(id, name);
            // These messages typically have Header -> SeqNum -> Time -> Multi-field body
            t.Fields.Add(new FastField(34, "SeqNum", FieldType.UInt32, FieldOperator.None));
            t.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.None));
            // Symbols and Prices follow in a repeating group or direct fields
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
                    if ((b & 0x80) != 0) break;
                }

                // 2. Read Template ID
                int templateId = (int)ReadInteger(data, ref offset);
                entry.TemplateId = templateId;

                if (_templates.TryGetValue(templateId, out var template))
                {
                    entry.MsgType = template.Name;
                    int bitIndex = 0;

                    foreach (var field in template.Fields)
                    {
                        bool usesPMap = field.Operator != FieldOperator.None && field.Operator != FieldOperator.Constant;
                        bool isPresent = !usesPMap || (bitIndex < pMapBits.Count && pMapBits[bitIndex++]);

                        object value = null;
                        if (isPresent)
                        {
                            if (field.Type == FieldType.ASCII) value = ReadString(data, ref offset);
                            else value = ReadInteger(data, ref offset);
                        }
                        ApplyFieldToEntry(entry, field, value);
                    }
                }
                
                // Fallback: Aggressive Scrape for Symbols (e.g. FNBB-EQ, BIHL-EQ)
                DiscoverFields(data, entry);
            }
            catch { DiscoverFields(data, entry); }

            return entry;
        }

        private static void DiscoverFields(byte[] data, MarketDataEntry entry)
        {
            try {
                string content = Encoding.ASCII.GetString(data);
                
                // 1. Precise Symbol Scanner (Filters out timestamps)
                // Looks for: CAPITAL LETTERS (3-10) and -EQ or -Indices or -CP
                var symMatch = Regex.Match(content, @"[A-Z]{3,10}-(EQ|Indices|CP|Bond)");
                if (symMatch.Success) {
                    entry.Symbol = symMatch.Value;
                }

                // 2. Precise Time Scanner
                var timeMatch = Regex.Match(content, @"\d{8}-\d{2}:\d{2}:\d{2}");
                if (timeMatch.Success) {
                    entry.SendingTime = ParseBseTime(timeMatch.Value);
                }

                // 3. Fix Type Discovery
                if (entry.TemplateId == 64) {
                    entry.MsgTypeCode = "0";
                    entry.EntryType = "Admin";
                }
                else {
                    // Application messages
                    entry.MsgTypeCode = "X";
                    entry.EntryType = "Update";
                    
                    // Crude discovery for Entry Types
                    if (content.Contains("Bid")) entry.EntryType = "Bid";
                    else if (content.Contains("Offer")) entry.EntryType = "Offer";
                    else if (content.Contains("Trade")) entry.EntryType = "Trade";
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
                    case 270: entry.Price = Convert.ToDecimal(value) / 10000m; break;
                    case 271: entry.Size = Convert.ToInt64(value); break;
                }
            } catch {}
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
                var match = Regex.Match(s, @"\d{8}-\d{2}:\d{2}:\d{2}");
                if (match.Success) {
                    return DateTime.ParseExact(match.Value, "yyyyMMdd-HH:mm:ss", null);
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
