using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BseDashboard.Models;

namespace BseDashboard.Decoder
{
    public class FastDecoder
    {
        // FAST Dictionary to handle "Copy" and "Tail" encoding operators
        private static Dictionary<string, string> _dictionary = new();

        public static MarketDataEntry Decode(byte[] data)
        {
            var entry = new MarketDataEntry { RawHex = BitConverter.ToString(data) };
            int offset = 0;

            try
            {
                // 1. Read Presence Map (PMap)
                // PMaps can be multiple bytes if the 7th bit is 0. 
                // We'll skip it for simple templates, but follow the spec for application msgs.
                byte pMap = data[offset++];
                if ((pMap & 0x80) == 0) offset++; // Simple multi-byte skip

                // 2. Read Template ID
                entry.TemplateId = (int)ReadInteger(data, ref offset);

                if (entry.TemplateId == 64) // Heartbeat (Administrative)
                {
                    entry.MsgTypeCode = "0";
                    entry.MsgType = "Heartbeat";
                    
                    // Tag 35: ASCII String (None Encoding)
                    string mType = ReadString(data, ref offset); 
                    
                    // Tag 52: SendingTime (None Encoding - YYYYMMDD-HH:MM:SS.uuuuuu)
                    string timeStr = ReadString(data, ref offset);
                    entry.SendingTime = ParseBseTime(timeStr);
                    entry.EntryType = "Admin";
                }
                else if (entry.TemplateId >= 100) // Application Messages (Field Encoded)
                {
                    // Heuristic for Incremental Refresh / SecDef
                    entry.MsgType = "Market Data";
                    entry.SendingTime = DateTime.Now;

                    // According to Section 6.2.2.2:
                    // Tag 35: ASCII String (Constant) -> Usually not in bitstream if constant
                    // Tag 52: ASCII String (Tail Encoding)
                    
                    // Since we are "frank" and don't have the dictionary yet, 
                    // we scan for the Symbol pattern which is often 'None' or 'Copy' encoded
                    string content = Encoding.ASCII.GetString(data);
                    var match = Regex.Match(content, @"[A-Z0-9]{2,8}-[A-Z]{2}");
                    if (match.Success) 
                    {
                        entry.Symbol = match.Value;
                        entry.MsgType = "IncRefresh/SecDef";
                    }

                    // Extract numeric values (Bid/Offer logic)
                    // This moves offset forward through the fields
                    try {
                        long val1 = ReadInteger(data, ref offset);
                        long val2 = ReadInteger(data, ref offset);
                        entry.Price = val1 / 10000m; // Heuristic for price decimal
                        entry.Size = val2;
                    } catch {}
                }
            }
            catch (Exception)
            {
                // Safe return if bitstream is truncated
            }

            return entry;
        }

        private static DateTime ParseBseTime(string s)
        {
            try {
                if (s.Length < 17) return DateTime.Now;
                // Format: 20260128-11:14:23.28082
                return DateTime.ParseExact(s.Substring(0, 17), "yyyyMMdd-HH:mm:ss", null);
            } catch { return DateTime.Now; }
        }

        public static long ReadInteger(byte[] data, ref int offset)
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

        public static string ReadString(byte[] data, ref int offset)
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
    }
}
