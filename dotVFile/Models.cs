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
}

public record VFileError(
	string ErrorCode,
	string Message,
	string Function)
{
	public override string ToString()
	{
		return $@"
=== VFileError ===
{ErrorCode} in {Function}()
{Message}
==================
";
	}
}

public record VFSOptions(
	string? Name,
	string VFileDirectory,
	IVFileHooks? Hooks,
	VFileStorageOptions? DefaultStorageOptions);

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
	string FileName,
	string? Version)
{
	public override string ToString()
	{
		return Id;
	}
}

public record VFile(
	VFileInfo FileInfo,
	VFileDataInfo DataInfo,
	byte[] Content);

public record VFileInfo(
	VFileId VFileId,
	string Hash,
	int Size,
	DateTimeOffset CreationTime,
	DateTimeOffset? DeleteAt)
{
	public VFileId VFileId { get; } = VFileId;
	public string FullPath { get; } = VFileId.Id;
	public string RelativePath { get; } = VFileId.RelativePath;
	public string Name { get; } = VFileId.FileName;
	public string? Version { get; } = VFileId.Version;
	public bool IsVersion { get; } = VFileId.Version.HasValue();
	public string Extension { get; } = Util.FileExtension(VFileId.FileName);
	public string Hash { get; } = Hash;
	/// <summary>
	/// Size in bytes.
	/// </summary>
	public int Size { get; } = Size;
	public DateTimeOffset? DeleteAt { get; } = DeleteAt;
	public DateTimeOffset CreationTime { get; } = CreationTime;
}

public record VFileDataInfo(
	string Hash,
	int Size,
	int SizeOnDisk,
	DateTimeOffset CreationTime,
	VFileCompression Compression);

public record VFileData(VFileDataInfo DataInfo, byte[] Content);

public record VFilePath(string? Path, string FileName);

public record StoreVFileRequest(
	VFilePath Path,
	byte[] Content,
	VFileStorageOptions? Opts = null);

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

public record VFileStorageOptions(
	VFileExistsBehavior ExistsBehavior,
	VFileCompression Compression,
	TimeSpan? TTL,
	int? MaxVersions,
	TimeSpan? VersionTTL)
{
	public VFileExistsBehavior ExistsBehavior { get; set; } = ExistsBehavior;
	public VFileCompression Compression { get; set; } = Compression;
	public TimeSpan? TTL { get; set; } = TTL;
	public int? MaxVersions { get; set; } = MaxVersions;
	public TimeSpan? VersionTTL { get; set; } = VersionTTL;
}

internal record StoreVFilesState
{
	public List<VFileInfo> NewVFileInfos = [];
	public List<VFileInfo> UpdateVFileInfos = [];
	public List<VFileInfo> DeleteVFileInfos = [];
	public List<VFileData> NewVFileData = [];
	public List<VFileDataInfo> DeleteVFileData = [];
}