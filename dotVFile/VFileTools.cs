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

internal record Stats<T>(int Count, T Sum, T Avg, T Min, T Max, Func<T, string> ToStringFn)
{
	public string SumString => ToStringFn(Sum);
	public string AvgString => ToStringFn(Avg);
	public string MinString => ToStringFn(Min);
	public string MaxString => ToStringFn(Max);
	public object JsonObject()
	{
		return new
		{
			Count,
			Sum = SumString,
			Avg = AvgString,
			Min = MinString,
			Max = MaxString
		};
	}
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

	public Dictionary<string, object> GetMetrics()
	{
		var results = new Dictionary<string, object>();

		foreach (var (name, timers) in Timers.OrderBy(x => x.Key))
		{
			var stats = Stats([.. timers.Select(x => x.Elapsed)]);
			results.Add($"Timer: {name}", stats.JsonObject());
		}

		results.Add("StoreMetrics - Content Count",
			Stats([.. StoreMetrics.Select(x => x.ContentSizes.Count)]).JsonObject());

		results.Add("StoreMetrics - Content Size",
			StatsSize([.. StoreMetrics.Select(x => x.ContentSizes.Sum())]).JsonObject());

		results.Add("GetOrStoreMetrics - Request Count",
			Stats([.. GetOrStoreMetrics.Select(x => x.RequestCount)]).JsonObject());

		return results;
	}

	public static Stats<TimeSpan> Stats(List<TimeSpan> timespans)
	{
		return new Stats<TimeSpan>(
			timespans.Count,
			new TimeSpan(timespans.Sum(x => x.Ticks)),
			new TimeSpan(timespans.Sum(x => x.Ticks) / timespans.Count),
			new TimeSpan(timespans.Min(x => x.Ticks)),
			new TimeSpan(timespans.Max(x => x.Ticks)),
			x => x.TimeString());
	}

	public static Stats<int> Stats(List<int> values)
	{
		return new Stats<int>(
			values.Count,
			values.Sum(),
			values.Sum() / values.Count,
			values.Min(),
			values.Max(),
			x => x.ToString());
	}

	public static Stats<long> Stats(List<long> values)
	{
		return new Stats<long>(
			values.Count,
			values.Sum(),
			values.Sum() / values.Count,
			values.Min(),
			values.Max(),
			x => x.ToString());
	}

	public static Stats<long> StatsSize(List<long> values)
	{
		return new Stats<long>(
			values.Count,
			values.Sum(),
			values.Sum() / values.Count,
			values.Min(),
			values.Max(),
			Util.SizeString);
	}
}