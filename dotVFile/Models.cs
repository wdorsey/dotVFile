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
	string RelativePath,
	List<string> RelativePathParts,
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
	public string RelativePath = string.Empty;
	public string FileName = string.Empty;
	public string FileExtension = string.Empty;
	public DateTimeOffset? Versioned;
	public bool IsVersion => Versioned.HasValue;
	public long? TTL;
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
	/// <summary>
	/// path is only the path part and should not include the FileName. To use a full file path, use VFilePath.FromFilePath.
	/// </summary>
	/// <param name="path">Only the path part, should not include FileName. To use a full file path, use VFilePath.FromFilePath.</param>
	public VFilePath(string? path, string fileName)
	{
		Path = path;
		FileName = fileName;
	}

	public static VFilePath FromFilePath(string filePath)
	{
		var fi = new FileInfo(filePath);

		return new VFilePath(
			fi.DirectoryName ?? string.Empty,
			fi.Name);
	}

	public static VFilePath FromPathParts(string fileName, params string[] pathParts)
	{
		var path = string.Join(VFS.PathDirectorySeparator, pathParts);
		return new VFilePath(path, fileName);
	}

	/// <summary>
	/// ONLY the path part, should not include FileName.
	/// </summary>
	public string? Path { get; }
	public string FileName { get; }

	public override string ToString()
	{
		return $"{Path}{VFS.PathDirectorySeparator}{FileName}";
	}
}

public record StoreVFileRequest(
	VFilePath Path,
	byte[] Content,
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
	public List<(VFileInfo Info, byte[] Content)> NewVFileContents = [];
}