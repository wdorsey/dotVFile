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
	/// Only a single application can access the VFile at once.
	/// This is good for writes to prevent data corruption from
	/// race-conditions or caching.
	/// </summary>
	SingleApplication,

	/// <summary>
	/// Any number of applications can simultaneously access the VFile instance.
	/// </summary>
	MultiApplication
}

public record VFilePermissions(
	VFilePermission Read,
	VFilePermission Write)
{
	public static VFilePermissions Default() =>
		new(VFilePermission.MultiApplication,
			VFilePermission.SingleApplication);
}

public record VFileOptions(
	string? Name,
	string Directory,
	IVFileHooks? Hooks = null,
	VFileStoreOptions? DefaultStoreOptions = null,
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
	public VFileStoreOptions DefaultStoreOptions { get; set; } =
		DefaultStoreOptions ?? VFileStoreOptions.Default();

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
			VFileStoreOptions.Default(),
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

public record VFileVersionOptions(
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

	public static VFileVersionOptions Default() =>
		new(VFileExistsBehavior.Overwrite, null, null);
}

public record VFileStoreOptions(
	VFileCompression Compression,
	TimeSpan? TTL,
	VFileVersionOptions VersionOpts)
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

	public VFileVersionOptions VersionOpts = VersionOpts;

	public VFileStoreOptions SetVersionOpts(VFileVersionOptions opts)
	{
		VersionOpts = opts;
		return this;
	}

	public static VFileStoreOptions Default() =>
		new(VFileCompression.None, null, VFileVersionOptions.Default());
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
	public List<VFileInfo> NewVFiles = [];
	public List<Db.VFile> UpdateVFiles = [];
	public List<Db.VFile> DeleteVFiles = [];
}

public record VFileCleanResult
{
	internal VFileCleanResult(
		Db.UnreferencedEntities unreferencedEntities,
		List<Db.VFile> deletedVFiles)
	{
		DeletedVFileCount = deletedVFiles.Count;
		DeletedFileContentCount = unreferencedEntities.FileContentRowIds.Count;
		DeletedDirectoryCount = unreferencedEntities.DirectoryRowIds.Count;
	}

	public long DeletedVFileCount;
	public long DeletedFileContentCount;
	public long DeletedDirectoryCount;
}

public record SystemInfo(
	Guid ApplicationId,
	string Version,
	DateTimeOffset? LastClean,
	DateTimeOffset LastUpdate);