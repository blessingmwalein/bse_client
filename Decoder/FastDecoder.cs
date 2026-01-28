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
            // Template 64: Heartbeat (Admin - Spec 6.2.2.1)
            // "Administrative messages are not field encoded"
            var hb = new FastTemplate(64, "Heartbeat");
            hb.Fields.Add(new FastField(35, "MsgType", FieldType.ASCII, FieldOperator.None));
            hb.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.None));
            _templates[64] = hb;

            // Template 193: Market Data Incremental Refresh (Heuristic)
            var ir = new FastTemplate(193, "IncrementalRefresh");
            ir.Fields.Add(new FastField(35, "MsgType", FieldType.ASCII, FieldOperator.Constant, "X"));
            ir.Fields.Add(new FastField(52, "SendingTime", FieldType.ASCII, FieldOperator.Tail));
            ir.Fields.Add(new FastField(55, "Symbol", FieldType.ASCII, FieldOperator.Copy));
            ir.Fields.Add(new FastField(269, "MDEntryType", FieldType.UInt32, FieldOperator.Copy));
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
                uint pMap = ReadPMap(data, ref offset);

                // 2. Read Template ID
                int templateId = (int)ReadInteger(data, ref offset);
                entry.TemplateId = templateId;

                Console.WriteLine($"[FAST] Processing Template {templateId} | PMap: {Convert.ToString(pMap, 2).PadLeft(7, '0')}");

                if (_templates.TryGetValue(templateId, out var template))
                {
                    entry.MsgType = template.Name;
                    int pMapBit = 0;

                    foreach (var field in template.Fields)
                    {
                        // Some operators don't use PMap bits
                        bool hasPMapBit = field.Operator != FieldOperator.None && field.Operator != FieldOperator.Constant;
                        bool isPresent = !hasPMapBit || (pMap & (1 << (6 - pMapBit++))) != 0;

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
                                // Delta is binary numeric, simple ReadInteger for now
                                value = ReadInteger(data, ref offset);
                                break;
                        }

                        ApplyFieldToEntry(entry, field, value);
                    }
                }
                else
                {
                    entry.MsgType = "Template Not Loaded";
                    // Fallback scanner so user sees something!
                    HeuristicScan(data, entry);
                }
            }
            catch (Exception ex)
            {
                entry.MsgType = "Bitstream Error";
                HeuristicScan(data, entry);
            }

            return entry;
        }

        private static void HeuristicScan(byte[] data, MarketDataEntry entry)
        {
            string content = Encoding.ASCII.GetString(data);
            var match = Regex.Match(content, @"[A-Z0-9]{3,8}-[A-Z]{2}");
            if (match.Success) entry.Symbol = match.Value;
        }

        private static void ApplyFieldToEntry(MarketDataEntry entry, FastField field, object value)
        {
            if (value == null) return;
            switch (field.Id)
            {
                case 35: entry.MsgTypeCode = value.ToString(); break;
                case 52: entry.SendingTime = ParseBseTime(value.ToString()); break;
                case 55: entry.Symbol = value.ToString(); break;
                case 270: entry.Price = Convert.ToDecimal(value) / 100.0m; break;
                case 271: entry.Size = Convert.ToInt64(value); break;
            }
        }

        private static object ReadField(byte[] data, ref int offset, FieldType type)
        {
            if (type == FieldType.ASCII) return ReadString(data, ref offset);
            return ReadInteger(data, ref offset);
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
                sb.Append((char)(b & 0x7F));
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
