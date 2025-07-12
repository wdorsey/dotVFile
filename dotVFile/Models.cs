using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace dotVFile;

public static class VFileErrorCodes
{
	public const string DuplicateRequest = "DUPLICATE_REQUEST";
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

public record VFileOptions(
	string? Name,
	string Directory,
	Action<VFileError>? ErrorHandler = null,
	StoreOptions? DefaultStoreOptions = null)
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
	/// User's ErrorHandler for common, known error states.
	/// The return values of functions still indicate success/error but
	/// without any details.
	/// Pass null to ignore. 
	/// </summary>
	public Action<VFileError> ErrorHandler { get; set; } =
		ErrorHandler ?? VFile.NotImplErrorHandler;

	/// <summary>
	/// Default Store options
	/// null will use VFileSystem.GetDefaultStoreOptions()
	/// </summary>
	public StoreOptions DefaultStoreOptions { get; set; } =
		DefaultStoreOptions ?? StoreOptions.Default();

	public static VFileOptions Default() =>
		new(null,
			Environment.CurrentDirectory,
			VFile.NotImplErrorHandler,
			StoreOptions.Default());
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
	public string DirectoryPath => VDirectory.Path;
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

public record VFileStats(
	long DatabaseFileSize,
	FileStats VFiles,
	FileStats Versions,
	FileStats Content,
	int DirectoryCount)
{
	public string DatabaseFileSizeString => Util.SizeString(DatabaseFileSize);
}

public record DirectoryStats(
	VDirectory Directory,
	FileStats VFiles,
	FileStats Versions,
	FileStats TotalVFiles,
	FileStats TotalVersions,
	List<VDirectory> Directories)
{
	public int DirectoryCount => Directories.Count;
}

public record FileStats(
	int Count,
	long Size,
	long SizeStored)
{
	public string SizeString => Util.SizeString(Size);
	public string SizeStoredString => Util.SizeString(SizeStored);
}

public record StoreRequest(
	VFilePath Path,
	VFileContent Content,
	StoreOptions? Opts = null)
{
	public VFilePath Path { get; set; } = Path;
	[JsonIgnore]
	public VFileContent Content { get; set; } = Content;
	public StoreOptions? Opts { get; set; } = Opts;

	/// <summary>
	/// Used internally for Copy functionality.
	/// Huge performance gain as it allows copying
	/// to never have to touch the actual content.
	/// </summary>
	internal string? CopyHash { get; set; }
}

public record StoreResult(
	List<VFileInfo> VFiles,
	List<StoreRequest> Errors);

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

public record CacheRequest(
	byte[] CacheKey,
	VFilePath Path,
	Func<VFileContent> ContentFn,
	StoreOptions? StoreOptions = null)
{
	public byte[] CacheKey { get; set; } = CacheKey;
	public VFilePath Path { get; set; } = Path;
	public Func<VFileContent> ContentFn { get; set; } = ContentFn;
	public StoreOptions? StoreOptions { get; set; } = StoreOptions;
}

public record CacheResult(CacheRequest CacheRequest)
{
	public VFileInfo? VFileInfo { get; internal set; }
	public bool ErrorOccurred => VFileInfo == null;
	public byte[]? Bytes { get; internal set; }
	public bool CacheHit = false;
}

internal record CacheRecord(string Hash);

internal record CacheRequestState(
	int Index,
	CacheRequest CacheRequest)
{
	public string Id => CacheRequest.Path.FilePath;
	public string? Hash;
	public VFilePath? CachePath;
	public CacheResult Result = new(CacheRequest);
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