using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTradingSystem.General.Data;
using Skender.Stock.Indicators;

namespace CryptoTradingSystem.IndicatorCalculator
{
    public class Calculator
    {
        private readonly MySQLDatabaseHandler databaseHandler;

        private readonly Enums.Assets asset;
        private readonly Enums.TimeFrames timeFrame;

        private readonly SortedSet<CustomQuote> quotes;
        private DateTime lastAssetCloseTime;

        private readonly List<int> ematimePeriods = new List<int> { 5, 9, 12, 20, 26, 50, 75, 200 };
        private readonly List<int> atrtimePeriods = new List<int> { 14 };

        public Calculator(Enums.Assets _asset, Enums.TimeFrames _timeFrame, string _connectionString)
        {
            asset = _asset;
            timeFrame = _timeFrame;
            lastAssetCloseTime = DateTime.MinValue;
            quotes = new SortedSet<CustomQuote>(Comparer<CustomQuote>.Create((a, b) => a.Date.CompareTo(b.Date)));

            databaseHandler = new MySQLDatabaseHandler(_connectionString);
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
                    int amountOfData = 750;

                    // get the candlestick from last saved AssetId for this asset and timeframe
                    var quotesToCheck = databaseHandler.GetCandleStickDataFromDatabase(asset, timeFrame, lastAssetCloseTime, amountOfData);
                    if (quotesToCheck.Count == 0)
                    {
                        return;
                    }

                    //Remove same entries and concat both lists
                    quotes.ExceptWith(quotesToCheck);
                    quotes.UnionWith(quotesToCheck);

                    Dictionary<int, List<EmaResult>> EMAs = new Dictionary<int, List<EmaResult>>();
                    Dictionary<int, List<SmaResult>> SMAs = new Dictionary<int, List<SmaResult>>();
                    Dictionary<int, List<AtrResult>> ATRs = new Dictionary<int, List<AtrResult>>();

                    Dictionary<CustomQuote, Dictionary<int, decimal?>> EMAsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();
                    Dictionary<CustomQuote, Dictionary<int, decimal?>> SMAsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();
                    Dictionary<CustomQuote, Dictionary<int, decimal?>> ATRsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();

                    #region Calculate Indicators

                    foreach (var ematimePeriod in ematimePeriods)
                    {
                        EMAs.Add(ematimePeriod, quotes.GetEma(ematimePeriod).ToList());
                        SMAs.Add(ematimePeriod, quotes.GetSma(ematimePeriod).ToList());
                    }

                    foreach (var atrtimePeriod in atrtimePeriods)
                    {
                        ATRs.Add(atrtimePeriod, quotes.GetAtr(atrtimePeriod).ToList());
                    }

                    #endregion

                    // if we get the amount of entries we wanted, then delete other up to this and set new lastAssetId
                    if (quotesToCheck.Count == amountOfData)
                    {
                        quotes.RemoveWhere(x => x.Date < lastAssetCloseTime);
                        lastAssetCloseTime = quotesToCheck.Last()!.Date;
                    }

                    quotesToCheck.Clear();

                    #region pass indicators to List matched to AssetId

                    foreach (var quoteEntry in quotes)
                    {
                        EMAsToSave.Add(quoteEntry, new Dictionary<int, decimal?>());
                        SMAsToSave.Add(quoteEntry, new Dictionary<int, decimal?>());
                        ATRsToSave.Add(quoteEntry, new Dictionary<int, decimal?>());

                        foreach (var ematimePeriod in ematimePeriods)
                        {
                            EMAsToSave[quoteEntry].Add(ematimePeriod, EMAs[ematimePeriod].FirstOrDefault(x => x.Date == quoteEntry.Date).Ema);
                            SMAsToSave[quoteEntry].Add(ematimePeriod, SMAs[ematimePeriod].FirstOrDefault(x => x.Date == quoteEntry.Date).Sma);
                        }

                        foreach (var atrtimePeriod in atrtimePeriods)
                        {
                            ATRsToSave[quoteEntry].Add(atrtimePeriod, ATRs[atrtimePeriod].FirstOrDefault(x => x.Date == quoteEntry.Date).Atr);
                        }
                    }

                    #endregion

                    #region write all the entries into the DB

                    databaseHandler.UpsertIndicators(Enums.Indicators.EMA, EMAsToSave);
                    databaseHandler.UpsertIndicators(Enums.Indicators.SMA, SMAsToSave);
                    databaseHandler.UpsertIndicators(Enums.Indicators.ATR, ATRsToSave);

                    #endregion

                    Console.WriteLine($"{asset.GetStringValue()} | {timeFrame.GetStringValue()} | {quotes.Last().Date} | wrote to the DB.");

                    Task.Delay(2000).GetAwaiter().GetResult();
                }
            });

            return taskToWork;
        }
    }
}
