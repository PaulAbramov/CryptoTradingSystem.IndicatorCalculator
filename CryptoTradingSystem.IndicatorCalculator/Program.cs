using CryptoTradingSystem.General.Data;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTradingSystem.IndicatorCalculator;

internal static class Program
{
	private static void Main()
	{
		IConfiguration config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

		var loggingfilePath = config.GetValue<string>("LoggingLocation");
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
#if RELEASE
                .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
                                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
#endif
#if DEBUG
			.WriteTo.Console(
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
#endif
			.WriteTo.File(
				loggingfilePath,
				rollingInterval: RollingInterval.Day,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		var connectionString = config.GetValue<string>("ConnectionString");

		var calcs = new Dictionary<Calculator, Task>();

		var amountOfData = config.GetValue<int>("AmountOfData");
		var lineIndexDictionary = new Dictionary<Enums.Assets, Dictionary<Enums.TimeFrames, int>>();
		var index = 1;
		
		foreach (var asset in (Enums.Assets[]) Enum.GetValues(typeof(Enums.Assets)))
		{
			lineIndexDictionary.TryAdd(asset, new Dictionary<Enums.TimeFrames, int>());
			
			foreach (var timeFrame in (Enums.TimeFrames[]) Enum.GetValues(typeof(Enums.TimeFrames)))
			{
				lineIndexDictionary[asset].TryAdd(timeFrame, index);
				index++;

				var calc = new Calculator(asset, timeFrame, connectionString);
				//Log.Debug(
				//	"{AssetToString} | " + "{TimeFrameToString} | " + "start to calculate indicators",
				//	asset.GetStringValue(),
				//	timeFrame.GetStringValue());
				calcs.Add(calc, Task.Run(() => calc.CalculateIndicatorsAndWriteToDatabase(amountOfData, lineIndexDictionary[asset][timeFrame])));
			}
		}

		Task.Delay(5000).GetAwaiter().GetResult();

		while (true)
		{
			foreach (var calc in calcs.Where(
				         calc =>
					         calc.Value.Status != TaskStatus.Running
					         && calc.Value.Status != TaskStatus.WaitingToRun
					         && calc.Value.Status != TaskStatus.WaitingForActivation))
			{
				calc.Value.Dispose();
				//Log.Debug(
				//	"{AssetToString} | " + "{TimeFrameToString} | " + "restart to calculate indicators",
				//	calc.Key.AssetToString,
				//	calc.Key.TimeFrameToString);
				calcs[calc.Key] = Task.Run(() => calc.Key.CalculateIndicatorsAndWriteToDatabase(amountOfData, lineIndexDictionary[calc.Key.Asset][calc.Key.TimeFrame]));

				// we have to break here, because we are manipulating the dictionary here and an error gets thrown
				break;
			}

			Task.Delay(500).GetAwaiter().GetResult();
		}
	}
}