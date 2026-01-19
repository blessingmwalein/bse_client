using System;
using System.Collections.Generic;

namespace BseMarketDataClient.Fast
{
    public static class FastFrameEncoder
    {
        public static byte[] Encode(byte[] payload)
        {
            var length = payload.Length;
            var prefix = EncodeLength(length);
            var result = new byte[prefix.Count + length];
            prefix.CopyTo(result, 0);
            Array.Copy(payload, 0, result, prefix.Count, length);
            return result;
        }

        private static List<byte> EncodeLength(int length)
        {
            var bytes = new List<byte>();
            if (length == 0)
            {
                bytes.Add(0x80); // 0 with stop bit
                return bytes;
            }

            var temp = length;
            var result = new List<byte>();
            
            // Extract 7 bits at a time
            while (temp > 0)
            {
                result.Add((byte)(temp & 0x7F));
                temp >>= 7;
            }

            result.Reverse();
            
            // Set stop bit on the last byte
            result[^1] |= 0x80;
            
            return result;
        }
    }
}
