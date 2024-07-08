using CryptoTradingSystem.General.Data;
using CryptoTradingSystem.General.Database;
using CryptoTradingSystem.General.Database.Models;
using CryptoTradingSystem.IndicatorCalculator.Interfaces;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTradingSystem.IndicatorCalculator;

internal class MySQLDatabaseHandler : IDatabaseHandlerIndicator
{
	private readonly string connectionString;

	public MySQLDatabaseHandler(string connectionString) => this.connectionString = connectionString;

	public int GetCandleStickAmount(Enums.Assets asset, Enums.TimeFrames timeFrame)
	{
		try
		{
			using var contextDb = new CryptoTradingSystemContext(connectionString);

			if (contextDb.Assets != null)
			{
				return contextDb.Assets.Count(
						x =>
							x.AssetName == asset.GetStringValue()
							&& x.Interval == timeFrame.GetStringValue());
			}
		}
		catch (Exception e)
		{
			Log.Error(
				e,
				"{AssetToString} | {TimeFrameToString} | could not get candles amount from Database",
				asset.GetStringValue(),
				timeFrame.GetStringValue());
			throw;
		}

		return 0;
	}
	
	public List<CustomQuote> GetCandleStickDataFromDatabase(
		Enums.Assets asset,
		Enums.TimeFrames timeFrame,
		int amount,
		DateTime lastCloseTime = new())
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
				parsedTimeFrame = TimeSpan.FromMinutes(Convert.ToDouble(timeFrame.GetStringValue()?.Trim('m')));
				break;
			case Enums.TimeFrames.H1:
			case Enums.TimeFrames.H4:
				parsedTimeFrame = TimeSpan.FromHours(Convert.ToDouble(timeFrame.GetStringValue()?.Trim('h')));
				break;
			case Enums.TimeFrames.D1:
				parsedTimeFrame = TimeSpan.FromDays(Convert.ToDouble(timeFrame.GetStringValue()?.Trim('d')));
				break;
			default:
				Log.Warning(
					"{AssetToString} | {TimeFrameToString} | {LastClose} | timeframe could not be translated",
					asset.GetStringValue(),
					timeFrame.GetStringValue(),
					lastCloseTime);
				return quotes;
		}

		try
		{
			using var contextDb = new CryptoTradingSystemContext(connectionString);

			if (contextDb.Assets != null)
			{
				var candlesToCalculate = contextDb.Assets.Where(
						x =>
							x.AssetName == asset.GetStringValue()
							&& x.Interval == timeFrame.GetStringValue()
							&& x.CloseTime >= lastCloseTime)
					.OrderBy(x => x.OpenTime)
					.Take(amount);

				var previousCandle = lastCloseTime;
				foreach (var candle in candlesToCalculate)
				{
					// If we do have a previous candle, check if the difference from the current to the previous one is above the timeframe we are looking for
					// If so, then it is a gap and then check if the gap is towards the current Year and Month, this is where we can be sure, that the data is not complete yet.
					// Break here then, so we can do a new request and get the new incoming data
					if (previousCandle != DateTime.MinValue)
					{
						var gap = candle.CloseTime - previousCandle > parsedTimeFrame;
						if (gap
						    && candle.CloseTime.Year == currentYear
						    && candle.CloseTime.Month == currentMonth
						    && (previousCandle.Year != currentYear || previousCandle.Month != currentMonth))
						{
							Log.Debug(
								"{AssetToString} | {TimeFrameToString} | "
								+ "there is a gap: '{CurrenctClose}' - '{PreviousCandle}' = '{Result}'",
								asset.GetStringValue(),
								timeFrame.GetStringValue(),
								candle.CloseTime,
								previousCandle,
								candle.CloseTime - previousCandle);
							break;
						}
					}
					else
					{
						// Do not allow to calculate indicators if we do not have data from the past
						if (candle.CloseTime.Year == currentYear
						    && (candle.CloseTime.Month == currentMonth || candle.CloseTime.Month == currentMonth - 1))
						{
							Log.Debug(
								"{AssetToString} | {TimeFrameToString} | "
								+ "did start to calculate this year: '{CurrenctClose}' / '{PreviousCandle}'",
								asset.GetStringValue(),
								timeFrame.GetStringValue(),
								candle.CloseTime,
								previousCandle);
							break;
						}
					}

					quotes.Add(
						new()
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
		}
		catch (Exception e)
		{
			Log.Error(
				e,
				"{AssetToString} | {TimeFrameToString} | {LastClose} | could not get candles from Database",
				asset.GetStringValue(),
				timeFrame.GetStringValue(),
				lastCloseTime);
			throw;
		}

		return quotes;
	}

	public void UpsertIndicators(Type indicator, Dictionary<CustomQuote, Dictionary<int, decimal?>> data)
	{
		try
		{
			using var contextDb = new CryptoTradingSystemContext(connectionString);
			using var transaction = contextDb.Database.BeginTransaction();

			foreach (var keyValuePair in data)
			{
				switch (indicator)
				{
					case not null when indicator == typeof(EMA):
						UpdateOrInsertIndicator(contextDb.EMAs, keyValuePair);
						break;
					case not null when indicator == typeof(SMA):
						UpdateOrInsertIndicator(contextDb.SMAs, keyValuePair);
						break;
					case not null when indicator == typeof(ATR):
						UpdateOrInsertIndicator(contextDb.ATRs, keyValuePair);
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(indicator), indicator, null);
				}
			}

			contextDb.SaveChanges();
			transaction.Commit();
		}
		catch (Exception e)
		{
			Log.Error(
				e,
				"{AssetToString} | {TimeFrameToString} | {Indicator} | could not upsert Candles",
				data.FirstOrDefault().Key.Asset,
				data.FirstOrDefault().Key.Interval,
				indicator?.Name);
			throw;
		}
	}

	private static void UpdateOrInsertIndicator<T>(
		DbSet<T>? databaseSet,
		KeyValuePair<CustomQuote, Dictionary<int, decimal?>> data)
		where T : Indicator
	{
		if (databaseSet == null)
		{
			return;
		}

		var emaValueToCandle = databaseSet
			.FirstOrDefault(
				x => x.AssetName == data.Key.Asset
				     && x.Interval == data.Key.Interval
				     && x.OpenTime == data.Key.OpenTime
				     && x.CloseTime == data.Key.Date);

		if (emaValueToCandle != null)
		{
			SetProperties(typeof(T), emaValueToCandle, data.Value);
		}
		else
		{
			var instance = (T) Activator.CreateInstance(typeof(T));
			if (instance == null)
			{
				return;
			}

			instance.AssetName = data.Key.Asset;
			instance.Interval = data.Key.Interval;
			instance.OpenTime = data.Key.OpenTime;
			instance.CloseTime = data.Key.Date;

			SetProperties(typeof(T), instance, data.Value);

			databaseSet.Add(instance);
		}
	}

	/// <summary>
	///   Checking properties like:
	///   EMA5
	///   EMA12
	///   EMA20
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
				{
					continue;
				}

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