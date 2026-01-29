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
        // BSE Market Data Field Mapping (Heuristic)
        private static Dictionary<int, FastTemplate> _templates = new();
        private static Dictionary<string, object> _dictionary = new();
        private static readonly object _lock = new();

        static FastDecoder()
        {
            // Heartbeat (Standard)
            var hb = new FastTemplate(64, "Heartbeat");
            hb.Fields.Add(new FastField(34, "SeqNum", FieldType.UInt32, FieldOperator.None));
            hb.Fields.Add(new FastField(35, "MsgType", FieldType.ASCII, FieldOperator.None)); 
            hb.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.None));
            _templates[64] = hb;
        }

        public static MarketDataEntry Decode(byte[] data)
        {
            var entry = new MarketDataEntry { 
                RawHex = BitConverter.ToString(data),
                SendingTime = DateTime.Now,
                Symbol = "-",
                MsgTypeCode = "X",
                EntryType = "Update"
            };

            int offset = 0;

            try
            {
                // 1. Detect and Skip Potential PDU Headers
                // BSE packets sometimes have 1-5 bytes of header before PMap
                // If the first byte doesn't look like a PMap (0x80 set elsewhere), 
                // we scan for the first valid PMap/TID sequence.
                if (data[0] == 0xF8 || data[0] == 0x01) offset = FindStartOfFast(data);

                // 2. Read Presence Map (PMap)
                uint pMap = ReadPMap(data, ref offset);

                // 3. Read Template ID
                int templateId = (int)ReadInteger(data, ref offset);
                entry.TemplateId = templateId;

                // 4. Decode if known, or DeepScrape if unknown
                if (_templates.TryGetValue(templateId, out var template))
                {
                    entry.MsgType = template.Name;
                    DecodeFields(data, ref offset, template, pMap, entry);
                }
                
                // 5. Always perform DeepScrape to catch Symbol, Price, Size
                DeepScrape(data, entry);
            }
            catch { DeepScrape(data, entry); }

            return entry;
        }

        private static int FindStartOfFast(byte[] data)
        {
            // Scan for common TID patterns like 64 (0x40|0x80 = 0xC0) or 16xxx (0x7F 0xC0/E0)
            for (int i = 0; i < Math.Min(data.Length - 2, 8); i++)
            {
                if (data[i] == 0xC0) return Math.Max(0, i - 1); // 1-byte PMap before TID 64
                if (data[i] == 0x7F && (data[i+1] & 0x80) != 0) return Math.Max(0, i - 1);
            }
            return 0;
        }

        private static void DecodeFields(byte[] data, ref int offset, FastTemplate template, uint pMap, MarketDataEntry entry)
        {
            int bitIndex = 0;
            foreach (var field in template.Fields)
            {
                bool usesPMap = field.Operator != FieldOperator.None;
                bool isPresent = !usesPMap || (pMap & (1u << (15 - bitIndex++))) != 0;

                if (isPresent)
                {
                    object value = (field.Type == FieldType.ASCII) ? ReadString(data, ref offset) : ReadInteger(data, ref offset);
                    ApplyFieldToEntry(entry, field, value);
                }
            }
        }

        private static void DeepScrape(byte[] data, MarketDataEntry entry)
        {
            try {
                string content = Encoding.ASCII.GetString(data);
                
                // Precise Symbol Discovery
                var symMatch = Regex.Match(content, @"[A-Z]{2,10}-(EQ|Indices|CP|Bond)");
                if (symMatch.Success) {
                    entry.Symbol = symMatch.Value;
                    
                    // Look for Price/Size immediately after the symbol in the hex
                    int symOffset = FindSubArray(data, Encoding.ASCII.GetBytes(entry.Symbol));
                    if (symOffset != -1)
                    {
                        int dataOffset = symOffset + entry.Symbol.Length;
                        
                        // Extract Price (Patterns like 82-8D -> 2.13 or B1-82 -> 0.49?)
                        // In many BSE packets, the next few bytes with 0x80 bits are the values
                        List<long> values = new();
                        int scanOffset = dataOffset;
                        while(values.Count < 5 && scanOffset < data.Length)
                        {
                            if ((data[scanOffset] & 0x80) != 0)
                            {
                                values.Add(data[scanOffset] & 0x7F);
                            }
                            scanOffset++;
                        }

                        if (values.Count >= 2)
                        {
                            // If we see 2 and 87 -> 2.87
                            entry.Price = values[0] + (values[1] / 100m);
                            entry.Size = values.Count > 2 ? values[values.Count-1] : 0;
                        }
                    }
                }

                // Time Scrape
                var timeMatch = Regex.Match(content, @"\d{8}-\d{2}:\d{2}:\d{2}");
                if (timeMatch.Success) entry.SendingTime = ParseBseTime(timeMatch.Value);

                if (entry.TemplateId == 64) {
                    entry.MsgTypeCode = "0";
                    entry.EntryType = "Admin";
                }
            } catch {}
        }

        private static int FindSubArray(byte[] source, byte[] target)
        {
            for (int i = 0; i <= source.Length - target.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < target.Length; j++)
                {
                    if (source[i + j] != target[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static void ApplyFieldToEntry(MarketDataEntry entry, FastField field, object value)
        {
            if (value == null) return;
            switch (field.Id)
            {
                case 35: entry.MsgTypeCode = value.ToString(); break;
                case 52: entry.SendingTime = ParseBseTime(value.ToString()); break;
                case 55: entry.Symbol = value.ToString(); break;
            }
        }

        private static uint ReadPMap(byte[] data, ref int offset)
        {
            uint pMap = 0;
            while (offset < data.Length)
            {
                byte b = data[offset++];
                pMap = (pMap << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) != 0) break;
            }
            return pMap;
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
                var match = Regex.Match(s, @"\d{8}-\d{2}:\d{2}:\d{2}");
                if (match.Success) return DateTime.ParseExact(match.Value, "yyyyMMdd-HH:mm:ss", null);
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
