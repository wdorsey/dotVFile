namespace dotVFile;

public class VFS
{
	private const char RelativePathDirectorySeparator = '/';

	public static VFileStorageOptions DefaultVFileStorageOptions() =>
		new(VFileExistsBehavior.Overwrite, VFileCompression.None, null, null, null);

	public VFS(VFSOptions opts)
	{
		if (opts.RootPath.IsEmpty() || !Path.IsPathFullyQualified(opts.RootPath))
			throw new Exception($"Invalid RootPath: \"{opts.RootPath}\". RootPath must be a valid directory where this VFS instance will operate, such as: \"C:\\vfs\".");

		Name = new DirectoryInfo(opts.RootPath).Name;
		if (!Directory.Exists(opts.RootPath))
			Directory.CreateDirectory(opts.RootPath);
		RootPath = new(opts.RootPath);
		Hooks = opts.Hooks;
		Database = new VFileDatabase(new(opts.RootPath, opts.Hooks));
	}

	private VFileDatabase Database { get; }
	public string Name { get; }
	public string RootPath { get; }
	public IVFileHooks Hooks { get; }

	public void StoreFile(
		VFileRelativePath? path,
		string fileName,
		byte[] contents,
		VFileStorageOptions opts)
	{
		if (string.IsNullOrEmpty(fileName))
		{
			Hooks.Error(new("INVALID_PARAM", new Exception("fileName must have value")));
			return;
		}

		var vfile = new VFileInfo(
			GenerateVFileId(path, fileName, null),
			DateTimeOffset.Now.Ticks.ToString(),
			DateTimeOffset.Now,
			1234,
			null);

		Hooks.Log(vfile.Id.ToString());

		Database.SaveVFileInfo(vfile);
	}

	private static VFileId GenerateVFileId(VFileRelativePath? path, string fileName, string? version)
	{
		string relativePath = RelativePathDirectorySeparator.ToString();
		if (path != null)
		{
			relativePath += string.Join(RelativePathDirectorySeparator, path.Paths);
			relativePath += RelativePathDirectorySeparator;
		}

		if (version.HasValue())
		{
			(var name, var ext) = Util.FileNameAndExtension(fileName);
			fileName = $"{name}.{version}{ext}";
		}

		var id = $"{relativePath}{fileName}";

		return new VFileId(id, relativePath, fileName, version);
	}
}
