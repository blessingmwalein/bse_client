using System;
using System.Collections.Generic;
using System.Text;
using BseDashboard.Models;

namespace BseDashboard.Decoder
{
    public class FastDecoder
    {
        public static MarketDataEntry Decode(byte[] data)
        {
            var entry = new MarketDataEntry { RawHex = BitConverter.ToString(data) };
            int offset = 0;

            try
            {
                // 1. Read Presence Map (Simplistic for now, first byte)
                byte pMap = data[offset++];
                
                // 2. Read Template ID (Integer)
                long templateId = ReadInteger(data, ref offset);

                if (templateId == 64) // Heartbeat
                {
                    entry.MsgType = "Heartbeat";
                    entry.EntryType = "Status";
                    
                    // Tag 35 - MsgType
                    string msgType = ReadString(data, ref offset);
                    
                    // Tag 52 - SendingTime
                    string timeStr = ReadString(data, ref offset);
                    if (!string.IsNullOrEmpty(timeStr) && timeStr.Length >= 17)
                    {
                        if (DateTime.TryParseExact(timeStr.Substring(0, 17), "yyyyMMdd-HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt))
                        {
                            entry.SendingTime = dt;
                        }
                    }
                    else
                    {
                        entry.SendingTime = DateTime.Now;
                    }
                }
                else if (data.Length > 20) // Generic handler for larger packets
                {
                    string content = Encoding.ASCII.GetString(data);
                    // Search for Symbol (usually in Tag 55 or similar)
                    if (content.Contains("FNBB-EQ")) entry.Symbol = "FNBB-EQ";
                    
                    // Basic heuristic for message type based on observed packets
                    if (data.Length == 109) {
                        entry.MsgType = "SecDef/Incremental";
                        entry.EntryType = "Update";
                    }
                    entry.SendingTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Decode Error: {ex.Message}");
            }

            return entry;
        }

        private static long ReadInteger(byte[] data, ref int offset)
        {
            long value = 0;
            while (offset < data.Length)
            {
                byte b = data[offset++];
                value = (value << 7) | (byte)(b & 0x7F);
                if ((b & 0x80) != 0) break; // Stop bit
            }
            return value;
        }

        private static string ReadString(byte[] data, ref int offset)
        {
            var sb = new StringBuilder();
            while (offset < data.Length)
            {
                byte b = data[offset++];
                sb.Append((char)(b & 0x7F));
                if ((b & 0x80) != 0) break; // Stop bit
            }
            return sb.ToString();
        }
    }
}
