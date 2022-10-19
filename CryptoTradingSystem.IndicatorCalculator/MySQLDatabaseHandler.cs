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
        private readonly string connectionString;

        public MySQLDatabaseHandler(string _connectionString)
        {
            connectionString = _connectionString;
        }

        public List<CustomQuote> GetCandleStickDataFromDatabase(Enums.Assets _asset, Enums.TimeFrames _timeFrame, DateTime _lastCloseTime = new DateTime(), int _amount = 1000)
        {
            List<CustomQuote> quotes = new List<CustomQuote>();

            int currentYear = DateTime.Now.Year;
            int currentMonth = DateTime.Now.Month;

            TimeSpan timeFrame;

            // Translate timeframe here to do date checks later on
            if (_timeFrame is Enums.TimeFrames.M5 || _timeFrame is Enums.TimeFrames.M15)
            {
                timeFrame = TimeSpan.FromMinutes(Convert.ToDouble(_timeFrame.GetStringValue().Trim('m')));
            }
            else if (_timeFrame is Enums.TimeFrames.H1 || _timeFrame is Enums.TimeFrames.H4)
            {
                timeFrame = TimeSpan.FromHours(Convert.ToDouble(_timeFrame.GetStringValue().Trim('h')));
            }
            else if (_timeFrame is Enums.TimeFrames.D1)
            {
                timeFrame = TimeSpan.FromDays(Convert.ToDouble(_timeFrame.GetStringValue().Trim('d')));
            }
            else
            {
                Log.Warning("{asset} | {timeFrame} | timeframe could not be translated.", _asset.GetStringValue(), _timeFrame.GetStringValue());
                return quotes;
            }

            try
            {
                using CryptoTradingSystemContext contextDB = new CryptoTradingSystemContext(connectionString);

                var candlesToCalculate = contextDB.Assets.Where(x => x.AssetName == _asset.GetStringValue() && x.Interval == _timeFrame.GetStringValue() && x.CloseTime >= _lastCloseTime).OrderBy(x => x.OpenTime).Take(_amount);

                DateTime previousCandle = _lastCloseTime;
                foreach (var candle in candlesToCalculate)
                {
                    // If we do have a previous candle, check if the difference from the current to the previous one is above the timeframe we are looking for
                    // If so, then it is a gap and then check if the gap is towards the current Year and Month, this is where we can be sure, that the data is not complete yet.
                    // Break here then, so we can do a new request and get the new incoming data
                    if (previousCandle != DateTime.MinValue)
                    {
                        bool gap = (candle.CloseTime - previousCandle) > timeFrame;
                        if (gap && candle.CloseTime.Year == currentYear && candle.CloseTime.Month == currentMonth && (previousCandle.Year != currentYear || previousCandle.Month != currentMonth))
                        {
                            Log.Debug("{asset} | {timeFrame} | there is a gap: '{currenctClose}' - '{previousCandle}' = '{result}'", _asset.GetStringValue(), _timeFrame.GetStringValue(), candle.CloseTime, previousCandle, candle.CloseTime - previousCandle);
                            break;
                        }
                    }
                    else
                    {
                        // Do not allow to calculate indicators if we do not have data from the past
                        if (candle.CloseTime.Year == currentYear && (candle.CloseTime.Month == currentMonth || candle.CloseTime.Month == currentMonth - 1))
                        {
                            Log.Debug("{asset} | {timeFrame} | did start to calculate this year: '{currenctClose}' / '{previousCandle}'", _asset.GetStringValue(), _timeFrame.GetStringValue(), candle.CloseTime, previousCandle);
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
                Log.Error("{asset} | {timeFrame} | {lastClose} | could not get candles from Database", _asset.GetStringValue(), _timeFrame.GetStringValue(), _lastCloseTime);
                throw;
            }

            return quotes;
        }

        public void UpsertIndicators(Enums.Indicators _indicator, Dictionary<CustomQuote, Dictionary<int, double?>> _data)
        {
            try
            {
                using CryptoTradingSystemContext contextDB = new CryptoTradingSystemContext(connectionString);
                using var transaction = contextDB.Database.BeginTransaction();

                foreach (var data in _data)
                {
                    switch (_indicator)
                    {
                        case Enums.Indicators.EMA:
                            UpdateOrInsertIndicator(contextDB.EMAs, data);
                            break;
                        case Enums.Indicators.SMA:
                            UpdateOrInsertIndicator(contextDB.SMAs, data);
                            break;
                        case Enums.Indicators.ATR:
                            UpdateOrInsertIndicator(contextDB.ATRs, data);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(_indicator), _indicator, null);
                    }
                }

                contextDB.SaveChanges();
                transaction.Commit();
            }
            catch (Exception e)
            {
                Log.Error("{asset} | {timeFrame} | {indicator} | could not upsert Candles", _data.FirstOrDefault().Key.Asset, _data.FirstOrDefault().Key.Interval, _indicator.GetStringValue());
                throw;
            }
        }

        private void UpdateOrInsertIndicator<T>(DbSet<T> _databaseSet, KeyValuePair<CustomQuote, Dictionary<int, double?>> _data) where T : Indicator
        {
            var emaValueToCandle = _databaseSet.FirstOrDefault(x => x.AssetName == _data.Key.Asset && x.Interval == _data.Key.Interval && x.OpenTime == _data.Key.OpenTime && x.CloseTime == _data.Key.Date);

            if (emaValueToCandle != null)
            {
                SetProperties(typeof(T), emaValueToCandle, _data.Value);
            }
            else
            {
                var instance = (T)Activator.CreateInstance(typeof(T));
                instance.AssetName = _data.Key.Asset;
                instance.Interval = _data.Key.Interval;
                instance.OpenTime = _data.Key.OpenTime;
                instance.CloseTime = _data.Key.Date;

                SetProperties(typeof(T), instance, _data.Value);

                _databaseSet.Add(instance);
            }
        }

        /// <summary>
        /// Checking properties like:
        /// EMA5
        /// EMA12
        /// EMA20
        /// </summary>
        /// <param name="_class"></param>
        /// <param name="_object"></param>
        /// <param name="_data"></param>
        private void SetProperties(Type _class, Indicator _object, Dictionary<int, double?> _data)
        {
            PropertyInfo[] properties = _class.GetProperties();

            foreach (var timePeriod in _data.Keys)
            {
                foreach (PropertyInfo property in properties)
                {
                    if (property.Name.EndsWith(timePeriod.ToString()))
                    {
                        if (_data[timePeriod] != null)
                        {
                            property.SetValue(_object, _data[timePeriod].Value);
                        }
                        else
                        {
                            property.SetValue(_object, null);
                        }
                        break;
                    }
                }
            }
        }
    }
}
