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

	public VFileInfo? GetVFileInfo(VFilePath path)
	{
		return GetVFileInfoVersions(path, VFileInfoVersionQuery.Latest).SingleOrDefault();
	}

	public VFileInfo? GetVFileInfo(VFileId id)
	{
		return GetVFileInfoVersions(id, VFileInfoVersionQuery.Latest).SingleOrDefault();
	}

	public List<VFileInfo> GetVFileInfos(List<VFilePath> paths)
	{
		var vfiles = Database.GetVFiles([.. paths.Select(x => BuildVFileId(x).Id)], VFileInfoVersionQuery.Latest);
		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfos(List<VFileId> ids)
	{
		var vfiles = Database.GetVFiles([.. ids.Select(x => x.Id)], VFileInfoVersionQuery.Latest);
		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfoVersions(VFilePath path, VFileInfoVersionQuery versionQuery)
	{
		return GetVFileInfoVersions(BuildVFileId(path), versionQuery);
	}

	public List<VFileInfo> GetVFileInfoVersions(VFileId id, VFileInfoVersionQuery versionQuery)
	{
		var vfiles = Database.GetVFiles(id.Id.AsList(), versionQuery);
		return DbVFileToVFileInfo(vfiles);
	}

	public VFile? GetVFile(VFilePath path)
	{
		var info = GetVFileInfo(path);
		if (info == null) return null;
		return GetVFiles(info.AsList()).SingleOrDefault();
	}

	public VFile? GetVFile(VFileId id)
	{
		var info = GetVFileInfo(id);
		if (info == null) return null;
		return GetVFiles(info.AsList()).SingleOrDefault();
	}

	public List<VFile> GetVFiles(List<VFilePath> paths)
	{
		return GetVFiles(GetVFileInfos(paths));
	}

	public List<VFile> GetVFiles(List<VFileId> ids)
	{
		return GetVFiles(GetVFileInfos(ids));
	}

	public List<VFile> GetVFiles(List<VFileInfo> vfiles)
	{
		var map = vfiles.ToDictionary(x => x.Id, x => x);

		var dbVFiles = Database.GetVFiles([.. vfiles.Select(x => x.Id)]);
		Database.FetchContent([.. dbVFiles.Select(x => x.VFileContent)]);

		return [.. dbVFiles.Select(x =>
		{
			// @note: Content should never be null here, just null coalescing to make the compile happy.
			var content = x.VFileContent.Compression == (byte)VFileCompression.None
				? x.VFileContent.Content ?? Util.EmptyBytes()
				: Util.Decompress(x.VFileContent.Content ?? Util.EmptyBytes());

			return new VFile(map[x.VFileInfo.Id], content);
		})];
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

			var existingVFile = Database.GetVFiles(vfileId.Id.AsList(), VFileInfoVersionQuery.Latest).SingleOrDefault();
			var existingContent = existingVFile?.VFileContent ?? Database.GetVFileContentByHash(hash);

			var newInfo = new VFileInfo
			{
				Id = Guid.NewGuid(),
				VFileId = vfileId,
				FullPath = vfileId.Id,
				RelativePath = vfileId.RelativePath,
				FileName = vfileId.FileName,
				FileExtension = Util.FileExtension(vfileId.FileName),
				DeleteAt = opts.TTL.HasValue ? now + opts.TTL : null,
				CreationTime = now,
				ContentId = Guid.NewGuid(),
				Hash = hash,
				Size = request.Content.Length,
				SizeStored = bytes.Length,
				Compression = opts.Compression,
				ContentCreationTime = now
			};

			if (existingVFile == null)
			{
				state.NewVFileInfos.Add(newInfo);
			}
			else
			{
				var versions = Database.GetVFiles(vfileId.Id.AsList(), VFileInfoVersionQuery.Versions);

				// existingContent will never be null here because existingInfo
				// can only be not null if it also has content.
				if (existingContent!.Hash != hash)
				{
					switch (opts.VersionOpts.Behavior)
					{
						case VFileVersionBehavior.Overwrite:
							{
								state.DeleteVFileInfos.Add(existingVFile.VFileInfo);
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
								// rebuilding FileId will just add the Versioned field,
								// so we don't need to update any other corresponding VFileId fields.
								existingVFile.VFileInfo.FileId = BuildVFileId(path, now).Id;
								existingVFile.VFileInfo.Versioned = now;
								existingVFile.VFileInfo.DeleteAt = opts.VersionOpts.VersionTTL.HasValue
									? now + opts.VersionOpts.VersionTTL
									: null;
								versions.Add(existingVFile);
								state.UpdateVFileInfos.Add(existingVFile.VFileInfo);
								state.NewVFileInfos.Add(newInfo);
								break;
							}
					}
				}

				int? maxVersions = opts.VersionOpts.MaxVersionsRetained;
				if (maxVersions.HasValue && versions.Count > maxVersions)
				{
					var delete = versions.OrderByDescending(x => x.VFileInfo.Versioned).Skip(maxVersions.Value).ToList();
					state.DeleteVFileInfos.AddRange(delete.Select(x => x.VFileInfo));
				}
			}

			if (existingContent == null)
			{
				state.NewVFileContents.Add((newInfo, bytes));
			}
		}

		// @TODO: check DeleteVFileInfos to see if data can be added to DeleteVFileData
		// write sql query to get count of infos with DeleteVFileInfos.Hash
		// if this proves too slow, remove it

		Hooks.Log(new { state.NewVFileInfos, state.UpdateVFileInfos, state.DeleteVFileInfos, NewVFileContentsCount = state.NewVFileContents.Count, state.DeleteVFileContents }.ToJson(true)!);

		Database.SaveStoreVFilesState(state);

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

	private static VFileId BuildVFileId(VFilePath path, DateTimeOffset? versioned = null)
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

	private static List<VFileInfo> DbVFileToVFileInfo(List<Db.VFile> vfiles)
	{
		return [.. vfiles.Select(x =>
			new VFileInfo
			{
				Id = x.VFileInfo.Id,
				VFileId = ParseVFileId(x.VFileInfo.FileId),
				FullPath = x.VFileInfo.FileId,
				RelativePath = x.VFileInfo.RelativePath,
				FileName = x.VFileInfo.FileName,
				FileExtension = x.VFileInfo.FileExtension,
				Versioned = x.VFileInfo.Versioned,
				DeleteAt = x.VFileInfo.DeleteAt,
				CreationTime = x.VFileInfo.CreationTime,
				ContentId = x.VFileContent.Id,
				Hash = x.VFileContent.Hash,
				Size = x.VFileContent.Size,
				SizeStored = x.VFileContent.SizeStored,
				Compression = (VFileCompression)x.VFileContent.Compression,
				ContentCreationTime = x.VFileContent.CreationTime
			})];
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
