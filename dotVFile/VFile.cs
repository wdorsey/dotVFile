namespace dotVFile;

public class VFile
{
	public const string Version = "1.0.0";

	private static string Context(string ctx) => $"{nameof(VFile)}.{ctx}";
	private static string FunctionContext(string fnName) => Context($"{fnName}()");
	private static string FunctionContext(string fnName, string ctx) => Context($"{fnName}(): {ctx}");

	public VFile(VFileOptions opts) : this(x => opts) { }
	public VFile(Func<VFileOptions, VFileOptions> configure)
	{
		var opts = VFileOptions.Default();
		opts = configure(opts);

		if (opts.Directory.IsEmpty() || !Path.IsPathFullyQualified(opts.Directory))
			throw new Exception($"Invalid Directory: \"{opts.Directory}\". Directory must be a valid path where this VFile instance will store its single file, such as: \"C:\\dotVFile\".");

		Name = opts.Name.HasValue() ? opts.Name : "dotVFile";
		Directory = Util.CreateDir(opts.Directory);
		Tools = new VFileTools(this, opts.Hooks);
		Database = new VFileDatabase(new(Name, Directory, Version, opts.Permissions, Tools));
		DefaultStoreOptions = opts.DefaultStoreOptions;
		Debug = opts.Debug;

		Clean();
	}

	internal VFileDatabase Database { get; private set; }
	public string Name { get; private set; }
	public string Directory { get; private set; }
	internal VFileTools Tools { get; private set; }
	public IVFileHooks Hooks => Tools.Hooks;
	public VFileStoreOptions DefaultStoreOptions { get; private set; }
	public bool Debug { get; set; }
	public SystemInfo SystemInfo => ConvertDbSystemInfo(Database.GetSystemInfo());

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
	/// !!! DANGER !!!
	/// This will delete EVERYTHING.
	/// This VFile instance will no longer work.
	/// </summary>
	public void DANGER_Destroy()
	{
		Database.DeleteDatabase();
	}

	/// <summary>
	/// Deletes VFiles that have passed their DeleteAt time.<br/>
	/// Deletes any dangling, unreferenced Content.
	/// </summary>
	public VFileCleanResult Clean()
	{
		// Some of the unreferenced data should normally be deleted during StoreVFiles 
		// but it isn't for both performance reasons and because it has
		// to be checked here anyways because expired VFiles are deleted.

		var t = Tools.TimerStart(FunctionContext(nameof(Clean)));

		// delete expired VFiles first so that their content and directories 
		// are freed to be cleaned up via DeleteUnreferencedEntities.
		var expired = Database.DeleteExpiredVFiles();
		var unreferenced = Database.DeleteUnreferencedFileContent();

		var sysInfo = Database.GetSystemInfo() with { LastClean = DateTimeOffset.Now };
		Database.UpdateSystemInfo(sysInfo);

		var result = new VFileCleanResult(unreferenced, expired);

		Tools.DebugLog($"{t.Name} => {result.ToJson(true)}");
		Tools.TimerEnd(t);

		return result;
	}

	public VFileInfo? GetVFileInfo(VFilePath path)
	{
		var t = Tools.TimerStart(Context("GetVFileInfo(VFilePath path)"));

		var result = GetVFileInfoVersions(path, VFileInfoVersionQuery.Latest).SingleOrDefault();

		Tools.TimerEnd(t);

		return result;
	}

	public List<VFileInfo> GetVFileInfos(List<VFilePath> paths)
	{
		var t = Tools.TimerStart(Context("GetVFileInfos(List<VFilePath> paths)"));

		var results = GetVFileInfoVersions(paths, VFileInfoVersionQuery.Latest);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVFileInfos(VDirectory directory)
	{
		var t = Tools.TimerStart(Context("GetVFileInfos(VDirectory directory)"));

		var results = GetVFileInfoVersions(directory, VFileInfoVersionQuery.Latest);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVFileInfoVersions(VFilePath path, VFileInfoVersionQuery versionQuery)
	{
		var t = Tools.TimerStart(Context("GetVFileInfoVersions(VFilePath path, VFileInfoVersionQuery versionQuery)"));

		var results = GetVFileInfoVersions(path.AsList(), versionQuery);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVFileInfoVersions(List<VFilePath> paths, VFileInfoVersionQuery versionQuery)
	{
		var t = Tools.TimerStart(Context("GetVFileInfoVersions(List<VFilePath> paths, VFileInfoVersionQuery versionQuery)"));

		var vfiles = Database.GetVFilesByFilePath(paths, versionQuery);
		var results = ConvertDbVFile(vfiles);

		Tools.TimerEnd(t);

		return results;
	}

	public List<VFileInfo> GetVFileInfoVersions(VDirectory directory, VFileInfoVersionQuery versionQuery)
	{
		var t = Tools.TimerStart(Context("GetVFileInfoVersions(VDirectory directory, VFileInfoVersionQuery versionQuery)"));

		var vfiles = Database.GetVFilesByDirectory([directory.Path], versionQuery);
		var results = ConvertDbVFile(vfiles);

		Tools.TimerEnd(t);

		return results;
	}

	public byte[]? GetBytes(VFilePath path)
	{
		var t = Tools.TimerStart(Context("GetBytes(VFilePath path)"));

		var vfile = Database.GetVFilesByFilePath(path.AsList(), VFileInfoVersionQuery.Latest).SingleOrDefault();
		var result = GetBytes(vfile);

		Tools.TimerEnd(t);

		return result;
	}

	public byte[]? GetBytes(VFileInfo info)
	{
		var t = Tools.TimerStart(Context("GetBytes(VFileInfo info)"));

		var vfile = Database.GetVFilesById(info.Id.AsList()).SingleOrDefault();
		var result = GetBytes(vfile);

		Tools.TimerEnd(t);

		return result;
	}

	private byte[]? GetBytes(Db.VFileModel? vfile)
	{
		if (vfile == null) return null;

		var t = Tools.TimerStart(Context("GetBytes(Db.VFileModel? vfile)"));

		var bytes = Database.GetContentBytes(vfile.FileContent);
		var result = vfile.FileContent.Compression == (byte)VFileCompression.None
			? bytes
			: Util.Decompress(bytes);

		Tools.TimerEnd(t);

		return result;
	}

	public VFileInfo? StoreVFile(
		VFilePath path,
		VFileContent content,
		VFileStoreOptions? opts = null)
	{
		return StoreVFile(new StoreVFileRequest(path, content, opts));
	}

	public VFileInfo? StoreVFile(StoreVFileRequest request)
	{
		return StoreVFiles(request.AsList()).SingleOrDefault();
	}

	public List<VFileInfo> StoreVFiles(List<StoreVFileRequest> requests)
	{
		// this function builds up all the VFileInfo changes within
		// a StoreVFilesState object.
		// Any new VFileContent is immediately saved so that the file bytes
		// are not kept around in memory. This is not harmful should
		// the state fail to save, any orphaned content can be cleaned up later.

		var t = Tools.TimerStart(FunctionContext(nameof(StoreVFiles)));
		var timer = Timer.Default(); // re-usable timer
		var metrics = new StoreVFilesMetrics();

		var result = new List<VFileInfo>();
		var state = new StoreVFilesState();
		var uniqueFilePaths = new HashSet<string>();
		var contentHashes = new HashSet<string>();
		var saveContent = new List<(VFileInfo Info, byte[] Bytes)>();

		foreach (var request in requests)
		{
			var rqt = Tools.TimerStart(FunctionContext(nameof(StoreVFiles), "Process request"));

			var path = request.Path;

			if (!Assert_ValidFileName(path.FileName, nameof(StoreVFiles)))
			{
				Tools.TimerEnd(rqt);
				Tools.TimerEnd(t);
				return [];
			}

			if (uniqueFilePaths.Contains(path.FilePath))
			{
				Hooks.ErrorHandler(new(
					VFileErrorCodes.DuplicateStoreVFileRequest,
					$"Duplicate StoreVFileRequest detected: {path.FilePath}",
					request));
				Tools.TimerEnd(rqt);
				Tools.TimerEnd(t);
				return [];
			}
			uniqueFilePaths.Add(path.FilePath);

			var now = DateTimeOffset.Now;
			var opts = request.Opts ?? DefaultStoreOptions;

			timer = Tools.TimerStart(FunctionContext(nameof(StoreVFiles), "Process Content"));

			var content = request.Content.GetContent();
			var bytes = opts.Compression == VFileCompression.Compress
				? Util.Compress(content)
				: content;
			var hash = Util.HashSHA256(bytes);
			metrics.ContentSizes.Add(bytes.Length);

			Tools.TimerEnd(timer);
			timer = Tools.TimerStart(FunctionContext(nameof(StoreVFiles), "Database.GetVFilesByFilePath"));

			var existingVFile = Database.GetVFilesByFilePath(
					path.AsList(),
					VFileInfoVersionQuery.Latest)
				.SingleOrDefault();

			Tools.TimerEnd(timer);

			var newInfo = new VFileInfo
			{
				Id = Guid.NewGuid(),
				VFilePath = path,
				DeleteAt = opts.TTL.HasValue ? now + opts.TTL : null,
				CreationTime = now,
				ContentId = Guid.NewGuid(),
				Hash = hash,
				Size = content.Length,
				SizeStored = bytes.Length,
				Compression = opts.Compression,
				ContentCreationTime = now
			};

			// save and check hashes to prevent saving the same content more than once.
			if (!contentHashes.Contains(hash) &&
				(existingVFile?.FileContent == null ||
				existingVFile.FileContent.Hash != hash))
			{
				// bulk saving these at the end is the fastest method I've found.
				saveContent.Add((newInfo, bytes));
			}
			contentHashes.Add(hash);

			if (existingVFile == null)
			{
				state.NewVFiles.Add(newInfo);
				result.Add(newInfo);
			}
			else
			{
				// previous VFileInfo exists but content is different.
				var contentDifference = existingVFile.FileContent != null && existingVFile.FileContent.Hash != hash;
				result.Add(contentDifference ? newInfo : new VFileInfo(existingVFile));

				switch (opts.VersionOpts.ExistsBehavior)
				{
					case VFileExistsBehavior.Overwrite:
					{
						if (contentDifference)
						{
							state.DeleteVFiles.Add(existingVFile.VFile);
							state.NewVFiles.Add(newInfo);
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
							Tools.TimerEnd(rqt);
							Tools.TimerEnd(t);
							return [];
						}
						break;
					}

					case VFileExistsBehavior.Version:
					{
						timer = Tools.TimerStart(FunctionContext(nameof(StoreVFiles), "VFileExistsBehavior.Version"));

						var versions = Database.GetVFilesByFilePath(path.AsList(), VFileInfoVersionQuery.Versions);

						if (contentDifference)
						{
							existingVFile.VFile.Versioned = now;
							versions.Add(existingVFile);
							state.UpdateVFiles.Add(existingVFile.VFile);
							state.NewVFiles.Add(newInfo);
						}

						// always check for TTL and MaxVersionsRetained changes
						foreach (var v in versions)
						{
							// always updates version's DeleteAt to the current opts.VersionOpts.TTL.
							// DeleteAt calculated off the Versioned timestamp.
							var expected = opts.VersionOpts.TTL.HasValue
								? v.VFile.Versioned + opts.VersionOpts.TTL
								: null;

							if (v.VFile.DeleteAt != expected)
							{
								v.VFile.DeleteAt = expected;
								// AddSafe to prevent adding the existingVFile twice
								state.UpdateVFiles.AddSafe(v.VFile);
							}
						}

						var maxVersions = opts.VersionOpts.MaxVersionsRetained;
						if (maxVersions.HasValue && versions.Count > maxVersions)
						{
							var delete = versions.Select(x => x.VFile)
								.OrderByDescending(x => x.Versioned)
								.Skip(maxVersions.Value);

							state.DeleteVFiles.AddRange(delete);
						}

						Tools.TimerEnd(timer);

						break;
					}
				}
			}

			Tools.TimerEnd(rqt);
		}

		timer = Tools.TimerStart(FunctionContext(nameof(StoreVFiles), "Database.SaveFileContent"));

		// wait for all content to finish saving
		Database.SaveFileContent(saveContent);

		Tools.TimerEnd(timer);
		timer = Tools.TimerStart(FunctionContext(nameof(StoreVFiles), "Database.SaveStoreVFilesState"));

		Database.SaveStoreVFilesState(state);

		Tools.TimerEnd(timer);
		Tools.TimerEnd(t); // overall SaveVFiles timer
		Tools.Metrics.StoreVFilesMetrics.Add(metrics);

		return result;
	}

	private static List<VFileInfo> ConvertDbVFile(List<Db.VFileModel> vfiles)
	{
		return [.. vfiles.Select(x => new VFileInfo(x))];
	}

	private static SystemInfo ConvertDbSystemInfo(Db.SystemInfo info)
	{
		return new(
			info.ApplicationId,
			info.Version,
			info.LastClean,
			info.LastUpdate);
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
