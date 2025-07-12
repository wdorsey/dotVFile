namespace dotVFile;

public class VFile
{
	public const string Version = "0.0.1";

	private static string Context(string ctx) => $"{ctx}"; // placeholder in case I want to add something
	private static string FunctionContext(string fnName) => Context($"{fnName}()");
	private static string FunctionContext(string fnName, string ctx) => Context($"{fnName}() {ctx}");

	private readonly Mutex Mutex = new();

	public static void NotImplErrorHandler(VFileError _) { }

	public VFile(VFileOptions opts) : this(_ => opts) { }
	public VFile(Func<VFileOptions, VFileOptions> configure)
	{
		var opts = VFileOptions.Default();
		opts = configure(opts);

		if (opts.Directory.IsEmpty() || !Path.IsPathFullyQualified(opts.Directory))
			throw new Exception($"Invalid Directory: \"{opts.Directory}\". Directory must be a valid path where this VFile instance will store its single file, such as: \"C:\\dotVFile\".");

		Name = opts.Name.HasValue() ? opts.Name : "dotVFile";
		Directory = Util.CreateDir(opts.Directory);
		Tools = new VFileTools(opts.ErrorHandler);
		Database = new VFileDatabase(new(Name, Directory, Version, Tools));
		DefaultStoreOptions = opts.DefaultStoreOptions;

		Clean();
	}

	internal VFileDatabase Database { get; private set; }
	internal VFileTools Tools { get; private set; }
	public string Name { get; private set; }
	public string Directory { get; private set; }
	public StoreOptions DefaultStoreOptions { get; private set; }
	public DateTimeOffset LastClean { get; internal set; }
	public DateTimeOffset NextClean { get; internal set; }

	/// <summary>
	/// Gets a copy of the <see cref="DefaultStoreOptions"/>
	/// </summary>
	public StoreOptions GetDefaultStoreOptions() => DefaultStoreOptions with { };

	/// <summary>
	/// FilePath for the single database file that is the entire virtual file system.<br/>
	/// This file most likely cannot be directly read outside of this library, it is fully locked.
	/// If you want to create a backup programatically, use File.Copy(SingleFilePath, [dest]).
	/// </summary>
	public string SingleFilePath => Database.DatabaseFilePath;

	/// <summary>
	/// FileName of the single database file that is the entire virutal file system.
	/// </summary>
	public string SingleFileName => Database.DatabaseFileName;

	/// <summary>
	/// !!! DANGER !!!
	/// This will delete EVERYTHING.
	/// It will then recreate the Database.
	/// This VFile instance will still work, but have no data.
	/// </summary>
	public void DANGER_WipeData()
	{
		Database.DropDatabase();
		Database.CreateDatabase();
	}

	/// <summary>
	/// Toggle recording of metrics for the currently running process.
	/// Get metrics via <see cref="GetMetrics"/>.<br/>
	/// Disabled by default as it causes a performance hit.
	/// </summary>
	public void SetMetricsMode(bool enabled)
	{
		Tools.MetricsEnabled = enabled;
	}

	/// <summary>
	/// Toggle debug logging. 
	/// Used primarily for local development.
	/// </summary>
	public void SetDebugMode(bool enabled, Action<string>? log)
	{
		Tools.DebugEnabled = enabled;
		Tools.DebugLogFn = log;
	}

	/// <summary>
	/// Deletes VFiles that have passed their DeleteAt expiration time.<br/>
	/// Deletes any dangling, unreferenced Content.
	/// </summary>
	public CleanResult Clean()
	{
		// Some of the unreferenced data should normally be deleted during Store()
		// but it isn't for both performance reasons and because it has
		// to be checked here anyways after expired VFiles are deleted.

		var t = Tools.TimerStart(FunctionContext(nameof(Clean)));

		CleanResult? result;

		Mutex.WaitOne();
		try
		{
			// delete expired VFiles first so that their content and directories 
			// are freed to be cleaned up via DeleteUnreferencedEntities.
			var expired = Database.DeleteExpiredVFiles();
			var unreferenced = Database.DeleteUnreferencedFileContent();

			result = new CleanResult(unreferenced, expired);

			LastClean = DateTimeOffset.Now;
			SetNextClean();
		}
		finally
		{
			Mutex.ReleaseMutex();
		}

		Tools.DebugLog($"{t.Name} => {new { Result = result, NextClean }.ToJson()}");
		Tools.TimerEnd(t);

		return result;
	}

	private void CleanCheck()
	{
		if (NextClean <= DateTimeOffset.Now)
		{
			Clean();
		}
	}

	private void SetNextClean()
	{
		var t = Tools.TimerStart(FunctionContext(nameof(SetNextClean)));

		var candidates = new List<DateTimeOffset>
		{
			Database.GetVFileMinDeleteAt() ?? DateTimeOffset.MaxValue,
			LastClean.AddHours(1)
		};

		NextClean = candidates.Min();

		Tools.TimerEnd(t);
	}

	/// <summary>
	/// Defragment the single database file. This is _very_ slow.
	/// During this process the Database file will be completely locked.
	/// </summary>
	public void DefragDatabaseFile()
	{
		Database.Vacuum();
	}

	public VFileInfo? Get(VFilePath path)
	{
		var t = Tools.TimerStart(Context("Get(path)"));

		var result = GetVersions(path, VersionQuery.Latest).SingleOrDefault();

		Tools.TimerEnd(t);

		return result;
	}

	public List<VFileInfo> Get(List<VFilePath> paths)
	{
		var t = Tools.TimerStart(Context("Get(paths)"));

		var results = GetVersions(paths, VersionQuery.Latest);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> Get(VDirectory directory, bool recursive = false)
	{
		var t = Tools.TimerStart(Context("Get(directory)"));

		var results = GetVersions(directory, recursive, VersionQuery.Latest);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVersions(VFilePath path, VersionQuery versionQuery = VersionQuery.Versions)
	{
		var t = Tools.TimerStart(Context("GetVersions(path)"));

		var results = GetVersions(path.AsList(), versionQuery);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVersions(List<VFilePath> paths, VersionQuery versionQuery = VersionQuery.Versions)
	{
		var t = Tools.TimerStart(Context("GetVersions(paths)"));

		CleanCheck();

		var vfiles = Database.GetVFilesByFilePath(paths, versionQuery);
		var results = ConvertDbVFile(vfiles);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVersions(
		VDirectory directory,
		bool recursive = false,
		VersionQuery versionQuery = VersionQuery.Versions)
	{
		var t = Tools.TimerStart(Context("GetVersions(directory)"));

		CleanCheck();

		var paths = recursive
			? Database.GetDirectoriesRecursive(directory.Path).Select(x => x.Path)
			: [directory.Path];

		var vfiles = Database.GetVFilesByDirectory(paths, versionQuery);

		var results = ConvertDbVFile(vfiles);

		Tools.TimerEnd(t);

		return results;
	}

	public byte[]? GetBytes(VFilePath path)
	{
		var t = Tools.TimerStart(Context("GetBytes(path)"));

		var vfile = Database.GetVFilesByFilePath(path.AsList(), VersionQuery.Latest).SingleOrDefault();
		var result = GetBytes(vfile);

		Tools.TimerEnd(t);

		return result;
	}

	public byte[]? GetBytes(VFileInfo info)
	{
		var t = Tools.TimerStart(Context("GetBytes(info)"));

		var vfile = Database.GetVFilesById(info.Id.AsList()).SingleOrDefault();
		var result = GetBytes(vfile);

		Tools.TimerEnd(t);

		return result;
	}

	private byte[]? GetBytes(Db.VFileModel? vfile)
	{
		if (vfile == null) return null;

		var t = Tools.TimerStart(Context("GetBytes(vfile)"));

		var bytes = Database.GetContentBytes(vfile.FileContent);
		var result = vfile.FileContent.Compression == (byte)VFileCompression.None
			? bytes
			: Util.Decompress(bytes);

		Tools.TimerEnd(t);

		return result;
	}

	/// <summary>
	/// Get all VDirectories within directory.
	/// Optionally recursive.
	/// </summary>
	public List<VDirectory> GetDirectories(VDirectory directory, bool recursive = false)
	{
		var t = Tools.TimerStart(Context("GetDirectories(dir)"));

		// GetDirectoriesRecursive skips 1 because the first result will be
		// directory, which we don't want to include.
		var dirs = (recursive
			? Database.GetDirectoriesRecursive(directory.Path).Skip(1)
			: Database.GetDirectories(directory.Path))
			.Select(x => new VDirectory(x.Path))
			.ToList();

		Tools.TimerEnd(t);

		return dirs;
	}

	/// <summary>
	/// Provides caching functionality.<br/>
	/// Ideal for content that requires an expensive process to get, but the same input always results in the same output.<br/>
	/// e.g. Fetching static content from a url. The url or file name would be the <paramref name="cacheKey"/>, get from url in <paramref name="contentFn"/>. Can set <paramref name="ttl"/> if the content behind the url can change.<br/>
	/// e.g. A build pipeline that processes raw files, like minifying html/css/js.
	/// The raw file bytes would be the <paramref name="cacheKey"/>, and the processing would happen in <paramref name="contentFn"/>.
	/// </summary>
	/// <param name="cacheKey">Value that will be hashed and checked against the existing cached content at <paramref name="path"/></param>
	/// <param name="path">VFilePath to content genereated by <paramref name="contentFn"/></param>
	/// <param name="contentFn">Function to generate the content should it not be cached.</param>
	/// <param name="ttl">Optional time-to-live for the content.</param>
	/// <param name="bypassCache">Bypass cache and run contentFn.</param>
	public CacheResult GetOrStore(
		byte[] cacheKey,
		VFilePath path,
		Func<VFileContent> contentFn,
		StoreOptions? storeOptions = null,
		bool bypassCache = false)
	{
		var request = new CacheRequest(cacheKey, path, contentFn, storeOptions);
		return GetOrStore(request.AsList(), bypassCache).Single();
	}

	/// <summary>
	/// Provides caching functionality.<br/>
	/// Ideal for content that requires an expensive process to get, but the same input always results in the same output.<br/>
	/// e.g. Fetching static content from a url. The url or file name would be the <paramref name="cacheKey"/>, get from url in <paramref name="contentFn"/>. Can set <paramref name="ttl"/> if the content behind the url can change.<br/>
	/// e.g. A build pipeline that processes raw files, like minifying html/css/js.
	/// The raw file bytes would be the <paramref name="cacheKey"/>, and the processing would happen in <paramref name="contentFn"/>.
	/// </summary>
	public CacheResult GetOrStore(CacheRequest request, bool bypassCache = false) =>
		GetOrStore(request.AsList(), bypassCache).Single();

	/// <summary>
	/// Provides caching functionality.<br/>
	/// Ideal for content that requires an expensive process to get, but the same input always results in the same output.<br/>
	/// e.g. Fetching static content from a url. The url or file name would be the <paramref name="cacheKey"/>, get from url in <paramref name="contentFn"/>. Can set <paramref name="ttl"/> if the content behind the url can change.<br/>
	/// e.g. A build pipeline that processes raw files, like minifying html/css/js.
	/// The raw file bytes would be the <paramref name="cacheKey"/>, and the processing would happen in <paramref name="contentFn"/>.
	/// </summary>
	public List<CacheResult> GetOrStore(List<CacheRequest> requests, bool bypassCache = false)
	{
		var t = Tools.TimerStart(FunctionContext(nameof(GetOrStore)));
		var metrics = new GetOrStoreMetrics { RequestCount = requests.Count };

		CleanCheck();

		// key is FilePath
		var results = new List<CacheResult>();
		var stateFilePathMap = new Dictionary<string, CacheRequestState>();
		var stateCacheFilePathMap = new Dictionary<string, CacheRequestState>();
		var cachePaths = new List<VFilePath>();
		var cacheDir = new VDirectory("__vfile-cache-lookup__");

		var tState = Tools.TimerStart(FunctionContext(nameof(GetOrStore), "Preprocess Requests"));
		for (var i = 0; i < requests.Count; i++)
		{
			var request = requests[i];
			var filePath = request.Path.FilePath;
			var state = new CacheRequestState(i, request);

			if (!stateFilePathMap.TryAdd(filePath, state))
			{
				Tools.ErrorHandler(new(
					VFileErrorCodes.DuplicateRequest,
					$"Duplicate CacheRequest detected: {filePath}",
					request.Path));
			}
			else
			{
				state.Hash = Hash(request.CacheKey);
				state.CachePath = VFilePath.Combine(cacheDir, request.Path);
				cachePaths.Add(state.CachePath);
				stateCacheFilePathMap.Add(state.CachePath.FilePath, state);
			}

			results.Add(state.Result); // results created here, in order of requests
		}
		Tools.TimerEnd(tState);

		if (!bypassCache)
		{
			var tCache = Tools.TimerStart(FunctionContext(nameof(GetOrStore), "Cache TryGet"));

			var tCacheLookup = Tools.TimerStart(FunctionContext(nameof(GetOrStore), "Cache Lookup"));

			var cachedInfos = Get(cachePaths);

			Tools.TimerEnd(tCacheLookup);

			var cachedPaths = new List<VFilePath>();
			foreach (var cachedInfo in cachedInfos)
			{
				tCacheLookup = Tools.TimerStart(FunctionContext(nameof(GetOrStore), "Cache Lookup"));

				var hash = Util.GetString(GetBytes(cachedInfo));
				var info = stateCacheFilePathMap[cachedInfo.FilePath];

				Tools.TimerEnd(tCacheLookup);

				if (info.Hash == hash)
				{
					// We have to call GetBytes one at a time,
					// but we'll add path to a list and then 
					// below we'll Get the VFileInfos in one go.
					var path = info.CacheRequest.Path;
					var bytes = GetBytes(path);
					if (bytes != null)
					{
						info.Result.Bytes = bytes;
						info.Result.CacheHit = true;
						cachedPaths.Add(path);
					}
				}
			}

			foreach (var info in Get(cachedPaths))
			{
				stateFilePathMap[info.FilePath].Result.VFileInfo = info;

				// remove from stateFilePathMap since it was found
				// and stateFilePathMap serves as the "remaining-to-be-found" list.
				stateFilePathMap.Remove(info.FilePath);
			}

			Tools.TimerEnd(tCache);
		}

		if (stateFilePathMap.Count > 0)
		{
			var tStore = Tools.TimerStart(FunctionContext(nameof(GetOrStore), "Cache Miss"));
			var storeRequests = new List<StoreRequest>();
			foreach (var (filePath, info) in stateFilePathMap)
			{
				info.Result.Bytes = info.CacheRequest.ContentFn().GetContent();

				// use request TTL
				var cacheOpts = new StoreOptions(VFileCompression.None, info.CacheRequest.StoreOptions?.TTL,
					new(VFileExistsBehavior.Overwrite, null, null));

				// cache store request
				storeRequests.Add(new(info.CachePath!, new(Util.GetBytes(info.Hash)), cacheOpts));

				// requested content store request
				storeRequests.Add(new(info.CacheRequest.Path, new(info.Result.Bytes), info.CacheRequest.StoreOptions));
			}

			var storeResult = Store(storeRequests);
			foreach (var result in storeResult.VFiles)
			{
				// TryGetValue becaues storeResult will contain the cache hash vfiles too.
				if (stateFilePathMap.TryGetValue(result.FilePath, out var file))
					file.Result.VFileInfo = result;
			}
			Tools.TimerEnd(tStore);
		}

		Tools.TimerEnd(t);
		Tools.Metrics.GetOrStoreMetrics.Add(metrics);

		return results;
	}

	/// <summary>
	/// Returns copied VFileInfo. If null, VFile was not found at request.From.
	/// </summary>
	/// <param name="request"></param>
	/// <returns></returns>
	public VFileInfo? Copy(
		CopyRequest request,
		VersionQuery versionQuery = VersionQuery.Latest,
		StoreOptions? opts = null) =>
		Copy([request], versionQuery, opts).SingleOrDefault();

	/// <summary>
	/// Returns copied VFileInfos.
	/// </summary>
	public List<VFileInfo> Copy(
		List<CopyRequest> requests,
		VersionQuery versionQuery = VersionQuery.Latest,
		StoreOptions? opts = null)
	{
		if (requests.IsEmpty()) return [];

		var t = Tools.TimerStart(Context("Copy(requests)"));

		var storeRequests = new List<StoreRequest>();

		foreach (var request in requests)
		{
			var vfiles = GetVersions(request.From, versionQuery);
			foreach (var vfile in vfiles)
			{
				storeRequests.Add(new StoreRequest(request.To, VFileContent.Default(), opts)
				{
					CopyHash = vfile.Hash
				});
			}
		}

		Tools.TimerEnd(t);

		return Store(storeRequests).VFiles;
	}

	/// <summary>
	/// Copy every vfile in a given Directory.<br/>
	/// recursive will copy all vfiles in every subdirectory.<br/>
	/// Returns copied VFileInfos.
	/// </summary>
	public List<VFileInfo> Copy(
		VDirectory directory,
		VDirectory to,
		bool recursive = false,
		VersionQuery versionQuery = VersionQuery.Latest,
		StoreOptions? opts = null)
	{
		var t = Tools.TimerStart(Context("Copy(directory)"));

		CleanCheck();

		var requests = new List<CopyRequest>();

		var directories = recursive
			? Database.GetDirectoriesRecursive(directory.Path).Select(x => x.Path)
			: [directory.Path];

		foreach (var dirPath in directories)
		{
			var infos = Get(new VDirectory(dirPath), false);

			var subdir = directory.Path == VDirectory.DirectorySeparator.ToString()
				? new VDirectory(dirPath)
				: new VDirectory(dirPath.Replace(directory.Path, string.Empty));
			var toDir = VDirectory.Join(to, subdir);

			requests.AddRange(infos.Select(x => new CopyRequest(x, new(toDir, x.FileName))));
		}

		Tools.TimerEnd(t);

		return Copy(requests, versionQuery, opts);
	}

	/// <summary>
	/// Copies then deletes vfiles.
	/// </summary>
	public MoveResult Move(
		List<CopyRequest> requests,
		VersionQuery versionQuery = VersionQuery.Both,
		StoreOptions? opts = null)
	{
		var t = Tools.TimerStart(Context("Move(requests)"));

		var copied = Copy(requests, versionQuery, opts);
		var deleted = Delete([.. requests.Select(x => x.From)], versionQuery);

		Tools.TimerEnd(t);

		return new(copied, deleted);
	}

	/// <summary>
	/// Copies then deletes given directory, all subdirectories, and all vfiles within them, including versions.<br/>
	/// </summary>
	public MoveResult Move(
		VDirectory directory,
		VDirectory to,
		StoreOptions? opts = null)
	{
		var t = Tools.TimerStart(Context("Move(directory)"));

		var copied = Copy(directory, to, true, VersionQuery.Both, opts);
		var deleted = Delete(directory);

		Tools.TimerEnd(t);

		return new(copied, deleted);
	}

	/// <summary>
	/// Returns deleted VFileInfo. If null, VFile was not found at path.
	/// </summary>
	public VFileInfo? Delete(VFilePath path, VersionQuery versionQuery = VersionQuery.Both) =>
		Delete([path], versionQuery).SingleOrDefault();

	/// <summary>
	/// Returns deleted VFileInfos.
	/// </summary>
	public List<VFileInfo> Delete(
		List<VFilePath> paths,
		VersionQuery versionQuery = VersionQuery.Both)
	{
		var t = Tools.TimerStart(Context("Delete(paths, versionQuery)"));

		var vfiles = Database.GetVFilesByFilePath(paths, versionQuery);

		Database.DeleteVFiles([.. vfiles.Select(x => x.VFile.RowId)]);

		// delete file content, if possible
		Database.DeleteUnreferencedFileContent();

		Tools.TimerEnd(t);

		return [.. vfiles.Select(x => new VFileInfo(x))];
	}

	/// <summary>
	/// Returns deleted VFileInfo. If null, VFileInfo was not found.
	/// </summary>
	public VFileInfo? Delete(VFileInfo info) => Delete([info]).SingleOrDefault();

	/// <summary>
	/// Returns deleted VFileInfos.
	/// </summary>
	public List<VFileInfo> Delete(List<VFileInfo> infos)
	{
		var t = Tools.TimerStart(Context("Delete(infos)"));

		var vfiles = Database.GetVFilesById([.. infos.Select(x => x.Id)]);

		Database.DeleteVFiles([.. vfiles.Select(x => x.VFile.RowId)]);

		// delete file content, if possible
		Database.DeleteUnreferencedFileContent();

		Tools.TimerEnd(t);

		return [.. vfiles.Select(x => new VFileInfo(x))];
	}

	/// <summary>
	/// Deletes given directory, all subdirectories, and all vfiles within them, including versions.<br/>
	/// Returns deleted vfiles.
	/// </summary>
	public List<VFileInfo> Delete(VDirectory directory)
	{
		var t = Tools.TimerStart(Context("Delete(directory)"));

		// delete files
		var files = GetVersions(directory, true, VersionQuery.Both);
		Delete(files);

		// delete directories
		var directories = Database.GetDirectoriesRecursive(directory.Path);
		directories.Reverse(); // reverse to delete child dirs before parent dirs.
		Database.DeleteDirectory([.. directories.Select(x => x.RowId)]);

		// delete file content, if possible
		Database.DeleteUnreferencedFileContent();

		Tools.TimerEnd(t);

		return files;
	}

	public StoreResult Store(VFilePath path, VFileContent content, StoreOptions? opts = null) =>
		Store(new StoreRequest(path, content, opts));

	public StoreResult Store(StoreRequest request)
	{
		return Store(request.AsList());
	}

	public StoreResult Store(List<StoreRequest> requests)
	{
		// this function builds up all the VFileInfo changes within
		// a StoreState object that is saved at the very end.
		// New Content is also saved in bulk at the very end.

		var t = Tools.TimerStart(FunctionContext(nameof(Store)));
		var timer = Timer.Default; // re-usable timer
		var metrics = new StoreMetrics();
		var results = new List<VFileInfo>();
		var errors = new List<StoreRequest>();
		var state = new StoreState();
		var saveHashContentMap = new Dictionary<string, (VFileInfo Info, byte[] Bytes)>();
		var uniqueFilePaths = new Dictionary<string, VFilePath>();
		var optsTTLIsSet = false;

		CleanCheck();

		// this validation loop also serves to gather unique VFilePaths.
		timer = Tools.TimerStart(FunctionContext(nameof(Store), "Validate"));
		foreach (var request in requests)
		{
			var path = request.Path;
			if (!Assert_ValidFileName(path.FileName, nameof(Store)))
			{
				errors.Add(request);
			}

			if (uniqueFilePaths.ContainsKey(path.FilePath))
			{
				Tools.ErrorHandler(new(
					VFileErrorCodes.DuplicateRequest,
					$"Duplicate StoreRequest detected: {path.FilePath}",
					request));
				errors.Add(request);
			}
			uniqueFilePaths.Add(path.FilePath, path);
		}
		Tools.TimerEnd(timer);

		if (errors.Count > 0)
		{
			Tools.TimerEnd(t);
			return new([], [.. errors]);
		}

		// lock
		Mutex.WaitOne();

		try
		{
			timer = Tools.TimerStart(FunctionContext(nameof(Store), "GetVFilesByFilePath"));

			var existingVFiles = Database.GetVFilesByFilePath([.. uniqueFilePaths.Values], VersionQuery.Latest)
				.ToDictionary(x => x.Directory.Path + x.VFile.FileName);

			Tools.TimerEnd(timer);

			foreach (var request in requests)
			{
				var rqt = Tools.TimerStart(FunctionContext(nameof(Store), "Request"));

				var path = request.Path;

				var now = DateTimeOffset.Now;
				var opts = request.Opts ?? DefaultStoreOptions;
				optsTTLIsSet = optsTTLIsSet || opts.TTL.HasValue;

				timer = Tools.TimerStart(FunctionContext(nameof(Store), "GetContent"));

				// process content
				var hash = string.Empty;
				long size = 0;
				long sizeStored = 0;
				var bytes = Util.EmptyBytes();
				if (request.CopyHash.HasValue())
				{
					hash = request.CopyHash;
				}
				else
				{
					var content = request.Content.GetContent();
					bytes = opts.Compression == VFileCompression.Compress
						? Util.Compress(content)
						: content;
					hash = Hash(bytes);
					size = content.LongLength;
					sizeStored = bytes.LongLength;
					metrics.ContentSizes.Add(bytes.Length);
				}

				Tools.TimerEnd(timer);

				var existingVFile = existingVFiles.GetValueOrDefault(path.FilePath);

				var newVFile = new VFileInfo
				{
					Id = Guid.NewGuid(),
					VFilePath = path,
					DeleteAt = opts.TTL.HasValue ? now + opts.TTL : null,
					CreationTime = now,
					ContentId = Guid.NewGuid(),
					Hash = hash,
					Size = size,
					SizeStored = sizeStored,
					Compression = opts.Compression,
					ContentCreationTime = now
				};

				// check to see if we need to save the content
				if (request.CopyHash.IsEmpty() &&
					(existingVFile?.FileContent == null ||
					existingVFile.FileContent.Hash != hash))
				{
					// bulk saving these at the end is the fastest method I've found.
					saveHashContentMap.AddSafe(hash, (newVFile, bytes));
				}

				if (existingVFile == null)
				{
					state.NewVFiles.Add(newVFile);
					results.Add(newVFile);
				}
				else
				{
					// previous VFileInfo exists but content is different.
					var contentDifference = existingVFile.FileContent != null && existingVFile.FileContent.Hash != hash;
					results.Add(contentDifference ? newVFile : new VFileInfo(existingVFile));

					switch (opts.VersionOpts.ExistsBehavior)
					{
						case VFileExistsBehavior.Overwrite:
						{
							if (contentDifference)
							{
								state.DeleteVFiles.Add(existingVFile.VFile);
								state.NewVFiles.Add(newVFile);
							}
							break;
						}

						case VFileExistsBehavior.Error:
						{
							if (contentDifference)
							{
								Tools.ErrorHandler(new(
									VFileErrorCodes.OverwriteNotAllowed,
									$"VFileVersionBehavior is set to Error. Request to overwrite existing file not allowed: {path.FilePath}",
									request));
								results.Remove(newVFile);
								errors.Add(request);
							}
							break;
						}

						case VFileExistsBehavior.Version:
						{
							timer = Tools.TimerStart(FunctionContext(nameof(Store), "Version"));

							if (contentDifference)
							{
								optsTTLIsSet = optsTTLIsSet || opts.VersionOpts.TTL.HasValue;
								existingVFile.VFile.Versioned = now;
								existingVFile.VFile.DeleteAt = opts.VersionOpts.TTL.HasValue
									? existingVFile.VFile.Versioned + opts.VersionOpts.TTL
									: null;
								state.UpdateVFiles.Add(existingVFile.VFile);
								state.NewVFiles.Add(newVFile);
							}

							var maxVersions = opts.VersionOpts.MaxVersionsRetained;
							if (maxVersions.HasValue)
							{
								var versions = Database.GetVFilesByFilePath(path.AsList(), VersionQuery.Versions);

								// if contentDifference, then existingVFile is now a version, but is not
								// included in versions, so subtracting 1 accounts for that.
								var max = contentDifference ? maxVersions.Value - 1 : maxVersions.Value;

								var delete = versions.Select(x => x.VFile)
									.OrderByDescending(x => x.Versioned)
									.Skip(max);

								state.DeleteVFiles.AddRange(delete);
							}

							Tools.TimerEnd(timer);

							break;
						}
					}
				}

				Tools.TimerEnd(rqt);
			}

			timer = Tools.TimerStart(FunctionContext(nameof(Store), "SaveFileContent"));

			// wait for all content to finish saving
			Database.SaveFileContent([.. saveHashContentMap.Select(x => x.Value)]);

			Tools.TimerEnd(timer);
			timer = Tools.TimerStart(FunctionContext(nameof(Store), "SaveStoreState"));

			Database.SaveStoreState(state);

			Tools.TimerEnd(timer);

			// if vfiles were versioned, check to see if NextClean needs to change.
			if (optsTTLIsSet && (state.NewVFiles.Count > 0 || state.UpdateVFiles.Count > 0))
				SetNextClean();

			// @note: We do not check to see if the content of state.DeleteVFiles
			// can be deleted here because it is faster not to.
			// The deletes that happen here are also more of an implicit nature,
			// so the expectation is different compared to the explicit Delete() functions.
			// It doesn't hurt anything to leave them dangling and they'll eventually
			// get deleted via Clean().

			Tools.TimerEnd(t); // overall SaveVFiles timer
			Tools.Metrics.StoreMetrics.Add(metrics);
		}
		finally
		{
			Mutex.ReleaseMutex();
		}

		return new([.. results], [.. errors]);
	}

	public List<string> ExportDirectory(
		VDirectory fromDirectory,
		string toDirectoryPath,
		VDirectory? removeRootDirectory = null,
		bool recursive = true,
		Func<string, string>? modifyFileName = null,
		Func<string, string>? modifyDirectoryPath = null)
	{
		var t = Tools.TimerStart(FunctionContext(nameof(ExportDirectory)));

		var results = new List<string>();

		var vfiles = Get(fromDirectory, recursive);

		foreach (var vfile in vfiles)
		{
			var relativeDir = removeRootDirectory != null
				? VDirectory.RemoveRootPath(vfile.VDirectory, removeRootDirectory)
				: vfile.VDirectory;

			var systemDir = new VDirectory(toDirectoryPath, relativeDir.Path).SystemPath;
			var dir = modifyDirectoryPath?.Invoke(systemDir) ?? systemDir;

			var fileName = modifyFileName?.Invoke(vfile.FileName) ?? vfile.FileName;
			var path = new VFilePath(dir, fileName);

			var bytes = GetBytes(vfile)!;
			Util.WriteFile(path.SystemFilePath, bytes);

			results.Add(path.SystemFilePath);
		}

		Tools.TimerEnd(t);

		return results;
	}

	public DirectoryStats GetDirectoryStats(VDirectory directory)
	{
		var t = Tools.TimerStart(FunctionContext(nameof(GetDirectoryStats)));

		CleanCheck();

		var stats = new DirectoryStats(
			directory,
			GetVFileStats(directory, false, false),
			GetVFileStats(directory, true, false),
			GetVFileStats(directory, false, true),
			GetVFileStats(directory, true, true),
			GetDirectories(directory, false));

		Tools.TimerEnd(t);

		return stats;
	}

	private FileStats GetVFileStats(VDirectory directory, bool versions, bool recursive)
	{
		var vfiles = versions ? GetVersions(directory, recursive) : Get(directory, recursive);

		return new FileStats(vfiles.Count, vfiles.Sum(x => x.Size), vfiles.Sum(x => x.SizeStored));
	}

	public FileStats GetContentStats()
	{
		var t = Tools.TimerStart(FunctionContext(nameof(GetContentStats)));

		CleanCheck();

		var contents = Database.GetFileContent();

		long size = 0, sizeStored = 0;
		foreach (var content in contents)
		{
			size += content.Size;
			sizeStored += content.SizeContent;
		}

		var stats = new FileStats(contents.Count, size, sizeStored);

		Tools.TimerEnd(t);

		return stats;
	}

	/// <summary>
	/// Get <see cref="VFileStats"/> for this VFile instance.
	/// </summary>
	public VFileStats GetStats()
	{
		var t = Tools.TimerStart(FunctionContext(nameof(GetStats)));

		CleanCheck();

		var dbSize = new FileInfo(SingleFilePath).Length;
		var dirStats = GetDirectoryStats(VDirectory.RootDirectory());
		var contentStats = GetContentStats();
		var allDirs = Database.GetDirectoriesRecursive(VDirectory.RootDirectory().Path);

		Tools.TimerEnd(t);

		return new(
			dbSize,
			dirStats.TotalVFiles,
			dirStats.TotalVersions,
			contentStats,
			allDirs.Count);
	}

	public MetricsResult GetMetrics()
	{
		return Tools.Metrics.GetMetrics();
	}

	private static List<VFileInfo> ConvertDbVFile(List<Db.VFileModel> vfiles)
	{
		return [.. vfiles.Select(x => new VFileInfo(x))];
	}

	private static string Hash(byte[] bytes)
	{
		return Util.HashSHA256(bytes);
	}

	private bool Assert_ValidFileName(string fileName, string context)
	{
		if (fileName.IsEmpty())
		{
			Tools.ErrorHandler(new(
				VFileErrorCodes.InvalidParameter,
				$"{context}: Invalid FileName - must have a value",
				"FileName"));
			return false;
		}

		return true;
	}
}
