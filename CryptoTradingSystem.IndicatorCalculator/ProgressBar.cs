using Serilog;
using System;

namespace CryptoTradingSystem.IndicatorCalculator;

public class ProgressBar
{
	private readonly string asset;
	private readonly string timeFrame;
	private readonly int total;
	private int progress;
	private int lineIndex; // Store the line index

	public ProgressBar(string asset, string timeFrame, int total, int lineIndex)
	{
		this.asset = asset;
		this.timeFrame = timeFrame;
		this.total = total;
		this.lineIndex = lineIndex;
		progress = 0;
	}

	public void Update(int value)
	{
		// Update the progress value
		progress = value;

		// Calculate the percentage
		var percentage = (int)((double)progress / total * 100);

		// Move the cursor to the specified line and back to the beginning of the line
		Console.SetCursorPosition(0, lineIndex);

		// Draw the progress bar
		Console.Write($"["
		              + $"{new string('#', percentage / 2)}"
		              + $"{new string(' ', 50 - percentage / 2)}"
		              + $"] "
		              + $"{percentage}% "
		              + $"{progress}/{total} "
		              + $"| {asset} "
		              + $"| {timeFrame}");
	}
}