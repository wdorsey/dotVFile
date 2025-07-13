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
}
