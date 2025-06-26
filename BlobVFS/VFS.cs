namespace BlobVFS;

public class VFS
{
	public static readonly char DirectorySeparator = Path.DirectorySeparatorChar;
	public static VFileStorageOptions DefaultVFileStorageOptions =>
		new(VFileExistsBehavior.Overwrite, VFileCompression.None, null, null, null);

	public VFS(VFSOptions opts)
	{
		if (opts.RootPath.IsEmpty() || !Path.IsPathFullyQualified(opts.RootPath))
			throw new Exception($"Invalid RootPath: \"{opts.RootPath}\". RootPath must be a valid directory where this VFS instance will operate, such as: \"C:\\vfs\".");

		Name = new DirectoryInfo(opts.RootPath).Name;
		if (!Directory.Exists(opts.RootPath))
			Directory.CreateDirectory(opts.RootPath);
		RootPath = new(opts.RootPath);
		Callbacks = opts.Callbacks;
		Database = new VFSDatabase(new(opts.RootPath, opts.Callbacks));
	}

	public string Name { get; }
	public string RootPath { get; }
	public IVFSCallbacks? Callbacks { get; }
	private VFSDatabase Database { get; }

	public void Go()
	{
		Database.Go();
	}
}
