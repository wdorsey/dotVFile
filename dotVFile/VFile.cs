namespace dotVFile;

public class VFile
{
	public const string Version = "1.0.0";

	private static string Context(string ctx) => $"{nameof(VFile)}.{ctx}";
	private static string FunctionContext(string fnName) => Context($"{fnName}()");
	private static string FunctionContext(string fnName, string ctx) => Context($"{fnName}(): {ctx}");

	public VFile(VFileOptions opts) : this(_ => opts) { }
	public VFile(Func<VFileOptions, VFileOptions> configure)
	{
		var opts = VFileOptions.Default();
		opts = configure(opts);

		if (opts.Directory.IsEmpty() || !Path.IsPathFullyQualified(opts.Directory))
			throw new Exception($"Invalid Directory: \"{opts.Directory}\". Directory must be a valid path where this VFile instance will store its single file, such as: \"C:\\dotVFile\".");

		Name = opts.Name.HasValue() ? opts.Name : "dotVFile";
		Directory = Util.CreateDir(opts.Directory);
		Tools = new VFileTools(this, opts.Hooks);
		Database = new VFileDatabase(new(Name, Directory, Version, Tools));
		DefaultStoreOptions = opts.DefaultStoreOptions;
		Debug = opts.Debug;

		Clean();
	}

	private readonly Mutex Mutex = new();
	internal VFileDatabase Database { get; private set; }
	internal VFileTools Tools { get; private set; }
	public string Name { get; private set; }
	public string Directory { get; private set; }
	public IVFileHooks Hooks => Tools.Hooks;
	public StoreOptions DefaultStoreOptions { get; private set; }
	public bool Debug { get; set; }

	/// <summary>
	/// Gets the single database file path that _is_ the entire virtual file system.
	/// This file could potentially be very large, so take care in retrieving it programmatically.
	/// </summary>
	public string SingleFilePath => Database.DatabaseFilePath;

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
	/// Deletes VFiles that have passed their DeleteAt expiration time.<br/>
	/// Deletes any dangling, unreferenced Content.
	/// </summary>
	public CleanResult Clean()
	{
		// Some of the unreferenced data should normally be deleted during StoreVFiles 
		// but it isn't for both performance reasons and because it has
		// to be checked here anyways because expired VFiles are deleted.

		var t = Tools.TimerStart(FunctionContext(nameof(Clean)));

		// delete expired VFiles first so that their content and directories 
		// are freed to be cleaned up via DeleteUnreferencedEntities.
		var expired = Database.DeleteExpiredVFiles();
		var unreferenced = Database.DeleteUnreferencedFileContent();

		Database.UpdateLastClean(DateTimeOffset.Now);

		var result = new CleanResult(unreferenced, expired);

		Tools.DebugLog($"{t.Name} => {result.ToJson()}");
		Tools.TimerEnd(t);

		return result;
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

	public List<VFileInfo> Get(VDirectoryInfo directory, bool recursive = false) =>
		Get(directory.VDirectory, recursive);
	public List<VFileInfo> Get(VDirectory directory, bool recursive = false)
	{
		var t = Tools.TimerStart(Context("Get(directory)"));

		var results = GetVersions(directory, recursive, VersionQuery.Latest);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVersions(VFilePath path, VersionQuery versionQuery = VersionQuery.Versions)
	{
		var t = Tools.TimerStart(Context("GetVersions(path, versionQuery)"));

		var results = GetVersions(path.AsList(), versionQuery);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVersions(List<VFilePath> paths, VersionQuery versionQuery = VersionQuery.Versions)
	{
		var t = Tools.TimerStart(Context("GetVersions(paths, versionQuery)"));

		var vfiles = Database.GetVFilesByFilePath(paths, versionQuery);
		var results = ConvertDbVFile(vfiles);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVersions(
		VDirectoryInfo directory,
		bool recursive = false,
		VersionQuery versionQuery = VersionQuery.Versions) =>
		GetVersions(directory.VDirectory, recursive, versionQuery);

	public List<VFileInfo> GetVersions(
		VDirectory directory,
		bool recursive = false,
		VersionQuery versionQuery = VersionQuery.Versions)
	{
		var t = Tools.TimerStart(Context("GetVersions(directory, recursive, versionQuery)"));

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
	/// Get <see cref="DirectoryStats"/> for this VFile instance.
	/// </summary>
	public DirectoryStats GetStats() => GetDirectory(VDirectory.RootDirectory())!.RecursiveStats;
	public VDirectoryInfo? GetDirectory(VDirectory directory)
	{
		var t = Tools.TimerStart(Context("GetDirectory(directory)"));

		var dbInfo = Database.GetDirectoryInfo(directory.Path);

		VDirectoryInfo? info = null;
		if (dbInfo != null)
		{
			var dirStats = new DirectoryStats(
				dbInfo.VFileCount,
				dbInfo.VersionedCount,
				dbInfo.ContentCount,
				dbInfo.DirectoryCount,
				dbInfo.SizeTotal,
				dbInfo.SizeContentTotal,
				dbInfo.VersionedSizeTotal,
				dbInfo.VersionedSizeContentTotal);

			var recursiveStats = new DirectoryStats(
				dbInfo.RecursiveVFileCount,
				dbInfo.RecursiveVersionedCount,
				dbInfo.RecursiveContentCount,
				dbInfo.RecursiveDirectoryCount,
				dbInfo.RecursiveSizeTotal,
				dbInfo.RecursiveSizeContentTotal,
				dbInfo.RecursiveVersionedSizeTotal,
				dbInfo.RecursiveVersionedSizeContentTotal);

			info = new(dbInfo.Directory, dirStats, recursiveStats);
		}

		Tools.TimerEnd(t);

		return info;
	}

	/// <summary>
	/// Returns copied VFileInfo. If null, VFile was not found at request.From.
	/// </summary>
	/// <param name="request"></param>
	/// <returns></returns>
	public VFileInfo? Copy(CopyRequest request, VersionQuery versionQuery = VersionQuery.Latest, StoreOptions? opts = null) =>
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

		return Store(storeRequests).NewVFiles;
	}

	/// <summary>
	/// Copy every vfile in a given Directory.<br/>
	/// recursive will copy all vfiles in every subdirectory.<br/>
	/// Returns copied VFileInfos.
	/// </summary>
	public List<VFileInfo> Copy(
		VDirectoryInfo directory,
		VDirectory to,
		bool recursive = false,
		VersionQuery versionQuery = VersionQuery.Latest,
		StoreOptions? opts = null) =>
		Copy(directory.VDirectory, to, recursive, versionQuery, opts);

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
		var t = Tools.TimerStart(Context("Copy(directory, to, recursive, opts)"));

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
		var t = Tools.TimerStart(Context("Move(requests, versionQuery)"));

		var copied = Copy(requests, versionQuery, opts);
		var deleted = Delete([.. requests.Select(x => x.From)], versionQuery);

		Tools.TimerEnd(t);

		return new(copied, deleted);
	}

	/// <summary>
	/// Copies then deletes given directory, all subdirectories, and all vfiles within them, including versions.<br/>
	/// </summary>
	public MoveResult Move(
		VDirectoryInfo directory,
		VDirectory to,
		StoreOptions? opts = null) =>
		Move(directory.VDirectory, to, opts);

	/// <summary>
	/// Copies then deletes given directory, all subdirectories, and all vfiles within them, including versions.<br/>
	/// </summary>
	public MoveResult Move(
		VDirectory directory,
		VDirectory to,
		StoreOptions? opts = null)
	{
		var t = Tools.TimerStart(Context("Move(directory, to, recursive, opts)"));

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

		Tools.TimerEnd(t);

		return [.. vfiles.Select(x => new VFileInfo(x))];
	}

	/// <summary>
	/// Deletes given directory, all subdirectories, and all vfiles within them, including versions.<br/>
	/// Returns deleted vfiles.
	/// </summary>
	public List<VFileInfo> Delete(VDirectoryInfo directory) => Delete(directory.VDirectory);

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

	public StoreResult Store(StoreRequest request)
	{
		return Store(request.AsList());
	}

	public StoreResult Store(List<StoreRequest> requests)
	{
		// this function builds up all the VFileInfo changes within
		// a StoreState object that is saved at the very end.
		// New Content is also saved in bulk at the very end.
		// This is all very much NOT thread-safe.

		var t = Tools.TimerStart(FunctionContext(nameof(Store)));
		var timer = Timer.Default(); // re-usable timer
		var metrics = new StoreVFilesMetrics();
		var results = new List<VFileInfo>();
		var errors = new List<StoreRequest>();
		var state = new StoreState();
		var saveHashContentMap = new Dictionary<string, (VFileInfo Info, byte[] Bytes)>();
		var uniqueFilePaths = new Dictionary<string, VFilePath>();

		// this validation loop also serves to gather unique VFilePaths.
		timer = Tools.TimerStart(FunctionContext(nameof(Store), "Validate requests"));
		foreach (var request in requests)
		{
			var path = request.Path;
			if (!Assert_ValidFileName(path.FileName, nameof(Store)))
			{
				errors.Add(request);
			}

			if (uniqueFilePaths.ContainsKey(path.FilePath))
			{
				Hooks.ErrorHandler(new(
					VFileErrorCodes.DuplicateStoreVFileRequest,
					$"Duplicate StoreVFileRequest detected: {path.FilePath}",
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
			timer = Tools.TimerStart(FunctionContext(nameof(Store), "Get existing VFiles via GetVFilesByFilePath"));

			var existingVFiles = Database.GetVFilesByFilePath([.. uniqueFilePaths.Values], VersionQuery.Latest)
				.ToDictionary(x => x.Directory.Path + x.VFile.FileName);

			Tools.TimerEnd(timer);

			foreach (var request in requests)
			{
				var rqt = Tools.TimerStart(FunctionContext(nameof(Store), "Process request"));

				var path = request.Path;

				var now = DateTimeOffset.Now;
				var opts = request.Opts ?? DefaultStoreOptions;

				timer = Tools.TimerStart(FunctionContext(nameof(Store), "Process Content"));

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
					hash = Util.HashSHA256(bytes);
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
								Hooks.ErrorHandler(new(
									VFileErrorCodes.OverwriteNotAllowed,
									$"VFileVersionBehavior is set to Error. Request to overwrite existing file not allowed: {path.FilePath}",
									request));
								errors.Add(request);
							}
							break;
						}

						case VFileExistsBehavior.Version:
						{
							timer = Tools.TimerStart(FunctionContext(nameof(Store), "VFileExistsBehavior.Version"));

							if (contentDifference)
							{
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

			timer = Tools.TimerStart(FunctionContext(nameof(Store), "Database.SaveFileContent"));

			// wait for all content to finish saving
			Database.SaveFileContent([.. saveHashContentMap.Select(x => x.Value)]);

			Tools.TimerEnd(timer);
			timer = Tools.TimerStart(FunctionContext(nameof(Store), "Database.SaveStoreState"));

			Database.SaveStoreState(state);

			Tools.TimerEnd(timer);
			Tools.TimerEnd(t); // overall SaveVFiles timer
			Tools.Metrics.StoreVFilesMetrics.Add(metrics);
		}
		finally
		{
			Mutex.ReleaseMutex();
		}

		return new([.. results], [.. errors]);
	}

	private static List<VFileInfo> ConvertDbVFile(List<Db.VFileModel> vfiles)
	{
		return [.. vfiles.Select(x => new VFileInfo(x))];
	}

	private bool Assert_ValidFileName(string fileName, string context)
	{
		if (fileName.IsEmpty())
		{
			Hooks.ErrorHandler(new(
				VFileErrorCodes.InvalidParameter,
				$"{context}: Invalid FileName - must have a value",
				"FileName"));
			return false;
		}

		return true;
	}
}
