using System;
using System.Collections.Generic;
using CryptoTradingSystem.General.Data;

namespace CryptoTradingSystem.IndicatorCalculator.Interfaces
{
    interface IDatabaseHandlerIndicator
    {
        List<CustomQuote> GetCandleStickDataFromDatabase(Enums.Assets _asset, Enums.TimeFrames _timeFrame, DateTime _lastCloseTime = new DateTime(), int _amount = 1000);
        
        /// <summary>
        /// Update or Insert the indicators into the DB
        /// </summary>
        /// <param name="_indicator"></param>
        /// <param name="_data"></param>
        void UpsertIndicators(Enums.Indicators _indicator, Dictionary<CustomQuote, Dictionary<int, decimal?>> _data);
    }
}
