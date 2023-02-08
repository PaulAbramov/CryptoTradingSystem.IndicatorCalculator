using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTradingSystem.General.Data;
using CryptoTradingSystem.General.Helper;
using Skender.Stock.Indicators;

namespace CryptoTradingSystem.IndicatorCalculator
{
    public class Calculator
    {
        private readonly MySQLDatabaseHandler _databaseHandler;

        private readonly Enums.Assets _asset;
        private readonly Enums.TimeFrames _timeFrame;

        private readonly SortedSet<CustomQuote> _quotes;
        private DateTime _lastAssetCloseTime;

        private readonly List<int> _ematimePeriods = new List<int> { 5, 9, 12, 20, 26, 50, 75, 200 };
        private readonly List<int> _atrtimePeriods = new List<int> { 14 };

        public Calculator(Enums.Assets asset, Enums.TimeFrames timeFrame, string connectionString)
        {
            _asset = asset;
            _timeFrame = timeFrame;
            _lastAssetCloseTime = DateTime.MinValue;
            _quotes = new SortedSet<CustomQuote>(Comparer<CustomQuote>.Create((a, b) => a.Date.CompareTo(b.Date)));

            _databaseHandler = new MySQLDatabaseHandler(connectionString);
        }

        /// <summary>
        /// Get the data from the database and calculate the indicators
        /// afterwards write them into the indicatorTables in the database
        /// </summary>
        public Task CalculateIndicatorsAndWriteToDatabase()
        {
            var taskToWork = Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    const int amountOfData = 750;

                    // get the candlestick from last saved AssetId for this asset and timeframe
                    var quotesToCheck = Retry.Do(() =>_databaseHandler.GetCandleStickDataFromDatabase(_asset, _timeFrame, _lastAssetCloseTime, amountOfData), TimeSpan.FromSeconds(1));
                    if (quotesToCheck.Count == 0)
                    {
                        return;
                    }

                    //Remove same entries and concat both lists
                    _quotes.ExceptWith(quotesToCheck);
                    _quotes.UnionWith(quotesToCheck);

                    Dictionary<int, List<EmaResult>> EMAs = new Dictionary<int, List<EmaResult>>();
                    Dictionary<int, List<SmaResult>> SMAs = new Dictionary<int, List<SmaResult>>();

                    Dictionary<CustomQuote, Dictionary<int, decimal?>> EMAsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();
                    Dictionary<CustomQuote, Dictionary<int, decimal?>> SMAsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();
                    Dictionary<CustomQuote, Dictionary<int, decimal?>> ATRsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();

                    #region Calculate Indicators

                    foreach (var ematimePeriod in _ematimePeriods)
                    {
                        EMAs.Add(ematimePeriod, _quotes.GetEma(ematimePeriod).ToList());
                        SMAs.Add(ematimePeriod, _quotes.GetSma(ematimePeriod).ToList());
                    }

                    var ATRs = _atrtimePeriods.ToDictionary(atrtimePeriod => atrtimePeriod, atrtimePeriod => _quotes.GetAtr(atrtimePeriod).ToList());

                    #endregion

                    // if we get the amount of entries we wanted, then delete other up to this and set new lastAssetId
                    if (quotesToCheck.Count == amountOfData)
                    {
                        _quotes.RemoveWhere(x => x.Date < _lastAssetCloseTime);
                        _lastAssetCloseTime = quotesToCheck.Last()!.Date;
                    }

                    #region pass indicators to List matched to AssetId

                    foreach (var quoteEntry in _quotes)
                    {
                        EMAsToSave.Add(quoteEntry, new Dictionary<int, decimal?>());
                        SMAsToSave.Add(quoteEntry, new Dictionary<int, decimal?>());
                        ATRsToSave.Add(quoteEntry, new Dictionary<int, decimal?>());

                        foreach (var ematimePeriod in _ematimePeriods)
                        {
                            EMAsToSave[quoteEntry].Add(ematimePeriod, EMAs[ematimePeriod].FirstOrDefault(x => x.Date == quoteEntry.Date)?.Ema);
                            SMAsToSave[quoteEntry].Add(ematimePeriod, SMAs[ematimePeriod].FirstOrDefault(x => x.Date == quoteEntry.Date)?.Sma);
                        }

                        foreach (var atrtimePeriod in _atrtimePeriods)
                        {
                            ATRsToSave[quoteEntry].Add(atrtimePeriod, ATRs[atrtimePeriod].FirstOrDefault(x => x.Date == quoteEntry.Date)?.Atr);
                        }
                    }

                    #endregion

                    #region write all the entries into the DB

                    _databaseHandler.UpsertIndicators(Enums.Indicators.EMA, EMAsToSave);
                    _databaseHandler.UpsertIndicators(Enums.Indicators.SMA, SMAsToSave);
                    _databaseHandler.UpsertIndicators(Enums.Indicators.ATR, ATRsToSave);

                    #endregion

                    if (quotesToCheck.Count == amountOfData)
                    {
                        Console.WriteLine($"{_asset.GetStringValue()} | {_timeFrame.GetStringValue()} | {_quotes.Last().Date} | wrote to the DB.");
                    }

                    quotesToCheck.Clear();

                    Task.Delay(2000).GetAwaiter().GetResult();
                }
            });

            return taskToWork;
        }
    }
}
