using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace dotVFile;

public interface IVFileHooks
{
	void ErrorHandler(VFileError error);
	void Log(string msg);
}

public class NotImplementedHooks : IVFileHooks
{
	public void ErrorHandler(VFileError error)
	{
		// no impl
	}

	public void Log(string msg)
	{
		// no impl
	}
}

public static class VFileErrorCodes
{
	public const string DuplicateStoreVFileRequest = "DUPLICATE_STORE_VFILE_REQUEST";
	public const string DatabaseException = "DATABASE_EXCEPTION";
	public const string InvalidParameter = "INVALID_PARAMETER";
	public const string OverwriteNotAllowed = "OVERWRITE_NOT_ALLOWED";
}

public record VFileError(
	string ErrorCode,
	string Message,
	object? Data)
{
	public override string ToString()
	{
		return $@"
=== VFileError {ErrorCode} ===
{Message}
{Data.ToJson(true)}
==================
";
	}
}

public record VFSOptions(
	string? Name,
	string VFileDirectory,
	IVFileHooks? Hooks = null,
	VFileStoreOptions? DefaultStoreOptions = null);

public record VFileInfo
{
	public Guid Id;
	public VFilePath VFilePath = VFilePath.Default();
	public VFilePath UserVFilePath = VFilePath.Default();
	public DateTimeOffset? Versioned;
	public bool IsVersion => Versioned.HasValue;
	public DateTimeOffset? DeleteAt;
	public DateTimeOffset CreationTime;

	// Content fields
	public Guid ContentId;
	public string Hash = string.Empty;
	/// <summary>
	/// Size of VFile content.
	/// </summary>
	public long Size;
	/// <summary>
	/// Size of VFile content stored in database.
	/// This can be different than Size because of compression.
	/// </summary>
	public long SizeStored;
	public VFileCompression Compression;
	public DateTimeOffset ContentCreationTime;
}

public record VFile(VFileInfo VFileInfo, byte[] Content);

public record VFilePath
{
	public VFilePath(string? directory, string fileName)
		: this(directory, fileName, Path.Combine(directory ?? string.Empty, fileName)) { }

	public VFilePath(string filePath) : this(new FileInfo(filePath)) { }

	public VFilePath(FileInfo fi) : this(fi.DirectoryName, fi.Name, fi.FullName) { }

	internal VFilePath(
		string? directory,
		string fileName,
		string filePath)
	{
		Directory = directory;
		DirectoryParts = VFS.DirectoryParts(Directory);
		FileName = fileName;
		FileExtension = fileName.HasValue() ? Util.FileExtension(fileName) : string.Empty;
		FilePath = filePath;
	}

	internal static VFilePath Default() => new(string.Empty, string.Empty);

	public string? Directory { get; }
	public List<string> DirectoryParts = [];
	public string FileName { get; }
	public string FileExtension { get; }
	public string FilePath { get; }

	public override string ToString()
	{
		return FilePath;
	}
}

public record VFileContent
{
	public VFileContent(byte[] bytes)
	{
		Bytes = bytes;
	}

	public VFileContent(string filePath)
	{
		FilePath = filePath;
	}

	public VFileContent(Stream stream)
	{
		Stream = stream;
	}

	public byte[]? Bytes;
	public string? FilePath;
	public Stream? Stream;
}

public record StoreVFileRequest(
	VFilePath Path,
	VFileContent Content,
	VFileStoreOptions? Opts = null)
{
	public VFilePath Path = Path;
	[JsonIgnore]
	public VFileContent Content = Content;
	public VFileStoreOptions? Opts = Opts;
}

[JsonConverter(typeof(StringEnumConverter))]
public enum VFileVersionBehavior
{
	/// <summary>
	/// Old file deleted, no versioning.
	/// </summary>
	Overwrite,
	/// <summary>
	/// If file already exists, do not save new file and report error.
	/// </summary>
	Error,
	/// <summary>
	/// Version old file.
	/// </summary>
	Version
}

[JsonConverter(typeof(StringEnumConverter))]
public enum VFileCompression
{
	None = 0,
	/// <summary>
	/// Uses Deflate compression algorithm.
	/// </summary>
	Compress = 1
}

/// <summary>
/// Default: VFS.GetDefaultVersionOptions()
/// </summary>
public record VFileVersionOptions(
	VFileVersionBehavior Behavior,
	int? MaxVersionsRetained,
	TimeSpan? TTL)
{
	public VFileVersionBehavior Behavior = Behavior;
	public int? MaxVersionsRetained = MaxVersionsRetained;
	public TimeSpan? TTL = TTL;
}

/// <summary>
/// Default: VFS.GetDefaultStoreOptions()
/// </summary>
public record VFileStoreOptions(
	VFileCompression Compression,
	TimeSpan? TTL,
	VFileVersionOptions VersionOpts)
{
	public VFileCompression Compression = Compression;
	public TimeSpan? TTL = TTL;
	/// <summary>
	/// Default: VFS.GetDefaultVersionOptions()
	/// </summary>
	public VFileVersionOptions VersionOpts = VersionOpts;

	public VFileStoreOptions SetVersionOpts(VFileVersionOptions opts)
	{
		VersionOpts = opts;
		return this;
	}
}

[JsonConverter(typeof(StringEnumConverter))]
public enum VFileInfoVersionQuery
{
	Latest,
	Versions,
	Both
}

internal record StoreVFilesState
{
	public List<VFileInfo> NewVFileInfos = [];
	public List<Db.VFileInfo> UpdateVFileInfos = [];
	public List<Db.VFileInfo> DeleteVFileInfos = [];
}