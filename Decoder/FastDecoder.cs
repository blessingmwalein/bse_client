using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BseDashboard.Models;

namespace BseDashboard.Decoder
{
    public class FastDecoder
    {
        // Dictionary to store "previous" values for COPY and TAIL operators
        private static Dictionary<string, object> _dictionary = new();
        private static readonly object _dictLock = new();

        public static MarketDataEntry Decode(byte[] data)
        {
            var entry = new MarketDataEntry { RawHex = BitConverter.ToString(data) };
            int offset = 0;

            try
            {
                // FAST PMap (Presence Map)
                // PMaps end when the 7th bit (0x80) is set.
                uint pMap = ReadPMap(data, ref offset);

                // Template ID
                entry.TemplateId = (int)ReadInteger(data, ref offset);

                if (entry.TemplateId == 64) // Heartbeat (Administrative - None Encoding)
                {
                    entry.MsgTypeCode = "0";
                    entry.MsgType = "Heartbeat";
                    entry.EntryType = "Admin";
                    
                    // Tag 35: ASCII String (None)
                    ReadString(data, ref offset); 
                    
                    // Tag 52: SendingTime (None)
                    string timeStr = ReadString(data, ref offset);
                    entry.SendingTime = ParseBseTime(timeStr);
                }
                else // Application Messages (Market Data / SecDef)
                {
                    entry.MsgType = "Market Data";
                    entry.EntryType = "Status";
                    entry.SendingTime = DateTime.Now;

                    // In a full implementation, we'd use the PMap to decide which fields to read.
                    // For BSE, symbols are often Template 192 (SecDef) or 193 (IncRefresh).
                    
                    // Let's implement a 'Dictionary-Aware' scan.
                    // If we see a string that looks like a Symbol, we save it.
                    // If a future message has the 'Symbol' bit' off in PMap, we use the saved one.
                    
                    string foundSymbol = ScanForSymbol(data);
                    lock (_dictLock)
                    {
                        if (!string.IsNullOrEmpty(foundSymbol))
                        {
                            _dictionary["Symbol"] = foundSymbol;
                            entry.Symbol = foundSymbol;
                        }
                        else if (_dictionary.TryGetValue("Symbol", out var saved))
                        {
                            entry.Symbol = (string)saved;
                        }
                    }

                    // Extract first available numbers as Price/Size (Heuristic for now)
                    // In Template 193, these are Delta/Copy encoded.
                    try {
                        long val1 = ReadInteger(data, ref offset);
                        long val2 = ReadInteger(data, ref offset);
                        entry.Price = val1 / 100.0m;
                        entry.Size = val2;
                    } catch {}
                    
                    entry.MsgType = $"BSE Template {entry.TemplateId}";
                }
            }
            catch (Exception)
            {
                // Return what we have
            }

            return entry;
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

        public static long ReadInteger(byte[] data, ref int offset)
        {
            long value = 0;
            bool negative = (data[offset] & 0x40) != 0; // Handle signed if needed
            
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

        private static string ScanForSymbol(byte[] data)
        {
            try {
                string content = Encoding.ASCII.GetString(data);
                var match = Regex.Match(content, @"[A-Z0-9]{2,10}-[A-Z]{2}");
                return match.Success ? match.Value : null;
            } catch { return null; }
        }

        private static DateTime ParseBseTime(string s)
        {
            try {
                if (s.Length < 17) return DateTime.Now;
                return DateTime.ParseExact(s.Substring(0, 17), "yyyyMMdd-HH:mm:ss", null);
            } catch { return DateTime.Now; }
        }
    }
}
