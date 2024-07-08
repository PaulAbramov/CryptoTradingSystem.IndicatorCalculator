using CryptoTradingSystem.General.Data;
using CryptoTradingSystem.General.Database.Models;
using CryptoTradingSystem.General.Helper;
using Serilog;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTradingSystem.IndicatorCalculator;

public class Calculator
{
	public Enums.Assets Asset { get; }
	public string? AssetToString => Asset.GetStringValue();
	public Enums.TimeFrames TimeFrame { get; }
	public string? TimeFrameToString => TimeFrame.GetStringValue();
	
	private readonly List<int> atrtimePeriods = new() { 14 };
	private readonly List<int> ematimePeriods = new() { 5, 9, 12, 20, 26, 50, 75, 200 };
	private readonly MySQLDatabaseHandler databaseHandler;
	private readonly SortedSet<CustomQuote> quotes;
	private DateTime lastAssetCloseTime;

	public Calculator(Enums.Assets asset, Enums.TimeFrames timeFrame, string connectionString)
	{
		Asset = asset;
		TimeFrame = timeFrame;
		lastAssetCloseTime = DateTime.MinValue;
		quotes = new(Comparer<CustomQuote>.Create((a, b) => a.Date.CompareTo(b.Date)));

		databaseHandler = new(connectionString);
	}

	/// <summary>
	///   Get the data from the database and calculate the indicators
	///   afterwards write them into the indicatorTables in the database
	/// </summary>
	public Task CalculateIndicatorsAndWriteToDatabase(int amountOfData, int lineIndex)
	{
		var taskToWork = Task.Factory.StartNew(
			() =>
			{
				var amountOfDataInDb = databaseHandler.GetCandleStickAmount(Asset, TimeFrame);
				
				// Initialize the progress bar
				var progressBar = new ProgressBar(AssetToString, TimeFrameToString, amountOfDataInDb, lineIndex);
				var iteration = 1;
				progressBar.Update(0);
				
				while (true)
				{
					//Log.Debug(
					//	"{AssetToString} | " + "{TimeFrameToString} | " + "getting data from {LastClose}",
					//	Asset.GetStringValue(),
					//	TimeFrame.GetStringValue(),
					//	lastAssetCloseTime);

					// get the candlestick from last saved AssetId for this asset and timeframe
					var quotesToCheck = Retry.Do(
						() => databaseHandler.GetCandleStickDataFromDatabase(
							Asset,
							TimeFrame,
							amountOfData,
							lastAssetCloseTime),
						TimeSpan.FromSeconds(1));

					if (quotesToCheck != null)
					{
						//Log.Information(
						//	"{AssetToString} | {TimeFrameToString} | got {Amount} back with last date: '{LastDate}'",
						//	Asset.GetStringValue(),
						//	TimeFrame.GetStringValue(),
						//	quotesToCheck.Count,
						//	quotesToCheck.LastOrDefault()?.Date);

						if (quotesToCheck.Count == 0)
						{
							return;
						}

						//Remove same entries and concat both lists
						quotes.ExceptWith(quotesToCheck);
						quotes.UnionWith(quotesToCheck);

						var EMAs = new Dictionary<int, List<EmaResult>>();
						var SMAs = new Dictionary<int, List<SmaResult>>();
						var ATRs = new Dictionary<int, List<AtrResult>>();

						var EMAsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();
						var SMAsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();
						var ATRsToSave = new Dictionary<CustomQuote, Dictionary<int, decimal?>>();

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

#region pass indicators to List matched to AssetId

						foreach (var quoteEntry in quotes)
						{
							EMAsToSave.Add(quoteEntry, new());
							SMAsToSave.Add(quoteEntry, new());
							ATRsToSave.Add(quoteEntry, new());

							foreach (var ematimePeriod in ematimePeriods)
							{
								EMAsToSave[quoteEntry]
									.Add(
										ematimePeriod,
										(decimal?) EMAs[ematimePeriod]
											.FirstOrDefault(x => x.Date == quoteEntry.Date)
											.Ema);
								SMAsToSave[quoteEntry]
									.Add(
										ematimePeriod,
										(decimal?) SMAs[ematimePeriod]
											.FirstOrDefault(x => x.Date == quoteEntry.Date)
											.Sma);
							}

							foreach (var atrtimePeriod in atrtimePeriods)
							{
								ATRsToSave[quoteEntry]
									.Add(
										atrtimePeriod,
										(decimal?) ATRs[atrtimePeriod]
											.FirstOrDefault(x => x.Date == quoteEntry.Date)
											.Atr);
							}
						}

#endregion

#region write all the entries into the DB

						Retry.Do(
							() => databaseHandler.UpsertIndicators(typeof(EMA), EMAsToSave),
							TimeSpan.FromSeconds(1));
						Retry.Do(
							() => databaseHandler.UpsertIndicators(typeof(SMA), SMAsToSave),
							TimeSpan.FromSeconds(1));
						Retry.Do(
							() => databaseHandler.UpsertIndicators(typeof(ATR), ATRsToSave),
							TimeSpan.FromSeconds(1));

#endregion

						//Log.Debug(
						//	"{AssetToString} | {TimeFrameToString} | {LastDate} | wrote to the DB",
						//	Asset.GetStringValue(),
						//	TimeFrame.GetStringValue(),
						//	quotes.Last().Date);
						
						// Update the progress bar
						progressBar.Update(iteration * amountOfData);

						quotesToCheck.Clear();
					}

					Task.Delay(2000).GetAwaiter().GetResult();
				}
			});

		return taskToWork;
	}
}