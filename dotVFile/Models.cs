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
	string RootPath,
	IVFileHooks? Hooks,
	VFileStorageOptions? DefaultStorageOptions);

public record VFSPaths(
	string Root,
	string Store);

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

public record VFile(VFileInfo FileInfo, byte[] Contents);

public record VFileInfo(
	VFileId VFileId,
	string Hash,
	int Size,
	DateTimeOffset CreationTime,
	DateTimeOffset? DeleteAt)
{
	public VFileId VFileId = VFileId;
	public string FullPath => VFileId.Id;
	public string RelativePath => VFileId.RelativePath;
	public string Name => VFileId.FileName;
	public string? Version => VFileId.Version;
	public string Extension => Util.FileExtension(Name);
	public string Hash = Hash;
	public DateTimeOffset CreationTime = CreationTime;
	/// <summary>
	/// Size in bytes.
	/// </summary>
	public int Size = Size;
	/// <summary>
	/// Versioned file.
	/// </summary>
	public bool IsVersion => Version.HasValue();
	public DateTimeOffset? DeleteAt = DeleteAt;
}

public record VFileDataInfo(
	string Hash,
	string Directory,
	string FileName,
	int Size,
	int SizeOnDisk,
	DateTimeOffset CreationTime,
	VFileCompression Compression);

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
	public List<VFileDataInfo> NewVFileDataInfo = [];
	public List<VFileDataInfo> DeleteVFileDataInfo = [];
	public List<(string Path, byte[] Bytes)> WriteFiles = [];
}