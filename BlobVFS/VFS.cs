namespace BlobVFS;

public class VFS
{
	public static readonly char DirectorySeparator = Path.DirectorySeparatorChar;
	public static VFileStorageOptions DefaultVFileStorageOptions =>
		new(VFileExistsBehavior.Overwrite, VFileCompression.None, null, null, null);

	public VFS(VFSOptions opts)
	{
		if (opts.RootPath.IsEmpty() || !Path.IsPathFullyQualified(opts.RootPath))
			throw new Exception($"Invalid RootPath: \"{opts.RootPath}\". It needs to be a valid directory path where this VFS instance will operate, such as: \"C:\\vfs\".");

		Name = new DirectoryInfo(opts.RootPath).Name;
		Paths = new(opts.RootPath);
		ErrorHandler = opts.ErrorHandler;
		LogFn = opts.LogFn;
	}

	public string Name { get; private set; }
	public VFSPaths Paths { get; private set; }
	public Action<VFSError> ErrorHandler { get; private set; }
	public Action<string> LogFn { get; private set; }
}
