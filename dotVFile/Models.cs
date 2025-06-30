using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace dotVFile;

public interface IVFileHooks
{
	void Error(VFileError error);
	void Log(string msg);
}

public class NotImplementedHooks : IVFileHooks
{
	public void Error(VFileError error)
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
	public const string Duplicate = "DUPLICATE";
	public const string InvalidParameter = "INVALID_PARAMETER";
	public const string SqliteException = "SQLITE_EXCEPTION";
	public const string VersionBehaviorViolation = "VERSION_BEHAVIOR_VIOLATION";
}

public record VFileError(
	string ErrorCode,
	string Message,
	string Context)
{
	public override string ToString()
	{
		return $@"
=== VFileError {ErrorCode} ===
{Context}
{Message}
==================
";
	}
}

public record VFSOptions(
	string? Name,
	string VFileDirectory,
	IVFileHooks? Hooks = null,
	VFileStorageOptions? DefaultStorageOptions = null);

/// <summary>
/// Uniquely identifies a VFile.<br/>
/// Examples of Id:<br/>
///	/file.txt<br/>
///	/folder/subfolder/file.txt<br/>
///	/folder/file.txt?v={Version}
/// </summary>
public record VFileId(
	string Id,
	string Directory,
	List<string> DirectoryParts,
	string FileName,
	string FilePath,
	DateTimeOffset? Versioned)
{
	public override string ToString()
	{
		return Id;
	}

	public static VFileId Default() =>
		new(string.Empty, string.Empty, [], string.Empty, string.Empty, null);
}

public record VFileInfo
{
	public Guid Id;
	public VFileId VFileId = VFileId.Default();
	public string FilePath = string.Empty;
	public string Directory = string.Empty;
	public string FileName = string.Empty;
	public string FileExtension = string.Empty;
	public DateTimeOffset? Versioned;
	public bool IsVersion => Versioned.HasValue;
	public DateTimeOffset? DeleteAt;
	public DateTimeOffset CreationTime;

	// Content fields
	public Guid ContentId;
	public string Hash = string.Empty;
	public long Size;
	public long SizeStored;
	public VFileCompression Compression;
	public DateTimeOffset ContentCreationTime;
}

public record VFile(VFileInfo VFileInfo, byte[] Content);

public record VFilePath
{
	public VFilePath(string? directory, string fileName)
	{
		Directory = directory;
		FileName = fileName;
	}
	public VFilePath(string filePath) : this(new FileInfo(filePath)) { }
	public VFilePath(FileInfo fi) : this(fi.DirectoryName ?? string.Empty, fi.Name) { }
	/// <summary>
	/// PathParts should not include any directory separators.
	/// </summary>
	/// <param name="directories">Individual directories, in order. Should not include any directory separators.</param>
	public VFilePath(string fileName, params string[] directories)
		: this(string.Join(VFS.PathDirectorySeparator, directories), fileName) { }

	public string? Directory { get; }
	public string FileName { get; }

	public override string ToString()
	{
		var dir = Directory?.TrimEnd('/').TrimEnd('\\');
		return $"{dir}{VFS.PathDirectorySeparator}{FileName}";
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
	VFileStorageOptions? Opts = null);

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
/// Default: VFS.GetDefaultStorageOptions()
/// </summary>
public record VFileStorageOptions(
	VFileCompression Compression,
	TimeSpan? TTL,
	VFileVersionOptions VersionOpts)
{
	public VFileCompression Compression = Compression;
	public TimeSpan? TTL = TTL;
	/// <summary>
	/// Version options are only applied if the file already
	/// exists and it is different from the new file.
	/// Default: VFS.GetDefaultVersionOptions()
	/// </summary>
	public VFileVersionOptions VersionOpts = VersionOpts;
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