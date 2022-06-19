using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTradingSystem.General.Data;
using Microsoft.Extensions.Configuration;

namespace CryptoTradingSystem.IndicatorCalculator
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

            var connectionString = config.GetValue<string>("ConnectionString");

            Dictionary<Calculator, Task> calcs = new Dictionary<Calculator, Task>();

            foreach (var asset in (Enums.Assets[]) Enum.GetValues(typeof(Enums.Assets)))
            {
                foreach (var timeFrame in (Enums.TimeFrames[])Enum.GetValues(typeof(Enums.TimeFrames)))
                {
                    Calculator calc = new Calculator(asset, timeFrame, connectionString);
                    calcs.Add(calc, Task.Run(calc.CalculateIndicatorsAndWriteToDatabase));
                }
            }

            Task.Delay(5000).GetAwaiter().GetResult();

            while (true)
            {
                foreach (var calc in calcs)
                {
                    if (calc.Value.Status != TaskStatus.Running &&
                        calc.Value.Status != TaskStatus.WaitingToRun &&
                        calc.Value.Status != TaskStatus.WaitingForActivation)
                    {
                        calc.Value.Dispose();
                        calcs[calc.Key] = Task.Run(calc.Key.CalculateIndicatorsAndWriteToDatabase);
                    }
                }

                Task.Delay(500).GetAwaiter().GetResult();
            }
        }
    }
}
