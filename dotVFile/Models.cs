using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace dotVFile;

public interface IVFileHooks
{
	void ErrorHandler(VFileError error);
	void DebugLog(string msg);
}

public class NotImplementedVFileHooks : IVFileHooks
{
	public void ErrorHandler(VFileError error)
	{
		// no impl
	}

	public void DebugLog(string msg)
	{
		// no impl
	}
}

public static class VFileErrorCodes
{
	public const string DuplicateStoreVFileRequest = "DUPLICATE_STORE_VFILE_REQUEST";
	public const string InvalidParameter = "INVALID_PARAMETER";
	public const string NotFound = "NOT_FOUND";
	public const string OverwriteNotAllowed = "OVERWRITE_NOT_ALLOWED";
	public const string MultipleApplicationInstances = "MULTIPLE_APPLICATION_INSTANCES";
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

public enum VFilePermission
{
	/// <summary>
	/// Only a single application instance can 
	/// access the VFile system at once.
	/// VFile is not thread-safe.
	/// </summary>
	SingleApplication,

	/// <summary>
	/// No restrictions.
	/// </summary>
	All
}

public record VFilePermissions(
	VFilePermission Read,
	VFilePermission Write)
{
	public static VFilePermissions Default() =>
		new(VFilePermission.All,
			VFilePermission.SingleApplication);
}

public record VFileOptions(
	string? Name,
	string Directory,
	IVFileHooks? Hooks = null,
	StoreOptions? DefaultStoreOptions = null,
	VFilePermissions? Permissions = null,
	bool Debug = false)
{
	/// <summary>
	/// Name of the VFile instance
	/// </summary>
	public string? Name { get; set; } = Name;

	/// <summary>
	/// Directory to store VFile's single-file
	/// </summary>
	public string Directory { get; set; } = Directory;

	/// <summary>
	/// User's IVFileHooks implementation (pass null to ignore).
	/// Hooks allow you to handle errors and hook into debug logging.
	/// </summary>
	public IVFileHooks Hooks { get; set; } =
		Hooks ?? new NotImplementedVFileHooks();

	/// <summary>
	/// Default Store options
	/// null will use VFileSystem.GetDefaultStoreOptions()
	/// </summary>
	public StoreOptions DefaultStoreOptions { get; set; } =
		DefaultStoreOptions ?? StoreOptions.Default();

	/// <summary>
	/// Read/Write restrictions for multiple 
	/// </summary>
	public VFilePermissions Permissions { get; set; } =
		Permissions ?? VFilePermissions.Default();

	/// <summary>
	/// Debug flag enables Hooks.DebugLog. 
	/// This makes Hooks.DebugLog _very_ verbose.
	/// </summary>
	public bool Debug { get; set; } = Debug;

	public static VFileOptions Default() =>
		new(null,
			Environment.CurrentDirectory,
			new NotImplementedVFileHooks(),
			StoreOptions.Default(),
			VFilePermissions.Default(),
			false);
}

public record VFileInfo
{
	internal VFileInfo() { }
	internal VFileInfo(Db.VFileModel vfile)
	{
		Id = vfile.VFile.Id;
		VFilePath = new(vfile.Directory.Path, vfile.VFile.FileName);
		Versioned = vfile.VFile.Versioned;
		DeleteAt = vfile.VFile.DeleteAt;
		CreationTime = vfile.VFile.CreateTimestamp;
		ContentId = vfile.FileContent.Id;
		Hash = vfile.FileContent.Hash;
		Size = vfile.FileContent.Size;
		SizeStored = vfile.FileContent.SizeContent;
		Compression = (VFileCompression)vfile.FileContent.Compression;
		ContentCreationTime = vfile.FileContent.CreateTimestamp;
	}

	public Guid Id { get; internal set; }
	public VFilePath VFilePath { get; internal set; } = VFilePath.Default();
	public string FileName => VFilePath.FileName;
	public string FilePath => VFilePath.FilePath;
	public VDirectory VDirectory => VFilePath.Directory;
	public string DirectoryName => VDirectory.Name;
	public DateTimeOffset? Versioned { get; internal set; }
	public bool IsVersion => Versioned.HasValue;
	public DateTimeOffset? DeleteAt { get; internal set; }
	public DateTimeOffset CreationTime { get; internal set; }

	// Content fields
	public Guid ContentId { get; internal set; }
	public string Hash { get; internal set; } = string.Empty;
	/// <summary>
	/// Size of VFile content.
	/// </summary>
	public long Size { get; internal set; }
	/// <summary>
	/// Size of VFile content stored in database.
	/// This can be different than Size because of Compression.
	/// </summary>
	public long SizeStored { get; internal set; }
	public VFileCompression Compression { get; internal set; }
	public DateTimeOffset ContentCreationTime { get; internal set; }
}

public record VDirectoryInfo
{
	public Guid Id;
	/// <summary>
	/// Name of the directory.
	/// </summary>
	public string Name = string.Empty;
	/// <summary>
	/// Full path of the directory.
	/// </summary>
	public string Path = string.Empty;
	public List<string> PathParts = [];
	public VDirectoryInfo? Parent;
	public VDirectoryInfo? Root;
	public DateTimeOffset CreationTime;
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

public record StoreRequest(
	VFilePath Path,
	VFileContent Content,
	StoreOptions? Opts = null)
{
	public VFilePath Path = Path;
	[JsonIgnore]
	public VFileContent Content = Content;
	public StoreOptions? Opts = Opts;
}

public record CopyRequest(
	VFilePath From,
	VFilePath To)
{
	public CopyRequest(
		VFileInfo from,
		VFilePath to)
		: this(from.VFilePath, to) { }
}

public record MoveResult(
	List<VFileInfo> NewVFileInfos,
	List<VFileInfo> DeletedVFileInfos);

[JsonConverter(typeof(StringEnumConverter))]
public enum VFileExistsBehavior
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

public record VersionOptions(
	VFileExistsBehavior ExistsBehavior,
	int? MaxVersionsRetained,
	TimeSpan? TTL)
{
	/// <summary>
	/// Determines what happens when a vfile is requested to be Stored but already exists.
	/// Options are Overwrite, Error, Version
	/// Default is Overwrite.
	/// </summary>
	public VFileExistsBehavior ExistsBehavior = ExistsBehavior;

	/// <summary>
	/// Max number of versions to keep. 
	/// Default is null (unlimited).
	/// </summary>
	public int? MaxVersionsRetained = MaxVersionsRetained;

	/// <summary>
	/// Time-to-live for versioned vfiles. 
	/// Default is null (no TTL).
	/// </summary>
	public TimeSpan? TTL = TTL;

	public static VersionOptions Default() =>
		new(VFileExistsBehavior.Overwrite, null, null);
}

public record StoreOptions(
	VFileCompression Compression,
	TimeSpan? TTL,
	VersionOptions VersionOpts)
{
	/// <summary>
	/// Compress the file or not before storing.
	/// No compression is much faster, but compressing saves disk space.
	/// Default is None
	/// </summary>
	public VFileCompression Compression = Compression;

	/// <summary>
	/// Time-to-live for vfiles. default is null (no TTL)
	/// </summary>
	public TimeSpan? TTL = TTL;

	public VersionOptions VersionOpts = VersionOpts;

	public StoreOptions SetVersionOpts(VersionOptions opts)
	{
		VersionOpts = opts;
		return this;
	}

	public static StoreOptions Default() =>
		new(VFileCompression.None, null, VersionOptions.Default());
}

[JsonConverter(typeof(StringEnumConverter))]
public enum VersionQuery
{
	Latest = 0,
	Versions = 1,
	Both = 2
}

internal record StoreState
{
	public List<VFileInfo> NewVFiles = [];
	public List<Db.VFile> UpdateVFiles = [];
	public List<Db.VFile> DeleteVFiles = [];
}

public record CleanResult
{
	internal CleanResult(
		Db.UnreferencedFileContent unreferencedFileContent,
		List<Db.VFile> deletedVFiles)
	{
		DeletedVFileCount = deletedVFiles.Count;
		DeletedFileContentCount = unreferencedFileContent.FileContentRowIds.Count;
	}

	public long DeletedVFileCount;
	public long DeletedFileContentCount;
}

public record SystemInfo(
	Guid ApplicationId,
	string Version,
	DateTimeOffset? LastClean,
	DateTimeOffset LastUpdate);