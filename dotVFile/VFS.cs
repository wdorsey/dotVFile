namespace dotVFile;

public class VFS
{
	private const char RelativePathDirectorySeparator = '/';
	private const string VersionDir = "__vfile_version__";

	public VFS(VFSOptions opts)
	{
		if (opts.RootPath.IsEmpty() || !Path.IsPathFullyQualified(opts.RootPath))
			throw new Exception($"Invalid RootPath: \"{opts.RootPath}\". RootPath must be a valid directory where this VFS instance will operate, such as: \"C:\\vfs\".");

		Name = new DirectoryInfo(opts.RootPath).Name;

		if (!Directory.Exists(opts.RootPath))
			Directory.CreateDirectory(opts.RootPath);
		RootPath = new(opts.RootPath);

		Hooks = opts.Hooks ?? new NotImplementedHooks();
		Database = new VFileDatabase(new(opts.RootPath, Hooks));
		DefaultStorageOptions = opts.DefaultStorageOptions ??
			new(VFileExistsBehavior.Overwrite, VFileCompression.None, null, null, null);
	}

	private VFileDatabase Database { get; }
	public string Name { get; }
	public string RootPath { get; }
	public IVFileHooks Hooks { get; }
	public VFileStorageOptions DefaultStorageOptions { get; }

	public VFileInfo? GetFileInfo(VFileId id)
	{
		var db = Database.GetVFileInfoByFileId(id.Id);
		return db != null ? DbVFileInfoToVFileInfo(db) : null;
	}

	public VFileInfo? StoreFile(
		VFileRelativePath? path,
		string fileName,
		byte[] contents)
	{
		return StoreFile(path, fileName, contents, DefaultStorageOptions);
	}

	public VFileInfo? StoreFile(
		VFileRelativePath? path,
		string fileName,
		byte[] contents,
		VFileStorageOptions opts)
	{
		if (string.IsNullOrEmpty(fileName))
		{
			Hooks.Error(new(VFileErrorCodes.InvalidParameter, "fileName must have value", nameof(StoreFile)));
			return null;
		}

		var vfileId = GenerateVFileId(path, fileName, null);
		var hash = DateTimeOffset.Now.Ticks.ToString();

		var vfile = new VFileInfo(
			vfileId,
			hash,
			1234,
			DateTimeOffset.Now,
			null);

		var data = new VFileDataInfo(
			hash,
			hash.Take(2).GetString(),
			hash,
			1234,
			1337,
			DateTimeOffset.Now,
			VFileCompression.None);

		var dbVFile = Database.SaveVFileInfo(vfile);
		var dbData = Database.SaveVFileDataInfo(data);
		Database.SaveVFileMap(hash, dbVFile.Id, dbData.Id);

		return vfile;
	}

	private static VFileId GenerateVFileId(VFileRelativePath? path, string fileName, string? version)
	{
		var relativePath = RelativePathDirectorySeparator.ToString();
		if (path != null)
		{
			relativePath += string.Join(RelativePathDirectorySeparator, path.Paths);
			relativePath += RelativePathDirectorySeparator;
		}

		var versionPart = version.HasValue()
			? $"?v={version}"
			: string.Empty;

		var id = $"{relativePath}{fileName}{versionPart}";

		return new VFileId(id, relativePath, fileName, version);
	}

	public static VFileId ParseVFileId(string id)
	{
		var idx = id.LastIndexOf(RelativePathDirectorySeparator) + 1;
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
}
