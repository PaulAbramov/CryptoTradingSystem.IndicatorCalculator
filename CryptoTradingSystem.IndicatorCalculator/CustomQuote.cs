using System;
using Skender.Stock.Indicators;

namespace CryptoTradingSystem.IndicatorCalculator
{
    public class CustomQuote : IQuote
    {
        // required base properties
        public DateTime Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }

        // custom properties
        public string? Asset { get; set; }
        public string? Interval { get; set; }
        public DateTime OpenTime { get; set; }
    }
}
