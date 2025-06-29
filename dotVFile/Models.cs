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
	DateTimeOffset? Versioned)
{
	public override string ToString()
	{
		return Id;
	}
}

public record VFile(
	VFileInfo FileInfo,
	byte[] Content);

public record VFileInfo(
	Guid Id,
	VFileId VFileId,
	string Hash,
	DateTimeOffset CreationTime,
	DateTimeOffset? DeleteAt,
	Guid ContentId,
	int Size,
	int SizeStored,
	VFileCompression Compression,
	DateTimeOffset ContentCreationTime)
{
	public Guid Id { get; } = Id;
	public VFileId VFileId { get; } = VFileId;
	public string FullPath { get; } = VFileId.Id;
	public string RelativePath { get; } = VFileId.RelativePath;
	public string Name { get; } = VFileId.FileName;
	public DateTimeOffset? Versioned { get; set; } = VFileId.Versioned;
	public bool IsVersion => VFileId.Versioned != null;
	public string Extension { get; } = Util.FileExtension(VFileId.FileName);
	public string Hash { get; } = Hash;
	public DateTimeOffset? DeleteAt { get; set; } = DeleteAt;
	public DateTimeOffset CreationTime { get; } = CreationTime;

	// Content fields
	public Guid ContentId { get; } = ContentId;
	public int Size { get; } = Size;
	public int SizeStored { get; } = SizeStored;
	public VFileCompression Compression { get; } = Compression;
	public DateTimeOffset ContentCreationTime { get; } = ContentCreationTime;
}

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

public record VFileVersionOptions(
	VFileVersionBehavior Behavior,
	int? MaxVersionsRetained,
	TimeSpan? VersionTTL);

public record VFileStorageOptions(
	VFileCompression Compression,
	TimeSpan? TTL,
	VFileVersionOptions VersionOpts)
{
	public VFileCompression Compression { get; set; } = Compression;
	public TimeSpan? TTL { get; set; } = TTL;
	/// <summary>
	/// Version options are only applied if the file already
	/// exists and it is different from the new file.
	/// </summary>
	public VFileVersionOptions VersionOpts { get; set; } = VersionOpts;
}

internal record StoreVFilesState
{
	public List<VFileInfo> NewVFileInfos = [];
	public List<VFileInfo> UpdateVFileInfos = [];
	public List<VFileInfo> DeleteVFileInfos = [];
	public List<VFileData> NewVFileData = [];
	public List<VFileDataInfo> DeleteVFileData = [];
}