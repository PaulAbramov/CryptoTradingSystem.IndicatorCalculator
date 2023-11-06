using CryptoTradingSystem.General.Data;
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
		foreach (var asset in (Enums.Assets[]) Enum.GetValues(typeof(Enums.Assets)))
		{
			foreach (var timeFrame in (Enums.TimeFrames[]) Enum.GetValues(typeof(Enums.TimeFrames)))
			{
				var calc = new Calculator(asset, timeFrame, connectionString);
				Log.Information(
					"{Asset} | " + "{TimeFrame} | " + "start to calculate indicators",
					asset.GetStringValue(),
					timeFrame.GetStringValue());
				calcs.Add(calc, Task.Run(() => calc.CalculateIndicatorsAndWriteToDatabase(amountOfData)));
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
				Log.Information(
					"{Asset} | " + "{TimeFrame} | " + "restart to calculate indicators",
					calc.Key.Asset,
					calc.Key.TimeFrame);
				calcs[calc.Key] = Task.Run(() => calc.Key.CalculateIndicatorsAndWriteToDatabase(amountOfData));

				// we have to break here, because we are manipulating the dictionary here and an error gets thrown
				break;
			}

			Task.Delay(500).GetAwaiter().GetResult();
		}
	}
}