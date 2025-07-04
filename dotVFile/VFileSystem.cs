namespace dotVFile;

internal class HooksWrapper(VFileSystem vfs, IVFileHooks? hooks) : IVFileHooks
{
	private readonly VFileSystem VFS = vfs;
	private readonly IVFileHooks Hooks = hooks ?? new NotImplementedVFileHooks();

	public void ErrorHandler(VFileError error)
	{
		Hooks.ErrorHandler(error);
	}

	public void DebugLog(string msg)
	{
		if (VFS.Debug)
			Hooks.DebugLog(msg);
	}
}

public class VFileSystem
{
	public const string Version = "1.0.0";

	public VFileSystem(VFileSystemOptions opts) : this(x => x = opts) { }
	public VFileSystem(Action<VFileSystemOptions> configure)
	{
		var opts = VFileSystemOptions.Default();
		configure(opts);

		if (opts.Directory.IsEmpty() || !Path.IsPathFullyQualified(opts.Directory))
			throw new Exception($"Invalid Directory: \"{opts.Directory}\". Directory must be a valid directory where this VFS instance will store its file, such as: \"C:\\dotVFile\".");

		Name = opts.Name.HasValue() ? opts.Name : "dotVFile";
		Directory = Util.CreateDir(opts.Directory);
		Hooks = new HooksWrapper(this, opts.Hooks);
		Database = new VFileDatabase(new(Name, Directory, Version, Hooks, opts.EnforceSingleInstance));
		DefaultStoreOptions = opts.DefaultStoreOptions ?? VFileStoreOptions.Default();
		Debug = opts.Debug;

		Clean();
	}

	internal VFileDatabase Database { get; private set; }
	public string Name { get; private set; }
	public string Directory { get; private set; }
	public IVFileHooks Hooks { get; private set; }
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
	/// This VFS instance will still work, but have no data.
	/// </summary>
	public void DANGER_WipeData()
	{
		Database.DropDatabase();
		Database.CreateDatabase();
	}

	/// <summary>
	/// !!! DANGER !!!
	/// This will delete EVERYTHING.
	/// This VFS instance will no longer work.
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
		// but it is not for both performance reasons and because it has
		// to be checked here anyways after the expired VFiles are deleted.

		var t = Hooks.LogTimerStart(nameof(Clean));

		// delete expired VFiles first so that their content and directories 
		// are freed to be cleaned up via DeleteUnreferencedEntities.
		var expired = Database.DeleteExpiredVFiles();
		var unreferenced = Database.DeleteUnreferencedEntities();

		var sysInfo = Database.GetSystemInfo() with { LastClean = DateTimeOffset.Now };
		Database.UpdateSystemInfo(sysInfo);

		var result = new VFileCleanResult(unreferenced, expired);
		Hooks.DebugLog($"{nameof(Clean)}() result: {result.ToJson(true)}");
		Hooks.LogTimerEnd(t);
		return result;
	}

	public VFileInfo? GetVFileInfo(VFilePath path)
	{
		return GetVFileInfoVersions(path, VFileInfoVersionQuery.Latest).SingleOrDefault();
	}

	public List<VFileInfo> GetVFileInfos(List<VFilePath> paths)
	{
		return GetVFileInfoVersions(paths, VFileInfoVersionQuery.Latest);
	}

	public List<VFileInfo> GetVFileInfos(VDirectory directory)
	{
		return GetVFileInfoVersions(directory, VFileInfoVersionQuery.Latest);
	}

	public List<VFileInfo> GetVFileInfoVersions(VFilePath path, VFileInfoVersionQuery versionQuery)
	{
		return GetVFileInfoVersions(path.AsList(), versionQuery);
	}

	public List<VFileInfo> GetVFileInfoVersions(List<VFilePath> paths, VFileInfoVersionQuery versionQuery)
	{
		var vfiles = Database.GetVFilesByFilePath(paths, versionQuery);

		return ConvertDbVFile(vfiles);
	}

	public List<VFileInfo> GetVFileInfoVersions(VDirectory directory, VFileInfoVersionQuery versionQuery)
	{
		var vfiles = Database.GetVFilesByDirectory([directory.Path], versionQuery);

		return ConvertDbVFile(vfiles);
	}

	public byte[]? GetBytes(VFilePath path)
	{
		var vfile = Database.GetVFilesByFilePath(path.AsList(), VFileInfoVersionQuery.Latest).SingleOrDefault();

		return GetBytes(vfile);
	}

	public byte[]? GetBytes(VFileInfo info)
	{
		var vfile = Database.GetVFilesById(info.Id.AsList()).SingleOrDefault();

		return GetBytes(vfile);
	}

	private byte[]? GetBytes(Db.VFileModel? vfile)
	{
		if (vfile == null) return null;

		var bytes = Database.GetContentBytes(vfile.FileContent);

		return vfile.FileContent.Compression == (byte)VFileCompression.None
			? bytes
			: Util.Decompress(bytes);
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

		var t = Hooks.LogTimerStart(nameof(StoreVFiles));

		var result = new List<VFileInfo>();
		var state = new StoreVFilesState();
		var uniqueMap = new HashSet<string>();

		foreach (var request in requests)
		{
			var path = request.Path;

			if (!Assert_ValidFileName(path.FileName, nameof(StoreVFiles)))
				return [];

			if (uniqueMap.Contains(path.FilePath))
			{
				Hooks.ErrorHandler(new(
					VFileErrorCodes.DuplicateStoreVFileRequest,
					$"Duplicate StoreVFileRequest detected: {path.FilePath}",
					request));
				return [];
			}
			uniqueMap.Add(path.FilePath);

			var now = DateTimeOffset.Now;
			var opts = request.Opts ?? DefaultStoreOptions;

			var content = request.Content.GetContent();
			var bytes = opts.Compression == VFileCompression.Compress
				? Util.Compress(content)
				: content;
			var hash = Util.HashSHA256(bytes);

			var existingVFile = Database.GetVFilesByFilePath(
				path.AsList(),
				VFileInfoVersionQuery.Latest)
				.SingleOrDefault();

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

			if (existingVFile?.FileContent == null ||
				existingVFile.FileContent.Hash != hash)
			{
				// save new content immediately, GC can free bytes
				// Database.SaveFileContent internally does an existence check against the Hash.
				Database.SaveFileContent(newInfo, bytes);
			}

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
							return [];
						}
						break;
					}

					case VFileExistsBehavior.Version:
					{
						var versions = Database.GetVFilesByFilePath(path.AsList(), VFileInfoVersionQuery.Versions);

						if (contentDifference)
						{
							existingVFile.VFile.Versioned = now;
							versions.Add(existingVFile);
							state.UpdateVFiles.Add(existingVFile.VFile);
							state.NewVFiles.Add(newInfo);
						}

						// always check for TTL and MaxVersions changes
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
						break;
					}
				}
			}
		}

		var dbResult = Database.SaveStoreVFilesState(state);

		Hooks.LogTimerEnd(t);

		return dbResult != null ? result : [];
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
