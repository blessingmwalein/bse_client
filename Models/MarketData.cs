using System;

namespace BseDashboard.Models
{
    public class MarketDataEntry
    {
        public string Symbol { get; set; } = string.Empty;
        public string MsgType { get; set; } = string.Empty;
        public DateTime SendingTime { get; set; }
        public string EntryType { get; set; } = string.Empty; // Bid, Offer, Trade
        public decimal Price { get; set; }
        public long Size { get; set; }
        public string RawHex { get; set; } = string.Empty;
        public string InstrumentStatus { get; set; } = "Active";
    }

    public enum BseMsgType
    {
        Heartbeat = 0,
        SecurityDefinition = 'd',
        SecurityStatus = 'f',
        IncrementalRefresh = 'X',
        Snapshot = 'W'
    }
}
