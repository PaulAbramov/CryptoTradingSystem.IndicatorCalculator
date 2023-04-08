using System;
using System.Collections.Generic;
using CryptoTradingSystem.General.Data;

namespace CryptoTradingSystem.IndicatorCalculator.Interfaces
{
    public interface IDatabaseHandlerIndicator
    {
        List<CustomQuote> GetCandleStickDataFromDatabase(Enums.Assets asset, Enums.TimeFrames timeFrame,  int amount, DateTime lastCloseTime = new DateTime());
        
        /// <summary>
        /// Update or Insert the indicators into the DB
        /// </summary>
        /// <param name="indicator"></param>
        /// <param name="data"></param>
        void UpsertIndicators(Type indicator, Dictionary<CustomQuote, Dictionary<int, decimal?>> data);
    }
}
