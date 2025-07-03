namespace dotVFile;

internal class HooksWrapper(VFS vfs, IVFileHooks? hooks) : IVFileHooks
{
	private readonly VFS VFS = vfs;
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

public class VFS
{
	public const char DirectorySeparator = '/';

	public static VFileStoreOptions GetDefaultStoreOptions() =>
		new(VFileCompression.None, null, GetDefaultVersionOptions());

	public static VFileVersionOptions GetDefaultVersionOptions() =>
		new(VFileExistsBehavior.Overwrite, null, null);

	public VFS(VFSOptions opts)
	{
		if (opts.VFileDirectory.IsEmpty() || !Path.IsPathFullyQualified(opts.VFileDirectory))
			throw new Exception($"Invalid VFileDirectory: \"{opts.VFileDirectory}\". VFileDirectory must be a valid directory where this VFS instance will store its file, such as: \"C:\\dotVFile\".");

		Name = opts.Name.HasValue() ? opts.Name : "dotVFile";
		VFileDirectory = Util.CreateDir(opts.VFileDirectory);
		Hooks = new HooksWrapper(this, opts.Hooks);
		Database = new VFileDatabase(new(Name, VFileDirectory, Hooks));
		DefaultStoreOptions = opts.DefaultStoreOptions ?? GetDefaultStoreOptions();
		Debug = opts.Debug;
	}

	internal VFileDatabase Database { get; }
	public string Name { get; }
	public string VFileDirectory { get; }
	public IVFileHooks Hooks { get; }
	public VFileStoreOptions DefaultStoreOptions { get; }
	public bool Debug { get; set; }

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
		return GetVFileInfoVersions(directory, recursive, VFileInfoVersionQuery.Latest);
	}

	public List<VFileInfo> GetVFileInfos(List<VFilePath> paths)
	{
		var vfiles = Database.GetVFilesByFilePath(paths.Select(StandardizePath), VFileInfoVersionQuery.Latest);

		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfoVersions(VFilePath path, VFileInfoVersionQuery versionQuery)
	{
		var vfiles = Database.GetVFilesByFilePath(StandardizePath(path).AsList(), versionQuery);

		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfoVersions(string directory, bool recursive, VFileInfoVersionQuery versionQuery)
	{
		var directories = recursive
			? GetDirectoriesRecursive(directory)
			: StandardizeDirectory(directory).AsList();

		var vfiles = Database.GetVFilesByDirectory(directories, versionQuery);

		return DbVFileToVFileInfo(vfiles);
	}

	public VFile? GetVFile(VFilePath path)
	{
		var vfile = Database.GetVFilesByFilePath(
			StandardizePath(path).AsList(),
			VFileInfoVersionQuery.Latest)
			.SingleOrDefault();

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
		var vfiles = Database.GetVFilesById(infos.Select(x => x.Id));

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

	private List<VFile> GetVFiles(List<Db.VFileModel> dbVFiles)
	{
		Database.FetchContent([.. dbVFiles.Select(x => x.FileContent)]);

		return [.. dbVFiles.Select(x =>
		{
			// @note: Content should never be null here, only null coalescing here to make the compiler happy.
			var content = x.FileContent.Compression == (byte)VFileCompression.None
				? x.FileContent.Content ?? Util.EmptyBytes()
				: Util.Decompress(x.FileContent.Content ?? Util.EmptyBytes());

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
				result.Add(contentDifference ? newInfo : DbVFileToVFileInfo(existingVFile));
				switch (opts.VersionOpts.Behavior)
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
								$"VFileVersionBehavior is set to Error. Request to overwrite existing file not allowed: {request.Path.FilePath}",
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

		// @TODO: probably move this into the clean-up operation
		var unreferenced = Database.GetUnreferencedEntities();
		Database.DeleteDirectory(unreferenced.DirectoryRowIds);
		Database.DeleteFileContent(unreferenced.FileContentRowIds);

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

	private static List<VFileInfo> DbVFileToVFileInfo(List<Db.VFileModel> vfiles)
	{
		return [.. vfiles.Select(DbVFileToVFileInfo)];
	}

	private static VFileInfo DbVFileToVFileInfo(Db.VFileModel vfile)
	{
		var filePath = $"{vfile.Directory.Path}{vfile.VFile.FileName}";
		return new VFileInfo
		{
			Id = vfile.VFile.Id,
			VFilePath = new(vfile.Directory.Path, vfile.VFile.FileName, filePath),
			Versioned = vfile.VFile.Versioned,
			DeleteAt = vfile.VFile.DeleteAt,
			CreationTime = vfile.VFile.CreateTimestamp,
			ContentId = vfile.FileContent.Id,
			Hash = vfile.FileContent.Hash,
			Size = vfile.FileContent.Size,
			SizeStored = vfile.FileContent.SizeContent,
			Compression = (VFileCompression)vfile.FileContent.Compression,
			ContentCreationTime = vfile.FileContent.CreateTimestamp
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
