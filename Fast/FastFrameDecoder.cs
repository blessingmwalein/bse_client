using System;
using System.Collections.Generic;
using System.IO;

namespace BseMarketDataClient.Fast
{
    public class FastFrameDecoder
    {
        private readonly List<byte> _buffer = new();

        public IEnumerable<byte[]> Feed(byte[] data, int length)
        {
            for (int i = 0; i < length; i++)
            {
                _buffer.Add(data[i]);
            }

            while (TryProcess(out var payload))
            {
                if (payload != null)
                {
                    yield return payload;
                }
            }
        }

        private bool TryProcess(out byte[]? payload)
        {
            payload = null;
            if (_buffer.Count == 0) return false;

            int prefixLength = 0;
            int dataLength = 0;
            bool foundStopBit = false;

            // Read the length prefix
            for (int i = 0; i < _buffer.Count; i++)
            {
                prefixLength++;
                dataLength = (dataLength << 7) | (_buffer[i] & 0x7F);
                if ((_buffer[i] & 0x80) != 0)
                {
                    foundStopBit = true;
                    break;
                }
                
                // Sanity check: length prefix shouldn't be too long (max 5 bytes for 32-bit int)
                if (prefixLength > 5)
                {
                    _buffer.RemoveAt(0); // Sync error, discard one byte
                    return false;
                }
            }

            if (!foundStopBit) return false;

            // Check if we have the full payload
            if (_buffer.Count < prefixLength + dataLength) return false;

            payload = new byte[dataLength];
            _buffer.CopyTo(prefixLength, payload, 0, dataLength);
            
            // Remove processed message from buffer
            _buffer.RemoveRange(0, prefixLength + dataLength);
            
            return true;
        }
    }
}
