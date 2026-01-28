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
            // Template 64: Heartbeat 
            // Hex: 9E C0 83 B0 ... -> PMap 9E, TID C0(64), SeqNum 83(3), MsgType B0('0')
            var hb = new FastTemplate(64, "Heartbeat");
            hb.Fields.Add(new FastField(34, "SeqNum", FieldType.UInt32, FieldOperator.None));
            hb.Fields.Add(new FastField(35, "MsgType", FieldType.ASCII, FieldOperator.None));
            hb.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.None));
            _templates[64] = hb;

            // Template 193: Incremental Refresh
            var ir = new FastTemplate(193, "Incremental");
            ir.Fields.Add(new FastField(35, "MsgType", FieldType.ASCII, FieldOperator.Constant, "X"));
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
                uint pMap = 0;
                while (offset < data.Length) {
                    byte b = data[offset++];
                    pMap = (pMap << 7) | (uint)(b & 0x7F);
                    if ((b & 0x80) != 0) break;
                }

                // 2. Read Template ID
                int templateId = (int)ReadInteger(data, ref offset);
                entry.TemplateId = templateId;

                Console.WriteLine($"[FAST] Processing TID:{templateId} PMap:{Convert.ToString(pMap, 2)} Len:{data.Length}");

                if (_templates.TryGetValue(templateId, out var template))
                {
                    entry.MsgType = template.Name;
                    int pMapBit = 0;

                    foreach (var field in template.Fields)
                    {
                        // Boolean: Does this field occupy a bit in the PMap?
                        // Mandatory "None" fields do NOT use PMap bits.
                        // Optional or Operator fields (Constant, Copy, Tail, Delta) DO use bits.
                        bool usesPMap = field.Operator != FieldOperator.None;
                        
                        // We consume bits from the PMap starting from the MOST significant bit (Bit 6 of the 7-bit PMap byte)
                        bool isPresent = !usesPMap || (pMap & (1 << (15 - pMapBit++))) != 0; // Simplified PMap bit walk

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
                                value = ReadInteger(data, ref offset);
                                break;
                        }

                        ApplyFieldToEntry(entry, field, value);
                    }
                }
                else
                {
                    entry.MsgType = "Unknown Template";
                    HeuristicScan(data, entry);
                }
            }
            catch { HeuristicScan(data, entry); }

            return entry;
        }

        private static void HeuristicScan(byte[] data, MarketDataEntry entry)
        {
            try {
                string content = Encoding.ASCII.GetString(data);
                var match = Regex.Match(content, @"[A-Z0-9]{3,8}-[A-Z]{2}");
                if (match.Success) entry.Symbol = match.Value;
                if (content.Contains("FNBB-EQ")) entry.Symbol = "FNBB-EQ";
            } catch {}
        }

        private static void ApplyFieldToEntry(MarketDataEntry entry, FastField field, object value)
        {
            if (value == null) return;
            switch (field.Id)
            {
                case 35: entry.MsgTypeCode = value.ToString(); break;
                case 52: entry.SendingTime = ParseBseTime(value.ToString()); break;
                case 55: entry.Symbol = value.ToString(); break;
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
                if (c != 0) sb.Append(c);
                if ((b & 0x80) != 0) break;
            }
            return sb.ToString();
        }

        private static DateTime ParseBseTime(string s)
        {
            try {
                if (string.IsNullOrEmpty(s) || s.Length < 17) return DateTime.Now;
                return DateTime.ParseExact(s.Substring(0, 17), "yyyyMMdd-HH:mm:ss", null);
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
