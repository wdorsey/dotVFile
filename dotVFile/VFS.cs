namespace dotVFile;

public class VFS
{
	public const char PathDirectorySeparator = '/';
	public static VFileStorageOptions GetDefaultStorageOptions() =>
		new(VFileExistsBehavior.Overwrite, VFileCompression.None, null, null, null);

	public VFS(VFSOptions opts)
	{
		if (opts.VFileDirectory.IsEmpty() || !Path.IsPathFullyQualified(opts.VFileDirectory))
			throw new Exception($"Invalid VFileDirectory: \"{opts.VFileDirectory}\". VFileDirectory must be a valid directory where this VFS instance will store its file, such as: \"C:\\dotVFile\".");

		Name = opts.Name.HasValue() ? opts.Name : "dotVFile";
		VFileDirectory = Util.CreateDir(opts.VFileDirectory);
		Hooks = opts.Hooks ?? new NotImplementedHooks();
		Database = new VFileDatabase(new(Name, VFileDirectory, Hooks));
		DefaultStorageOptions = opts.DefaultStorageOptions ?? GetDefaultStorageOptions();
	}

	private VFileDatabase Database { get; }
	public string Name { get; }
	public string VFileDirectory { get; }
	public IVFileHooks Hooks { get; }
	public VFileStorageOptions DefaultStorageOptions { get; }

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

	public VFileInfo? GetVFileInfo(VFileId id)
	{
		var db = Database.GetVFileInfoByFileId(id.Id);
		return db != null ? DbVFileInfoToVFileInfo(db) : null;
	}

	public List<VFileInfo> GetVFileVersions(VFileId id)
	{
		var infos = Database.GetVFileInfosByFileId(id.Id, Db.VFileInfoVersionQuery.Versions)
			.Select(DbVFileInfoToVFileInfo);

		return [.. infos];
	}

	public VFileDataInfo? GetVFileDataInfo(VFileId id)
	{
		var data = Database.GetVFileDataInfoByFileId(id.Id);
		return data != null ? DbVFileDataInfoToVFileDataInfo(data) : null;
	}

	public byte[]? GetVFileContent(VFileId id)
	{
		var vfile = Database.GetVFileByFileId(id.Id);
		return vfile?.File;
	}

	public VFile? GetVFile(VFileId id)
	{
		var info = GetVFileInfo(id);
		var dataInfo = GetVFileDataInfo(id);
		var content = GetVFileContent(id);

		if (info == null || dataInfo == null || content == null)
			return null;

		return new(info, dataInfo, content);
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
				var dataInfo = new VFileDataInfo(
					hash,
					request.Content.Length,
					bytes.Length,
					now,
					opts.Compression);

				var data = new VFileData(dataInfo, bytes);

				state.NewVFileData.Add(data);
			}
		}

		// @TODO: transaction?
		Database.SaveVFileData(state.NewVFileData);
		Database.SaveVFileInfo(state.NewVFileInfos);

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
			db.Size,
			db.SizeOnDisk,
			db.CreationTime,
			(VFileCompression)db.Compression);
	}
}
