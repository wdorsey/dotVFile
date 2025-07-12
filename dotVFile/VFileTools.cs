using System.Collections.Concurrent;
using System.Diagnostics;
using Newtonsoft.Json;

namespace dotVFile;

/// <summary>
/// Tools/Utilities for internal usage.
/// It also implements IVFileHooks so that it can wrap VFile.Debug.
/// Most things in here only do work if VFile.Debug = true, for performance reasons.
/// </summary>
internal class VFileTools(Action<VFileError> errorHandler)
{
	public Action<VFileError> ErrorHandler { get; } = errorHandler;
	public bool MetricsEnabled { get; set; }
	public bool DebugEnabled { get; set; }
	public Action<string>? DebugLogFn { get; set; }
	public Metrics Metrics { get; } = new Metrics();

	public void DebugLog(string msg)
	{
		if (DebugEnabled)
			DebugLogFn?.Invoke(msg);
	}

	public Timer TimerStart(string name)
	{
		if (!MetricsEnabled)
			return Timer.Default;

		var timer = new Timer(name);

		Metrics.Timers.AddOrUpdate(name, [timer], (_, list) => { list.Add(timer); return list; });

		timer.Start();
		return timer;
	}

	public void TimerEnd(Timer timer)
	{
		if (!MetricsEnabled) return;

		timer.Stop();
	}

	public void LogTimerEnd(Timer timer)
	{
		if (!MetricsEnabled) return;

		TimerEnd(timer);
		DebugLog(timer.ToString());
	}

	public void LogMetrics()
	{
		if (!MetricsEnabled) return;

		DebugLog("Metrics: " + Metrics.GetMetrics().GetDisplay().ToJson(true)!);
	}
}

internal class Timer(string Name)
{
	public static Timer Default = new("__default__");

	public string Name { get; } = Name;
	public Stopwatch Stopwatch { get; } = new Stopwatch();
	public TimeSpan Elapsed => Stopwatch.Elapsed;

	public Timer Start()
	{
		Stopwatch.Start();
		return this;
	}

	public Timer Stop()
	{
		Stopwatch.Stop();
		return this;
	}

	public override string ToString()
	{
		return $"Timer: {Name} {Elapsed.TimeString()}";
	}
}

internal record StoreMetrics
{
	public List<long> ContentSizes = [];
}

internal record GetOrStoreMetrics
{
	public int RequestCount;
}

public record Stats<T>(string Name, int Count, T Sum, T Avg, T Min, T Max, Func<T, string> ToStringFn)
{
	[JsonIgnore]
	private readonly Func<T, string> ToStringFn = ToStringFn;
	public string SumString => ToStringFn(Sum);
	public string AvgString => ToStringFn(Avg);
	public string MinString => ToStringFn(Min);
	public string MaxString => ToStringFn(Max);

	public StatsDisplay GetDisplay() => new(Name, Count, SumString, AvgString, MinString, MaxString);
}

public record StatsDisplay(
	string Name,
	int Count,
	string Sum,
	string Avg,
	string Min,
	string Max);

public record MetricsResult(
	List<Stats<TimeSpan>> Timers,
	Stats<int> StoreContentCount,
	Stats<long> StoreContentSizes,
	Stats<int> GetOrStoreCount)
{
	public object GetDisplay() => new
	{
		Timers = Timers.Select(x => x.GetDisplay()).ToList(),
		StoreContentCount = StoreContentCount.GetDisplay(),
		StoreContentSizes = StoreContentSizes.GetDisplay(),
		GetOrStoreCount = GetOrStoreCount.GetDisplay()
	};
}

internal class Metrics
{
	public ConcurrentDictionary<string, ConcurrentBag<Timer>> Timers = [];
	public ConcurrentBag<StoreMetrics> StoreMetrics = [];
	public ConcurrentBag<GetOrStoreMetrics> GetOrStoreMetrics = [];

	public void Clear()
	{
		Timers.Clear();
		StoreMetrics.Clear();
		GetOrStoreMetrics.Clear();
	}

	public MetricsResult GetMetrics()
	{
		var timerStats = new List<Stats<TimeSpan>>();
		foreach (var (name, timers) in Timers.OrderBy(x => x.Key))
		{
			var stats = Stats($"{name} Timer", [.. timers.Select(x => x.Elapsed)]);
			timerStats.Add(stats);
		}

		var statsStoreCount = Stats("Store Content Count", [.. StoreMetrics.Select(x => x.ContentSizes.Count)]);
		var statsStoreSizes = StatsSize("Store Content Size", [.. StoreMetrics.Select(x => x.ContentSizes.Sum())]);
		var statsGetOrStoreCount = Stats("GetOrStore Request Count", [.. GetOrStoreMetrics.Select(x => x.RequestCount)]);

		return new(timerStats, statsStoreCount, statsStoreSizes, statsGetOrStoreCount);
	}

	public static Stats<TimeSpan> Stats(string name, List<TimeSpan> timespans)
	{
		return new Stats<TimeSpan>(
			name,
			timespans.Count,
			new TimeSpan(timespans.Sum(x => x.Ticks)),
			new TimeSpan(Util.DivideLong(timespans.Sum(x => x.Ticks), timespans.Count)),
			new TimeSpan(timespans.Min(x => x.Ticks)),
			new TimeSpan(timespans.Max(x => x.Ticks)),
			x => x.TimeString());
	}

	public static Stats<int> Stats(string name, List<int> values)
	{
		return new Stats<int>(
			name,
			values.Count,
			values.Sum(),
			Util.DivideInt(values.Sum(), values.Count),
			values.MinSafe(0),
			values.MaxSafe(0),
			x => x.ToString());
	}

	public static Stats<long> Stats(string name, List<long> values)
	{
		return new Stats<long>(
			name,
			values.Count,
			values.Sum(),
			Util.DivideLong(values.Sum(), values.Count),
			values.MinSafe(0),
			values.MaxSafe(0),
			x => x.ToString());
	}

	public static Stats<long> StatsSize(string name, List<long> values)
	{
		return new Stats<long>(
			name,
			values.Count,
			values.Sum(),
			Util.DivideLong(values.Sum(), values.Count),
			values.MinSafe(0),
			values.MaxSafe(0),
			Util.SizeString);
	}
}