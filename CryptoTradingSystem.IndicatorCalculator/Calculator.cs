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
	private readonly Enums.Assets asset;
	private readonly List<int> atrtimePeriods = new() { 14 };
	private readonly List<int> ematimePeriods = new() { 5, 9, 12, 20, 26, 50, 75, 200 };
	private readonly MySQLDatabaseHandler databaseHandler;
	private readonly SortedSet<CustomQuote> quotes;
	private readonly Enums.TimeFrames timeFrame;
	private DateTime lastAssetCloseTime;

	public Calculator(Enums.Assets asset, Enums.TimeFrames timeFrame, string connectionString)
	{
		this.asset = asset;
		this.timeFrame = timeFrame;
		lastAssetCloseTime = DateTime.MinValue;
		quotes = new(Comparer<CustomQuote>.Create((a, b) => a.Date.CompareTo(b.Date)));

		databaseHandler = new(connectionString);
	}

	public string? Asset => asset.GetStringValue();

	public string? TimeFrame => timeFrame.GetStringValue();

	/// <summary>
	///   Get the data from the database and calculate the indicators
	///   afterwards write them into the indicatorTables in the database
	/// </summary>
	public Task CalculateIndicatorsAndWriteToDatabase(int amountOfData)
	{
		var taskToWork = Task.Factory.StartNew(
			() =>
			{
				while (true)
				{
					Log.Debug(
						"{Asset} | " + "{TimeFrame} | " + "getting data from {LastClose}",
						asset.GetStringValue(),
						timeFrame.GetStringValue(),
						lastAssetCloseTime);

					// get the candlestick from last saved AssetId for this asset and timeframe
					var quotesToCheck = Retry.Do(
						() => databaseHandler.GetCandleStickDataFromDatabase(
							asset,
							timeFrame,
							amountOfData,
							lastAssetCloseTime),
						TimeSpan.FromSeconds(1));

					if (quotesToCheck != null)
					{
						Log.Debug(
							"{Asset} | {TimeFrame} | got {Amount} back with last date: '{LastDate}'",
							asset.GetStringValue(),
							timeFrame.GetStringValue(),
							quotesToCheck.Count,
							quotesToCheck.LastOrDefault()?.Date);

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

						Log.Debug(
							"{Asset} | {TimeFrame} | {LastDate} | wrote to the DB",
							asset.GetStringValue(),
							timeFrame.GetStringValue(),
							quotes.Last().Date);

						quotesToCheck.Clear();
					}

					Task.Delay(2000).GetAwaiter().GetResult();
				}
			});

		return taskToWork;
	}
}