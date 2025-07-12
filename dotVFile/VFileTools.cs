using System.Collections.Concurrent;
using System.Diagnostics;

namespace dotVFile;

/// <summary>
/// Tools/Utilities for internal usage.
/// It also implements IVFileHooks so that it can wrap VFile.Debug.
/// Most things in here only do work if VFile.Debug = true, for performance reasons.
/// </summary>
internal class VFileTools(VFile vfile, IVFileHooks hooks) : IVFileHooks
{
	private readonly VFile VFile = vfile;

	public IVFileHooks Hooks { get; } = hooks;
	public Metrics Metrics { get; } = new Metrics();

	public void ErrorHandler(VFileError error)
	{
		Hooks.ErrorHandler(error);
	}

	public void DebugLog(string msg)
	{
		if (VFile.Debug)
			Hooks.DebugLog(msg);
	}

	public Timer TimerStart(string name)
	{
		if (!VFile.Debug)
			return Timer.Default();

		var timer = new Timer(name);

		Metrics.Timers.AddOrUpdate(name, [timer], (_, list) => { list.Add(timer); return list; });

		timer.Start();
		return timer;
	}

	public void TimerEnd(Timer timer)
	{
		if (!VFile.Debug) return;

		timer.Stop();
	}

	public void LogTimerEnd(Timer timer)
	{
		TimerEnd(timer);
		DebugLog(timer.ToString());
	}

	public void LogMetrics()
	{
		if (!VFile.Debug) return;

		DebugLog("Stats: " + VFile.GetStats().ToJson(true)!);
		DebugLog("Metrics: " + Metrics.GetMetrics().ToJson(true)!);
	}
}

internal class Timer(string Name)
{
	public static Timer Default() => new("__default__");

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
	Stats<int> GetOrStoreCount);

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