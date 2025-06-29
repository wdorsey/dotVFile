namespace dotVFile;

public class VFS
{
	public const char PathDirectorySeparator = '/';

	public static VFileStorageOptions GetDefaultStorageOptions() =>
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
		var data = Database.GetVFileDataInfoByFileId(id.Id);

		if (data == null)
			return null;

		var vfile = Database.GetVFile(data.RowId) ?? throw new Exception("VFileDataInfo exists, but no VFile found.");

		var bytes = data.Compression == (byte)VFileCompression.None
			? vfile.File
			: Util.Decompress(vfile.File);

		return bytes;
	}

	public VFile? GetVFile(VFilePath path)
	{
		if (!Assert_ValidFileName(path.FileName, nameof(GetVFile)))
			return null;

		return GetVFile(BuildVFileId(path, null));
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
			if (!Assert_ValidFileName(path.FileName, nameof(StoreVFiles)))
				return [];

			var vfileId = BuildVFileId(path, null);

			if (uniqueMap.Contains(vfileId.Id))
			{
				Hooks.Error(new(VFileErrorCodes.Duplicate, $"Duplicate file detected: {vfileId}", nameof(StoreVFiles)));
				return [];
			}

			uniqueMap.Add(vfileId.Id);

			var now = DateTimeOffset.Now;
			var opts = request.Opts ?? DefaultStorageOptions;

			var bytes = opts.Compression == VFileCompression.Compress
				? Util.Compress(request.Content)
				: request.Content;
			var hash = Util.HashSHA256(bytes);

			var existingInfo = GetVFileInfo(vfileId);
			var newInfo = new VFileInfo(
				vfileId,
				hash,
				now,
				opts.TTL.HasValue ? now + opts.TTL : null);

			if (existingInfo == null)
			{
				state.NewVFileInfos.Add(newInfo);
			}
			else
			{
				var versions = GetVFileVersions(vfileId);
				if (existingInfo.Hash != hash)
				{
					switch (opts.VersionOpts.Behavior)
					{
						case VFileVersionBehavior.Overwrite:
							{
								state.DeleteVFileInfos.Add(existingInfo);
								state.NewVFileInfos.Add(newInfo);
								break;
							}
						case VFileVersionBehavior.Error:
							{
								var msg = $"Requested to overwrite existing file: {vfileId}";
								Hooks.Error(new(VFileErrorCodes.VersionBehaviorViolation, msg, nameof(StoreVFiles)));
								return [];
							}
						case VFileVersionBehavior.Version:
							{
								existingInfo.Versioned = now;
								existingInfo.DeleteAt = opts.VersionOpts.VersionTTL.HasValue
									? now + opts.VersionOpts.VersionTTL
									: null;
								versions.Add(existingInfo);
								state.UpdateVFileInfos.Add(existingInfo);
								state.NewVFileInfos.Add(newInfo);
								break;
							}
					}
				}

				int? maxVersions = opts.VersionOpts.MaxVersionsRetained;
				if (maxVersions.HasValue && versions.Count > maxVersions)
				{
					var delete = versions.OrderByDescending(x => x.Versioned).Skip(maxVersions.Value).ToList();
					state.DeleteVFileInfos.AddRange(delete);
				}
			}

			if (Database.GetVFileDataInfoByHash(hash) == null)
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

		// @TODO: check DeleteVFileInfos to see if data can be added to DeleteVFileData
		// write sql query to get count of infos with DeleteVFileInfos.Hash
		// if this proves too slow, remove it

		// @TODO: transaction?
		Database.SaveVFileData(state.NewVFileData);
		Database.SaveVFileInfo(state.NewVFileInfos);

		return state.NewVFileInfos;
	}

	private static string NormalizeRelativePath(string? path)
	{
		char[] dividers = { '/', '\\' };
		var parts = path?.Split(dividers, StringSplitOptions.RemoveEmptyEntries);
		var relativePath = PathDirectorySeparator.ToString();
		if (parts.AnySafe())
		{
			relativePath += string.Join(PathDirectorySeparator, parts);
			relativePath += PathDirectorySeparator;
		}
		return relativePath;
	}

	private static List<string> GetRelativePathParts(string relativePath)
	{
		// @note: this explicitly works only with a NormalizeRelativePath
		return [.. relativePath.Split(PathDirectorySeparator, StringSplitOptions.RemoveEmptyEntries)];
	}

	private static VFileId BuildVFileId(VFilePath path, DateTimeOffset? versioned)
	{
		var relativePath = NormalizeRelativePath(path.Path);
		var parts = GetRelativePathParts(relativePath);

		var versionPart = versioned != null
			? $"?v={versioned.ToDefaultString()}"
			: string.Empty;

		var id = $"{relativePath}{path.FileName}{versionPart}";

		return new VFileId(id, relativePath, parts, path.FileName, versioned);
	}

	private static VFileId ParseVFileId(string id)
	{
		var idx = id.LastIndexOf(PathDirectorySeparator) + 1;
		var versionIdx = id.LastIndexOf("?v=");
		var relativePath = id[..idx];
		var parts = GetRelativePathParts(relativePath);
		string? versionQueryString = versionIdx > 0 ? id[versionIdx..] : null;
		DateTimeOffset? versioned = null;
		string fileName = id[idx..];
		if (versionQueryString != null)
		{
			versioned = DateTimeOffset.Parse(versionQueryString.Skip(3).GetString());
			fileName = fileName.Replace(versionQueryString, string.Empty);
		}

		return new(id, relativePath, parts, fileName, versioned);
	}

	private static VFileInfo DbVFileInfoToVFileInfo(Db.VFileInfo db)
	{
		return new VFileInfo(
			ParseVFileId(db.FileId),
			db.Hash,
			db.CreationTime,
			db.DeleteAt);
	}

	private static VFileDataInfo DbVFileDataInfoToVFileDataInfo(Db.VFileDataInfo db)
	{
		return new VFileDataInfo(
			db.Hash,
			db.Size,
			db.SizeStored,
			db.CreationTime,
			(VFileCompression)db.Compression);
	}

	private bool Assert_ValidFileName(string fileName, string context)
	{
		if (fileName.IsEmpty())
		{
			Hooks.Error(new(VFileErrorCodes.InvalidParameter, "fileName must have value", context));
			return false;
		}

		return true;
	}
}
