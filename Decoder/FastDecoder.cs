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

                if (templateId == 64) // Heartbeat (Based on user capture)
                {
                    entry.MsgType = "Heartbeat";
                    entry.EntryType = "Status";
                    // Tag 35
                    entry.MsgType = ReadString(data, ref offset);
                    // Tag 52
                    string timeStr = ReadString(data, ref offset);
                    if (DateTime.TryParseExact(timeStr.Substring(0, 17), "yyyyMMdd-HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        entry.SendingTime = dt;
                    }
                }
                else if (templateId == 192 || templateId == 193) // Likely Incremental Refresh or SecDef
                {
                    // This is a placeholder for more complex logic
                    // We saw "FNBB-EQ" in a 109 byte packet
                    string content = Encoding.ASCII.GetString(data);
                    if (content.Contains("FNBB-EQ")) entry.Symbol = "FNBB-EQ";
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
