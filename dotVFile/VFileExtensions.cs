namespace dotVFile;

public static class VFileExtensions
{
	public static StoreOptions SetCompression(this StoreOptions options, VFileCompression compression)
	{
		options.Compression = compression;
		return options;
	}

	public static StoreOptions SetTTL(this StoreOptions options, TimeSpan? ttl)
	{
		options.TTL = ttl;
		return options;
	}

	public static StoreOptions SetVersionOptions(this StoreOptions options, VersionOptions versionOptions)
	{
		options.VersionOpts = versionOptions;
		return options;
	}

	public static StoreOptions SetExistsBehavior(this StoreOptions options, VFileExistsBehavior behavior)
	{
		options.VersionOpts.SetExistsBehavior(behavior);
		return options;
	}

	public static StoreOptions SetMaxVersionsRetained(this StoreOptions options, int? maxVersionsRetained)
	{
		options.VersionOpts.SetMaxVersionsRetained(maxVersionsRetained);
		return options;
	}

	public static StoreOptions SetVersionTTL(this StoreOptions options, TimeSpan? ttl)
	{
		options.VersionOpts.SetTTL(ttl);
		return options;
	}

	public static VersionOptions SetExistsBehavior(this VersionOptions options, VFileExistsBehavior behavior)
	{
		options.ExistsBehavior = behavior;
		return options;
	}

	public static VersionOptions SetMaxVersionsRetained(this VersionOptions options, int? maxVersionsRetained)
	{
		options.MaxVersionsRetained = maxVersionsRetained;
		return options;
	}

	public static VersionOptions SetTTL(this VersionOptions options, TimeSpan? ttl)
	{
		options.TTL = ttl;
		return options;
	}

	public static List<TResult> ResultsOrThrow<TRequest, TResult>(this List<VFileResult<TRequest, TResult>> results)
	{
		if (results.HasErrors() || !results.AllHasResult())
		{
			throw new Exception("results contains errors or null Results");
		}

		return [.. results.Select(x => x.Result!)];
	}

	public static List<TResult?> Results<TRequest, TResult>(this List<VFileResult<TRequest, TResult>> results)
	{
		return [.. results.Select(x => x.Result)];
	}

	public static List<VFileError<TRequest>?> Errors<TRequest, TResult>(this List<VFileResult<TRequest, TResult>> results)
	{
		return [.. results.Select(x => x.Error)];
	}

	public static bool AllHasResult<TRequest, TResult>(this List<VFileResult<TRequest, TResult>> results)
	{
		return results.All(x => x.HasResult);
	}

	public static bool HasErrors<TRequest, TResult>(this List<VFileResult<TRequest, TResult>> results)
	{
		return results.Any(x => x.HasError);
	}
}
