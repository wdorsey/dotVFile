namespace dotVFile;

public class VFS
{
	public const char PathDirectorySeparator = '/';
	public static VFileStorageOptions GetDefaultStorageOptions() =>
		new(VFileExistsBehavior.Overwrite, VFileCompression.None, null, null, null);

	public VFS(VFSOptions opts)
	{
		if (opts.RootPath.IsEmpty() || !Path.IsPathFullyQualified(opts.RootPath))
			throw new Exception($"Invalid RootPath: \"{opts.RootPath}\". RootPath must be a valid directory where this VFS instance will operate, such as: \"C:\\vfs\".");

		Name = new DirectoryInfo(opts.RootPath).Name;
		Util.CreateDir(opts.RootPath);
		Paths = new(
			opts.RootPath,
			Path.Combine(opts.RootPath, "store"));
		Hooks = opts.Hooks ?? new NotImplementedHooks();
		Database = new VFileDatabase(new(opts.RootPath, Hooks));
		DefaultStorageOptions = opts.DefaultStorageOptions ?? GetDefaultStorageOptions();
	}

	private VFileDatabase Database { get; }
	public string Name { get; }
	public VFSPaths Paths { get; }
	public IVFileHooks Hooks { get; }
	public VFileStorageOptions DefaultStorageOptions { get; }

	/// <summary>
	/// !!! DANGER !!!
	/// This will delete EVERYTHING in RootPath.
	/// This VFS instance will no longer work.
	/// </summary>
	public void Destroy()
	{
		Database.DeleteDatabase();
		Util.DeleteDir(Paths.Root, true);
	}

	public VFileInfo? GetFileInfo(VFileId id)
	{
		var db = Database.GetVFileInfoByFileId(id.Id);
		return db != null ? DbVFileInfoToVFileInfo(db) : null;
	}

	public List<VFileInfo> GetVersions(VFileId id)
	{
		var infos = Database.GetVFileInfosByFileId(id.Id, Db.VFileInfoVersionQuery.Versions)
			.Select(DbVFileInfoToVFileInfo);

		return [.. infos];
	}

	public VFileDataInfo? GetDataInfo(VFileId id)
	{
		var data = Database.GetVFileDataInfoByFileId(id.Id);
		return data != null ? DbVFileDataInfoToVFileDataInfo(data) : null;
	}

	public VFileInfo? StoreVFile(
		VFilePath path,
		byte[] content,
		VFileStorageOptions? opts = null)
	{
		return StoreVFile(new StoreVFileRequest(path, content, opts));
	}

	public VFileInfo? StoreVFile(StoreVFileRequest request)
	{
		return StoreVFiles(request.AsList()).SingleOrDefault();
	}

	public List<VFileInfo> StoreVFiles(List<StoreVFileRequest> requests)
	{
		var state = new StoreVFilesState();
		var uniqueMap = new HashSet<string>();

		foreach (var request in requests)
		{
			var path = request.Path;
			if (string.IsNullOrEmpty(path.FileName))
			{
				Hooks.Error(new(VFileErrorCodes.InvalidParameter, "fileName must have value", nameof(StoreVFiles)));
				return [];
			}

			var vfileId = NewVFileId(NormalizeRelativePath(path), path.FileName, null);
			if (uniqueMap.Contains(vfileId.Id))
			{
				Hooks.Error(new(VFileErrorCodes.Duplicate, $"Duplicate file detected: {vfileId}", nameof(StoreVFiles)));
				return [];
			}
			uniqueMap.Add(vfileId.Id);

			var opts = request.Opts ?? DefaultStorageOptions;

			var bytes = opts.Compression == VFileCompression.Compress
				? Util.Compress(request.Content)
				: request.Content;

			var hash = Util.HashSHA256(bytes);
			var dbData = Database.GetVFileDataInfoByHash(hash);
			var now = DateTimeOffset.Now;

			state.NewVFileInfos.Add(
				new VFileInfo(
					vfileId,
					hash,
					request.Content.Length,
					now,
					opts.TTL.HasValue ? now + opts.TTL : null));

			if (dbData == null)
			{
				var data = new VFileDataInfo(
					hash,
					hash.Take(2).GetString(),
					hash,
					request.Content.Length,
					bytes.Length,
					now,
					opts.Compression);

				state.NewVFileDataInfo.Add(data);
				state.WriteFiles.Add((DataFilePath(data), bytes));
			}
		}

		// @TODO: transaction?
		foreach (var vfile in state.NewVFileInfos)
		{
			Database.SaveVFileInfo(vfile);
		}

		foreach (var data in state.NewVFileDataInfo)
		{
			Database.SaveVFileDataInfo(data);
		}

		foreach (var (path, bytes) in state.WriteFiles)
		{
			Util.WriteFile(path, bytes);
		}

		return state.NewVFileInfos;
	}

	private static string NormalizeRelativePath(VFilePath path)
	{
		char[] dividers = { '/', '\\' };
		var parts = path.Path?.Split(dividers, StringSplitOptions.RemoveEmptyEntries);
		var relativePath = PathDirectorySeparator.ToString();
		if (parts.AnySafe())
		{
			relativePath += string.Join(PathDirectorySeparator, parts);
			relativePath += PathDirectorySeparator;
		}
		return relativePath;
	}

	private string DataFilePath(VFileDataInfo info)
	{
		return Path.Combine(Paths.Store, info.Directory, info.FileName);
	}

	private static VFileId NewVFileId(string relativePath, string fileName, string? version)
	{
		var versionPart = version.HasValue()
			? $"?v={version}"
			: string.Empty;

		var id = $"{relativePath}{fileName}{versionPart}";

		return new VFileId(id, relativePath, fileName, version);
	}

	public static VFileId ParseVFileId(string id)
	{
		var idx = id.LastIndexOf(PathDirectorySeparator) + 1;
		var versionIdx = id.LastIndexOf("?v=");
		var relativePath = id[..idx];
		string? versionQueryString = versionIdx > 0 ? id[versionIdx..] : null;
		string? version = null;
		string fileName = id[idx..];
		if (versionQueryString != null)
		{
			version = versionQueryString.Skip(3).GetString();
			fileName = fileName.Replace(versionQueryString, string.Empty);
		}

		return new(id, relativePath, fileName, version);
	}

	private static VFileInfo DbVFileInfoToVFileInfo(Db.VFileInfo db)
	{
		return new VFileInfo(
			ParseVFileId(db.FileId),
			db.Hash,
			db.Size,
			db.CreationTime,
			db.DeleteAt);
	}

	private static VFileDataInfo DbVFileDataInfoToVFileDataInfo(Db.VFileDataInfo db)
	{
		return new VFileDataInfo(
			db.Hash,
			db.Directory,
			db.FileName,
			db.Size,
			db.SizeOnDisk,
			db.CreationTime,
			(VFileCompression)db.Compression);
	}
}
