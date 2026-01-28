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

            // Incremental Refresh (Template 193)
            var ir = new FastTemplate(193, "Incremental");
            ir.Fields.Add(new FastField(34, "SeqNum", FieldType.UInt32, FieldOperator.None));
            ir.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.Tail));
            ir.Fields.Add(new FastField(55, "Symbol", FieldType.ASCII, FieldOperator.Copy));
            ir.Fields.Add(new FastField(269, "EntryType", FieldType.ASCII, FieldOperator.Copy));
            ir.Fields.Add(new FastField(270, "Price", FieldType.Decimal, FieldOperator.Delta));
            ir.Fields.Add(new FastField(271, "Size", FieldType.UInt32, FieldOperator.Delta));
            _templates[193] = ir;
        }

        public static MarketDataEntry Decode(byte[] data)
        {
            var entry = new MarketDataEntry { RawHex = BitConverter.ToString(data) };
            int offset = 0;

            try
            {
                // 1. Read Presence Map (PMap)
                // PMaps can be multiple bytes. High bit (0x80) is the extension bit.
                // We collect all bits into a single bit-array.
                List<bool> pMapBits = new();
                while (offset < data.Length)
                {
                    byte b = data[offset++];
                    for (int i = 6; i >= 0; i--) {
                        pMapBits.Add((b & (1 << i)) != 0);
                    }
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
                        bool usesPMap = field.Operator != FieldOperator.None;
                        bool isPresent = !usesPMap || (bitIndex < pMapBits.Count && pMapBits[bitIndex++]);

                        object value = null;
                        switch (field.Operator)
                        {
                            case FieldOperator.None:
                                value = ReadField(data, ref offset, field.Type);
                                break;
                            case FieldOperator.Constant:
                                value = field.ConstantValue;
                                break;
                            case FieldOperator.Copy:
                            case FieldOperator.Tail:
                                if (isPresent) {
                                    value = ReadField(data, ref offset, field.Type);
                                    lock(_lock) _dictionary[field.Name] = value;
                                } else {
                                    lock(_lock) _dictionary.TryGetValue(field.Name, out value);
                                }
                                break;
                            case FieldOperator.Delta:
                                // Delta uses numeric enrichment, for now we read as None
                                value = ReadInteger(data, ref offset);
                                break;
                        }

                        ApplyFieldToEntry(entry, field, value);
                    }
                }
                else
                {
                    entry.MsgType = "Discovering...";
                    DiscoverFields(data, offset, entry);
                }
            }
            catch { DiscoverFields(data, 0, entry); }

            return entry;
        }

        private static void DiscoverFields(byte[] data, int offset, MarketDataEntry entry)
        {
            try {
                // Heuristic Fallback
                string content = Encoding.ASCII.GetString(data);
                var match = Regex.Match(content, @"[A-Z0-9]{3,8}-[A-Z]{2}");
                if (match.Success) entry.Symbol = match.Value;
                if (content.Contains("FNBB-EQ")) entry.Symbol = "FNBB-EQ";
                
                // Try to find the MsgType '0' or 'X' or 'd'
                if (content.Contains("-0")) entry.MsgTypeCode = "0";
                else if (content.Contains("-X")) entry.MsgTypeCode = "X";
                else if (content.Contains("-d")) entry.MsgTypeCode = "d";
            } catch {}
        }

        private static void ApplyFieldToEntry(MarketDataEntry entry, FastField field, object value)
        {
            if (value == null) return;
            string sVal = value.ToString();

            switch (field.Id)
            {
                case 35: entry.MsgTypeCode = sVal; break;
                case 52: entry.SendingTime = ParseBseTime(sVal); break;
                case 55: entry.Symbol = sVal; break;
                case 270: entry.Price = Convert.ToDecimal(value) / 10000m; break;
                case 271: entry.Size = Convert.ToInt64(value); break;
            }
        }

        private static object ReadField(byte[] data, ref int offset, FieldType type)
        {
            if (type == FieldType.ASCII) return ReadString(data, ref offset);
            return ReadInteger(data, ref offset);
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
                // Handle various BSE time formats
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
        public string ConstantValue { get; set; }
        public FastField(int id, string name, FieldType type, FieldOperator op, string constVal = null)
        {
            Id = id; Name = name; Type = type; Operator = op; ConstantValue = constVal;
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
