using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CryptoTradingSystem.General.Data;
using CryptoTradingSystem.General.Database;
using CryptoTradingSystem.General.Database.Models;
using CryptoTradingSystem.IndicatorCalculator.Interfaces;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace CryptoTradingSystem.IndicatorCalculator
{
    internal class MySQLDatabaseHandler : IDatabaseHandlerIndicator
    {
        private readonly string _connectionString;

        public MySQLDatabaseHandler(string connectionString)
        {
            _connectionString = connectionString;
        }

        public List<CustomQuote> GetCandleStickDataFromDatabase(Enums.Assets asset, Enums.TimeFrames timeFrame, DateTime lastCloseTime = new DateTime(), int amount = 1000)
        {
            var quotes = new List<CustomQuote>();

            var currentYear = DateTime.Now.Year;
            var currentMonth = DateTime.Now.Month;

            TimeSpan parsedTimeFrame;

            switch (timeFrame)
            {
                // Translate timeframe here to do date checks later on
                case Enums.TimeFrames.M5:
                case Enums.TimeFrames.M15:
                    parsedTimeFrame = TimeSpan.FromMinutes(Convert.ToDouble(timeFrame.GetStringValue().Trim('m')));
                    break;
                case Enums.TimeFrames.H1:
                case Enums.TimeFrames.H4:
                    parsedTimeFrame = TimeSpan.FromHours(Convert.ToDouble(timeFrame.GetStringValue().Trim('h')));
                    break;
                case Enums.TimeFrames.D1:
                    parsedTimeFrame = TimeSpan.FromDays(Convert.ToDouble(timeFrame.GetStringValue().Trim('d')));
                    break;
                default:
                    Console.WriteLine($"GetCandleStickDataFromDatabase | {timeFrame} konnte nicht übersetzt werden");
                    return quotes;
            }

            try
            {
                using var contextDB = new CryptoTradingSystemContext(_connectionString);

                var candlesToCalculate = contextDB.Assets.Where(x => 
                    x.AssetName == asset.GetStringValue() && 
                    x.Interval == timeFrame.GetStringValue() && 
                    x.CloseTime >= lastCloseTime)
                    .OrderBy(x => x.OpenTime)
                    .Take(amount);

                DateTime previousCandle = lastCloseTime;
                foreach (var candle in candlesToCalculate)
                {
                    // If we do have a previous candle, check if the difference from the current to the previous one is above the timeframe we are looking for
                    // If so, then it is a gap and then check if the gap is towards the current Year and Month, this is where we can be sure, that the data is not complete yet.
                    // Break here then, so we can do a new request and get the new incoming data
                    if (previousCandle != DateTime.MinValue)
                    {
                        var gap = (candle.CloseTime - previousCandle) > parsedTimeFrame;
                        if (gap && candle.CloseTime.Year == currentYear && candle.CloseTime.Month == currentMonth && (previousCandle.Year != currentYear || previousCandle.Month != currentMonth))
                        {
                            Log.Debug("{asset} | {timeFrame} | there is a gap: '{currenctClose}' - '{previousCandle}' = '{result}'", asset.GetStringValue(), timeFrame.GetStringValue(), candle.CloseTime, previousCandle, candle.CloseTime - previousCandle);
                            break;
                        }
                    }
                    else
                    {
                        // Do not allow to calculate indicators if we do not have data from the past
                        if (candle.CloseTime.Year == currentYear && (candle.CloseTime.Month == currentMonth || candle.CloseTime.Month == currentMonth - 1))
                        {
                            Log.Debug("{asset} | {timeFrame} | did start to calculate this year: '{currenctClose}' / '{previousCandle}'", asset.GetStringValue(), timeFrame.GetStringValue(), candle.CloseTime, previousCandle);
                            break;
                        }
                    }

                    quotes.Add(new CustomQuote
                    {
                        Asset = candle.AssetName,
                        Interval = candle.Interval,
                        OpenTime = candle.OpenTime,
                        Date = candle.CloseTime,
                        Open = candle.CandleOpen,
                        High = candle.CandleHigh,
                        Low = candle.CandleLow,
                        Close = candle.CandleClose,
                        Volume = candle.Volume
                    });

                    previousCandle = candle.CloseTime;
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    "{asset} | {timeFrame} | {lastClose} | could not get candles from Database", 
                    asset.GetStringValue(), 
                    timeFrame.GetStringValue(), 
                    lastCloseTime);
                throw;
            }

            return quotes;
        }

        public void UpsertIndicators(Enums.Indicators indicator, Dictionary<CustomQuote, Dictionary<int, decimal?>> data)
        {
            try
            {
                using var contextDB = new CryptoTradingSystemContext(_connectionString);
                using var transaction = contextDB.Database.BeginTransaction();

                foreach (var keyValuePair in data)
                {
                    switch (indicator)
                    {
                        case Enums.Indicators.EMA:
                            UpdateOrInsertIndicator(contextDB.EMAs, keyValuePair);
                            break;
                        case Enums.Indicators.SMA:
                            UpdateOrInsertIndicator(contextDB.SMAs, keyValuePair);
                            break;
                        case Enums.Indicators.ATR:
                            UpdateOrInsertIndicator(contextDB.ATRs, keyValuePair);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(indicator), indicator, null);
                    }
                }

                contextDB.SaveChanges();
                transaction.Commit();
            }
            catch (Exception e)
            {
                Log.Error(
                    "{asset} | {timeFrame} | {indicator} | could not upsert Candles", 
                    data.FirstOrDefault().Key.Asset, 
                    data.FirstOrDefault().Key.Interval, 
                    indicator.GetStringValue());
                throw;
            }
        }

        private void UpdateOrInsertIndicator<T>(DbSet<T> databaseSet, KeyValuePair<CustomQuote, Dictionary<int, decimal?>> data) where T : Indicator
        {
            var emaValueToCandle = databaseSet
                .FirstOrDefault(x => x.AssetName == data.Key.Asset 
                && x.Interval == data.Key.Interval 
                && x.OpenTime == data.Key.OpenTime 
                && x.CloseTime == data.Key.Date);

            if (emaValueToCandle != null)
            {
                SetProperties(typeof(T), emaValueToCandle, data.Value);
            }
            else
            {
                var instance = (T)Activator.CreateInstance(typeof(T));
                if (instance == null) 
                    return;
                
                instance.AssetName = data.Key.Asset;
                instance.Interval = data.Key.Interval;
                instance.OpenTime = data.Key.OpenTime;
                instance.CloseTime = data.Key.Date;

                SetProperties(typeof(T), instance, data.Value);

                databaseSet.Add(instance);
            }
        }

        /// <summary>
        /// Checking properties like:
        /// EMA5
        /// EMA12
        /// EMA20
        /// </summary>
        /// <param name="classType"></param>
        /// <param name="indicatorObject"></param>
        /// <param name="data"></param>
        private static void SetProperties(Type classType, Indicator indicatorObject, Dictionary<int, decimal?> data)
        {
            var properties = classType.GetProperties();

            foreach (var timePeriod in data.Keys)
            {
                foreach (var property in properties)
                {
                    if (!property.Name.EndsWith(timePeriod.ToString()))
                        continue;
                    
                    if (data[timePeriod] != null)
                    {
                        property.SetValue(indicatorObject, data[timePeriod].Value);
                    }
                    else
                    {
                        property.SetValue(indicatorObject, null);
                    }
                    break;
                }

            }
        }
    }
}
