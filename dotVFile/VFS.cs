namespace dotVFile;

public class VFS
{
	public const char DirectorySeparator = '/';

	public static VFileStoreOptions GetDefaultStoreOptions() =>
		new(VFileCompression.None, null, GetDefaultVersionOptions());

	public static VFileVersionOptions GetDefaultVersionOptions() =>
		new(VFileVersionBehavior.Overwrite, null, null);

	public VFS(VFSOptions opts)
	{
		if (opts.VFileDirectory.IsEmpty() || !Path.IsPathFullyQualified(opts.VFileDirectory))
			throw new Exception($"Invalid VFileDirectory: \"{opts.VFileDirectory}\". VFileDirectory must be a valid directory where this VFS instance will store its file, such as: \"C:\\dotVFile\".");

		Name = opts.Name.HasValue() ? opts.Name : "dotVFile";
		VFileDirectory = Util.CreateDir(opts.VFileDirectory);
		Hooks = opts.Hooks ?? new NotImplementedHooks();
		Database = new VFileDatabase(new(Name, VFileDirectory, Hooks));
		DefaultStoreOptions = opts.DefaultStoreOptions ?? GetDefaultStoreOptions();
	}

	internal VFileDatabase Database { get; }
	public string Name { get; }
	public string VFileDirectory { get; }
	public IVFileHooks Hooks { get; }
	public VFileStoreOptions DefaultStoreOptions { get; }

	/// <summary>
	/// Gets the single database file path that _is_ the entire virtual file system.
	/// This file could potentially be very large, so take care in how you retrieve it programmatically.
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

	public VFileInfo? GetVFileInfo(VFilePath path)
	{
		return GetVFileInfoVersions(
			StandardizePath(path),
			VFileInfoVersionQuery.Latest)
			.SingleOrDefault();
	}

	public List<VFileInfo> GetVFileInfos(string directory, bool recursive)
	{
		var directories = recursive
			? GetDirectoriesRecursive(directory)
			: StandardizeDirectory(directory).AsList();

		var query = new Db.VFileInfoQuery
		{
			Directories = directories,
			VersionQuery = VFileInfoVersionQuery.Latest
		};
		var vfiles = Database.QueryVFiles(query);

		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfos(List<VFilePath> paths)
	{
		var query = new Db.VFileInfoQuery
		{
			FilePaths = [.. paths.Select(x => StandardizePath(x).FilePath)],
			VersionQuery = VFileInfoVersionQuery.Latest
		};
		var vfiles = Database.QueryVFiles(query);

		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfoVersions(VFilePath path, VFileInfoVersionQuery versionQuery)
	{
		var query = new Db.VFileInfoQuery
		{
			FilePaths = StandardizePath(path).FilePath.AsList(),
			VersionQuery = versionQuery
		};
		var vfiles = Database.QueryVFiles(query);

		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfoVersions(string directory, bool recursive, VFileInfoVersionQuery versionQuery)
	{
		var directories = recursive
			? GetDirectoriesRecursive(directory)
			: StandardizeDirectory(directory).AsList();

		var query = new Db.VFileInfoQuery
		{
			Directories = directories,
			VersionQuery = versionQuery
		};
		var vfiles = Database.QueryVFiles(query);

		return DbVFileToVFileInfo(vfiles);
	}

	public VFile? GetVFile(VFilePath path)
	{
		var query = new Db.VFileInfoQuery
		{
			FilePaths = StandardizePath(path).FilePath.AsList(),
			VersionQuery = VFileInfoVersionQuery.Latest
		};
		var vfile = Database.QueryVFiles(query).SingleOrDefault();
		if (vfile == null)
			return null;

		return GetVFiles(vfile.AsList()).SingleOrDefault();
	}

	public VFile? GetVFile(VFileInfo info)
	{
		return GetVFiles(info.AsList()).SingleOrDefault();
	}

	public List<VFile> GetVFiles(List<VFilePath> paths)
	{
		return GetVFiles(GetVFileInfos(paths));
	}

	public List<VFile> GetVFiles(List<VFileInfo> infos)
	{
		var query = new Db.VFileInfoQuery
		{
			Ids = [.. infos.Select(x => x.Id)]
		};
		var vfiles = Database.QueryVFiles(query);

		return GetVFiles(vfiles);
	}

	public List<VFile> GetVFiles(string directory, bool recursive)
	{
		return GetVFiles(GetVFileInfos(directory, recursive));
	}

	public byte[]? GetVFileBytes(VFilePath path)
	{
		return GetVFile(path)?.Content;
	}

	private List<VFile> GetVFiles(List<Db.VFile> dbVFiles)
	{
		Database.FetchContent([.. dbVFiles.Select(x => x.VFileContent)]);

		return [.. dbVFiles.Select(x =>
		{
			// @note: Content should never be null here, only null coalescing here to make the compiler happy.
			var content = x.VFileContent.Compression == (byte)VFileCompression.None
				? x.VFileContent.Content ?? Util.EmptyBytes()
				: Util.Decompress(x.VFileContent.Content ?? Util.EmptyBytes());

			return new VFile(DbVFileToVFileInfo(x), content);
		})];
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

		var result = new List<VFileInfo>();
		var state = new StoreVFilesState();
		var uniqueMap = new HashSet<string>();

		foreach (var request in requests)
		{
			var path = StandardizePath(request.Path);

			if (!Assert_ValidFileName(path.FileName, nameof(StoreVFiles)))
				return [];

			if (uniqueMap.Contains(path.FilePath))
			{
				Hooks.ErrorHandler(new(
					VFileErrorCodes.DuplicateStoreVFileRequest,
					$"Duplicate StoreVFileRequest detected: {request.Path.FilePath}",
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

			var existingVFile = Database.QueryVFiles(
				new Db.VFileInfoQuery
				{
					FilePaths = path.FilePath.AsList(),
					VersionQuery = VFileInfoVersionQuery.Latest
				}).SingleOrDefault();

			var existingContent = existingVFile?.VFileContent;

			// if the existingVFile content matches the passed in content (via hash),
			// then we can just use that instead of having to query the database.
			var vfileContent = existingContent != null && existingContent.Hash == hash
				? existingContent
				: Database.QueryVFileContent(
					new Db.VFileContentQuery
					{
						Hashes = hash.AsList()
					}).SingleOrDefault();

			var newInfo = new VFileInfo
			{
				Id = Guid.NewGuid(),
				VFilePath = path,
				UserVFilePath = request.Path,
				DeleteAt = opts.TTL.HasValue ? now + opts.TTL : null,
				CreationTime = now,
				ContentId = Guid.NewGuid(),
				Hash = hash,
				Size = content.Length,
				SizeStored = bytes.Length,
				Compression = opts.Compression,
				ContentCreationTime = now
			};

			if (vfileContent == null)
			{
				// save new content immediately, GC can free bytes
				vfileContent = Database.SaveVFileContent(newInfo, bytes);
			}

			if (existingVFile == null)
			{
				state.NewVFileInfos.Add(newInfo);
				result.Add(newInfo);
			}
			else
			{
				// previous VFileInfo exists but content is different.
				var contentDifference = existingContent != null && existingContent.Hash != hash;
				result.Add(contentDifference ? newInfo : DbVFileToVFileInfo(existingVFile));
				switch (opts.VersionOpts.Behavior)
				{
					case VFileVersionBehavior.Overwrite:
					{
						if (contentDifference)
						{
							state.DeleteVFileInfos.Add(existingVFile.VFileInfo);
							state.NewVFileInfos.Add(newInfo);
						}
						break;
					}

					case VFileVersionBehavior.Error:
					{
						if (contentDifference)
						{
							Hooks.ErrorHandler(new(
								VFileErrorCodes.OverwriteNotAllowed,
								$"VFileVersionBehavior is set to Error. Request to overwrite existing file not allowed: {request.Path.FilePath}",
								request));
							return [];
						}
						break;
					}

					case VFileVersionBehavior.Version:
					{
						var versions = Database.QueryVFiles(
							new Db.VFileInfoQuery
							{
								FilePaths = path.FilePath.AsList(),
								VersionQuery = VFileInfoVersionQuery.Versions
							});

						if (contentDifference)
						{
							existingVFile.VFileInfo.Versioned = now;
							versions.Add(existingVFile);
							state.UpdateVFileInfos.Add(existingVFile.VFileInfo);
							state.NewVFileInfos.Add(newInfo);
						}

						// always check for TTL and MaxVersions changes
						foreach (var v in versions)
						{
							// always updates version's DeleteAt to the current opts.VersionOpts.TTL.
							// DeleteAt calculated off the Versioned timestamp.
							var expected = opts.VersionOpts.TTL.HasValue
								? v.VFileInfo.Versioned + opts.VersionOpts.TTL
								: null;
							if (v.VFileInfo.DeleteAt != expected)
							{
								v.VFileInfo.DeleteAt = expected;
								// AddSafe to prevent adding the existingVFile twice
								state.UpdateVFileInfos.AddSafe(v.VFileInfo);
							}
						}

						var maxVersions = opts.VersionOpts.MaxVersionsRetained;
						if (maxVersions.HasValue && versions.Count > maxVersions)
						{
							var delete = versions.Select(x => x.VFileInfo)
								.OrderByDescending(x => x.Versioned)
								.Skip(maxVersions.Value);
							state.DeleteVFileInfos.AddRange(delete);
						}
						break;
					}
				}
			}
		}

		var dbResult = Database.SaveStoreVFilesState(state);

		// @TODO: probably move this into the clean-up operation
		var rowIds = Database.GetUnreferencedVFileContentRowIds();
		Database.DeleteVFileContent(rowIds);

		return dbResult != null ? result : [];
	}

	/// <summary>
	/// Standardizes all directories to use DirectorySeparator '/'
	/// and the full path always starts and ends with '/'.
	/// e.g. /x/y/z/ 
	/// </summary>
	private static string StandardizeDirectory(string? directory)
	{
		char[] dividers = ['/', '\\'];
		var parts = directory?.Split(dividers, StringSplitOptions.RemoveEmptyEntries);
		var result = DirectorySeparator.ToString();
		if (parts.AnySafe())
		{
			result += string.Join(DirectorySeparator, parts);
			result += DirectorySeparator;
		}
		return result;
	}

	private static VFilePath StandardizePath(VFilePath path)
	{
		var dir = StandardizeDirectory(path.Directory);
		return new(dir, path.FileName, $"{dir}{path.FileName}");
	}

	internal static List<string> DirectoryParts(string? directory)
	{
		if (directory.IsEmpty()) return [];

		char[] dividers = ['/', '\\'];
		return [.. directory.Split(dividers, StringSplitOptions.RemoveEmptyEntries)];
	}

	/// <summary>
	/// /x/y/z/ => [/x/, /x/y/, /x/y/z/]
	/// </summary>
	private static List<string> GetDirectoriesRecursive(string directory)
	{
		var result = new List<string>();
		var parts = DirectoryParts(StandardizeDirectory(directory));
		var prev = DirectorySeparator.ToString();
		foreach (var dir in parts)
		{
			prev += dir + DirectorySeparator;
			result.Add(prev);
		}
		return result;
	}

	private static List<VFileInfo> DbVFileToVFileInfo(List<Db.VFile> vfiles)
	{
		return [.. vfiles.Select(DbVFileToVFileInfo)];
	}

	private static VFileInfo DbVFileToVFileInfo(Db.VFile vfile)
	{
		return new VFileInfo
		{
			Id = vfile.VFileInfo.Id,
			VFilePath = new(vfile.VFileInfo.Directory, vfile.VFileInfo.FileName, vfile.VFileInfo.FilePath),
			Versioned = vfile.VFileInfo.Versioned,
			DeleteAt = vfile.VFileInfo.DeleteAt,
			CreationTime = vfile.VFileInfo.CreationTime,
			ContentId = vfile.VFileContent.Id,
			Hash = vfile.VFileContent.Hash,
			Size = vfile.VFileContent.Size,
			SizeStored = vfile.VFileContent.SizeStored,
			Compression = (VFileCompression)vfile.VFileContent.Compression,
			ContentCreationTime = vfile.VFileContent.CreationTime
		};
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
