using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTradingSystem.General.Data;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace CryptoTradingSystem.IndicatorCalculator
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

            var loggingfilePath = config.GetValue<string>("LoggingLocation");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
#if RELEASE
                .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
#endif
#if DEBUG
                .WriteTo.Console()
#endif
                .WriteTo.File(loggingfilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var connectionString = config.GetValue<string>("ConnectionString");

            var calcs = new Dictionary<Calculator, Task>();

            int amountOfData = config.GetValue<int>("AmountOfData");
            foreach (var asset in (Enums.Assets[]) Enum.GetValues(typeof(Enums.Assets)))
            {
                foreach (var timeFrame in (Enums.TimeFrames[])Enum.GetValues(typeof(Enums.TimeFrames)))
                {
                    Calculator calc = new Calculator(asset, timeFrame, connectionString);
                    Log.Information("{asset} | {timeFrame} | start to calculate indicators.", asset.GetStringValue(), timeFrame.GetStringValue());
                    calcs.Add(calc, Task.Run(() => calc.CalculateIndicatorsAndWriteToDatabase(amountOfData)));
                }
            }

            Task.Delay(5000).GetAwaiter().GetResult();

            while (true)
            {
                foreach (var calc in calcs.Where(calc => 
                                calc.Value.Status != TaskStatus.Running &&
                                calc.Value.Status != TaskStatus.WaitingToRun && 
                                calc.Value.Status != TaskStatus.WaitingForActivation))
                {
                    calc.Value.Dispose();
                    Log.Information("{asset} | {timeFrame} | restart to calculate indicators.", calc.Key.Asset, calc.Key.TimeFrame);
                    calcs[calc.Key] = Task.Run(() => calc.Key.CalculateIndicatorsAndWriteToDatabase(amountOfData));
                    // we have to break here, because we are manipulating the dictionary here and an error gets thrown
                    break;
                }

                Task.Delay(500).GetAwaiter().GetResult();
            }
        }
    }
}
