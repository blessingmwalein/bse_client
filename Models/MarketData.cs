using System;

namespace BseDashboard.Models
{
    public class MarketDataEntry
    {
        public string Symbol { get; set; } = "-";
        public string MsgType { get; set; } = "Unknown";
        public string MsgTypeCode { get; set; } = ""; // 0, X, d, etc.
        public DateTime SendingTime { get; set; }
        public string EntryType { get; set; } = "-"; // Bid, Offer, Trade
        public decimal Price { get; set; }
        public long Size { get; set; }
        public int TemplateId { get; set; }
        public string RawHex { get; set; } = string.Empty;
        
        // Extended fields from doc
        public string MDUpdateAction { get; set; } = "0"; // 0=New, 1=Update...
        public string TickDirection { get; set; } = "";
    }
}
