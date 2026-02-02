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
        // FAST State Management (Dictionaries for Copy/Delta operators)
        private static readonly Dictionary<int, long> _intDictionary = new();
        private static readonly Dictionary<int, decimal> _decimalDictionary = new();
        private static readonly Dictionary<int, string> _stringDictionary = new();
        private static readonly object _stateLock = new();

        public static MarketDataEntry Decode(byte[] data)
        {
            var entry = new MarketDataEntry { 
                RawHex = BitConverter.ToString(data),
                SendingTime = DateTime.Now,
                Symbol = "-",
                MsgTypeCode = "?",
                EntryType = "Status"
            };

            try
            {
                int offset = 0;
                
                // 1. Read PMap (Prefix Map)
                // PMaps in FAST are bitmasks indicating which fields are present/modified.
                // We'll skip it for now and look for the Template ID.
                uint pMap = (uint)ReadUInt(data, ref offset); 
                
                // 2. Read Template ID (Usually follows PMap)
                entry.TemplateId = (int)ReadUInt(data, ref offset);

                if (entry.TemplateId == 64) // Heartbeat
                {
                    entry.MsgTypeCode = "0";
                    entry.EntryType = "Admin";
                    entry.MsgType = "Heartbeat";
                    // Skip rest of administrative fields for brevity or use Scrape
                }
                else if (entry.TemplateId == 193 || entry.TemplateId == 115) // Market Data Incremental Refresh
                {
                    entry.MsgTypeCode = "X";
                    entry.EntryType = "Update";
                    entry.MsgType = "MarketData";
                    
                    // Structural Decode for Template 193
                    // According to templates.xml:
                    // MsgType (35), SendingTime (52), Sequence MDEntries [NoMDEntries (268), Symbol (55), MDEntryType (269), MDEntryPx (270), MDEntrySize (271)]
                    
                    // Skip MsgType (Constant 'X') and SendingTime (Tail)
                    // In a real FAST engine, we'd update SendingTime in the dictionary.
                    
                    // We'll use the DeepScrape for the variable length string fields 
                    // and use the dictionary for the numerical Delta fields.
                }

                // 3. Fallback / Hybrid Scrape
                // BSE feed often has plaintext-like segments (Symbol names, Dates)
                DeepScrape(data, entry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Decoder Error] {ex.Message}");
            }

            return entry;
        }

        private static void DeepScrape(byte[] data, MarketDataEntry entry)
        {
            try {
                // 1. Time Scrape - Scan for potential BSE date strings (yyyyMMdd-HH:mm:ss)
                string fullContent = Encoding.ASCII.GetString(data.Select(b => (byte)(b & 0x7F)).ToArray());
                var timeMatch = Regex.Match(fullContent, @"(\d{8}-\d{2}:\d{2}:\d{2})");
                if (timeMatch.Success) entry.SendingTime = ParseBseTime(timeMatch.Value);

                // 2. Custom Symbol & Value Scraper
                // We scan the raw bytes for sequences that match BSE symbol patterns (e.g., ABC-EQ)
                // but we handle the FAST stop-bit (0x80) on the last character.
                for (int i = 0; i < data.Length - 5; i++)
                {
                    int length = 0;
                    bool hasDash = false;
                    
                    // Scan for Symbol: Uppercase letters, digits, or '-'
                    while (i + length < data.Length && length < 15)
                    {
                        byte b = data[i + length];
                        byte pure = (byte)(b & 0x7F);
                        if ((pure >= 'A' && pure <= 'Z') || (pure >= '0' && pure <= '9') || pure == '-')
                        {
                            if (pure == '-') hasDash = true;
                            length++;
                            if ((b & 0x80) != 0) break; // FAST Stop Bit
                        }
                        else break;
                    }

                    if (length >= 4 && hasDash)
                    {
                        // Found a potential symbol
                        StringBuilder sb = new();
                        for (int k = 0; k < length; k++) sb.Append((char)(data[i + k] & 0x7F));
                        entry.Symbol = sb.ToString();

                        // Clean common BSE suffixes if needed (though usually we want the full symbol)
                        // entry.Symbol = entry.Symbol.TrimEnd('O', 'Q'); 

                        // 3. Extract Numerical Values (Price, Size) following the symbol
                        int scanOffset = i + length;
                        List<long> values = new();
                        while (values.Count < 10 && scanOffset < data.Length)
                        {
                            try {
                                values.Add(ReadInt(data, ref scanOffset));
                            } catch { break; }
                        }

                        if (values.Count >= 2)
                        {
                            lock (_stateLock)
                            {
                                // BSE Heuristic:
                                // Index 0: Often MDEntryType or sequence ID
                                // Index 1: Often Price (Mantissa)
                                // Index 2: Often Size
                                
                                // Price Handling (Usually 2 decimal places if not PMap controlled)
                                long rawPrice = values.Count > 1 ? values[1] : 0;
                                if (rawPrice != 0)
                                {
                                    entry.Price = rawPrice / 100m;
                                    // Handle Absolute values vs small deltas
                                    if (entry.Price > 0 && entry.Price < 1000) 
                                        _decimalDictionary[entry.TemplateId] = entry.Price;
                                }

                                // Size Handling
                                if (values.Count > 2)
                                {
                                    long rawSize = Math.Abs(values[2]);
                                    if (rawSize > 0) entry.Size = rawSize;
                                }

                                // Match Entry Type if possible
                                if (values[0] == 0) entry.EntryType = "Bid";
                                else if (values[0] == 1) entry.EntryType = "Offer";
                                else if (values[0] == 2) entry.EntryType = "Trade";
                            }
                        }
                        break; // Stop after first valid symbol match in this packet
                    }
                }
            } catch { }
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

        // FAST Unsigned Integer (7-bit with stop bit)
        private static long ReadUInt(byte[] data, ref int offset)
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

        // FAST Signed Integer (Two's complement logic for 7-bit streams)
        private static long ReadInt(byte[] data, ref int offset)
        {
            if (offset >= data.Length) throw new IndexOutOfRangeException();
            
            byte b = data[offset++];
            long value = (b & 0x40) != 0 ? -1 : 0; // Sign extend from 7th bit
            value = (value << 7) | (byte)(b & 0x7F);
            
            if ((b & 0x80) == 0) // Multi-byte
            {
                while (offset < data.Length)
                {
                    b = data[offset++];
                    value = (value << 7) | (byte)(b & 0x7F);
                    if ((b & 0x80) != 0) break;
                }
            }
            return value;
        }

        private static DateTime ParseBseTime(string s)
        {
            try {
                // Handle 20260202-07:47:41 format
                if (DateTime.TryParseExact(s, "yyyyMMdd-HH:mm:ss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                    return dt;
            } catch { }
            return DateTime.Now;
        }
    }
}
