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

	internal VFileDatabase Database { get; }
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
		var vfiles = Database.GetVFiles([.. paths.Select(x => BuildVFileId(x).FilePath)], VFileInfoVersionQuery.Latest);
		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfos(List<VFileId> ids)
	{
		var vfiles = Database.GetVFiles([.. ids.Select(x => x.FilePath)], VFileInfoVersionQuery.Latest);
		return DbVFileToVFileInfo(vfiles);
	}

	public List<VFileInfo> GetVFileInfoVersions(VFilePath path, VFileInfoVersionQuery versionQuery)
	{
		return GetVFileInfoVersions(BuildVFileId(path), versionQuery);
	}

	public List<VFileInfo> GetVFileInfoVersions(VFileId id, VFileInfoVersionQuery versionQuery)
	{
		var vfiles = Database.GetVFiles(id.FilePath.AsList(), versionQuery);
		return DbVFileToVFileInfo(vfiles);
	}

	public VFile? GetVFile(VFilePath path)
	{
		return GetVFile(BuildVFileId(path));
	}

	public VFile? GetVFile(VFileId id)
	{
		var vfile = Database.GetLatestVFile(id.FilePath);
		if (vfile == null) return null;
		return GetVFiles(vfile.AsList()).SingleOrDefault();
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
		return GetVFiles(Database.GetVFiles([.. vfiles.Select(x => x.Id)]));
	}

	private List<VFile> GetVFiles(List<Db.VFile> dbVFiles)
	{
		Database.FetchContent([.. dbVFiles.Select(x => x.VFileContent)]);

		return [.. dbVFiles.Select(x =>
		{
			// @note: Content should never be null here, just null coalescing to make the compile happy.
			var content = x.VFileContent.Compression == (byte)VFileCompression.None
				? x.VFileContent.Content ?? Util.EmptyBytes()
				: Util.Decompress(x.VFileContent.Content ?? Util.EmptyBytes());

			return new VFile(DbVFileToVFileInfo(x), content);
		})];
	}

	public VFileInfo? StoreVFile(
		VFilePath path,
		VFileContent content,
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

			var content = request.Content.GetContent();
			var bytes = opts.Compression == VFileCompression.Compress
				? Util.Compress(content)
				: content;
			request.Content.Clear(); // free memory
			var hash = Util.HashSHA256(bytes);

			var existingVFile = Database.GetLatestVFile(vfileId.FilePath);
			var existingContent = existingVFile?.VFileContent;

			var vfileContent = existingContent != null && existingContent.Hash == hash
				? existingContent
				: Database.GetVFileContentByHash(hash);

			var newInfo = new VFileInfo
			{
				Id = Guid.NewGuid(),
				VFileId = vfileId,
				FilePath = vfileId.FilePath,
				Directory = vfileId.Directory,
				FileName = vfileId.FileName,
				FileExtension = Util.FileExtension(vfileId.FileName),
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
			}
			else
			{
				// previous VFileInfo already exists but content is different.
				var contentDifference = existingContent != null && existingContent.Hash != hash;
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
							var msg = $"Requested to overwrite existing file: {vfileId}";
							Hooks.Error(new(VFileErrorCodes.VersionBehaviorViolation, msg, nameof(StoreVFiles)));
							return [];
						}
						break;
					}

					case VFileVersionBehavior.Version:
					{
						var versions = Database.GetVFiles(vfileId.FilePath.AsList(), VFileInfoVersionQuery.Versions);

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
							// always updates version's DeleteAt to the current opts.VersionOpts.TTL that is passed in.
							// DeleteAt calculated off the Versioned timestamp.
							var expected = opts.VersionOpts.TTL.HasValue
								? v.VFileInfo.Versioned + opts.VersionOpts.TTL
								: null;
							if (v.VFileInfo.DeleteAt != expected)
							{
								v.VFileInfo.DeleteAt = expected;
								// sometimes this adds a dupe existingVFile, that's ok
								state.UpdateVFileInfos.Add(v.VFileInfo);
							}
						}

						var maxVersions = opts.VersionOpts.MaxVersionsRetained;
						if (maxVersions.HasValue && versions.Count > maxVersions)
						{
							var delete = versions.OrderByDescending(x => x.VFileInfo.Versioned).Skip(maxVersions.Value).ToList();
							state.DeleteVFileInfos.AddRange(delete.Select(x => x.VFileInfo));
						}
						break;
					}
				}
			}
		}

		Database.SaveStoreVFilesState(state);

		// @TODO: probably move this into the clean-up operation
		var rowIds = Database.GetUnreferencedVFileContentRowIds();
		Database.DeleteVFileContent(rowIds);

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
		var relativePath = NormalizeRelativePath(path.Directory);
		return BuildVFileId(relativePath, path.FileName, versioned);
	}

	private static VFileId BuildVFileId(Db.VFileInfo info)
	{
		return BuildVFileId(info.Directory, info.FileName, info.Versioned);
	}

	private static VFileId BuildVFileId(string relativePath, string fileName, DateTimeOffset? versioned = null)
	{
		var parts = GetRelativePathParts(relativePath);

		var filePath = $"{relativePath}{fileName}";

		var versionPart = versioned != null
			? $"?v={versioned.ToDefaultString()}"
			: string.Empty;

		var id = $"{filePath}{versionPart}";

		return new VFileId(id, relativePath, parts, fileName, filePath, versioned);
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
			VFileId = BuildVFileId(vfile.VFileInfo),
			FilePath = vfile.VFileInfo.FilePath,
			Directory = vfile.VFileInfo.Directory,
			FileName = vfile.VFileInfo.FileName,
			FileExtension = vfile.VFileInfo.FileExtension,
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
			Hooks.Error(new(VFileErrorCodes.InvalidParameter, "fileName must have value", context));
			return false;
		}

		return true;
	}
}
