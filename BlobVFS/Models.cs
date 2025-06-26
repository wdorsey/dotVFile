using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BlobVFS;

public interface IVFSCallbacks
{
	void HandleError(VFSError error);
	void Log(string msg);
}

public record VFSError(string ErrorCode, Exception Exception);

public record VFSOptions(string RootPath, IVFSCallbacks? Callbacks);

/// <summary>
/// Uniquely identifies a VFile.<br/>
/// Examples of Id:<br/>
///	\file.txt<br/>
///	\folder\subfolder\file.txt<br/>
///	\folder\file.{Version}.txt
/// </summary>
public record VFileId(string Id, string RelativePath, string FileName, string? Version);

public record VFile
{
	public VFileInfo? FileInfo;
	public VFileDataInfo? DataInfo;
	public byte[]? Contents;
}

public record VFileInfo(
	VFileId VFileId,
	string Hash,
	DateTimeOffset CreationTime,
	long Size,
	DateTimeOffset? DeleteAt)
{
	public VFileId Id = VFileId;
	public string FullPath => Id.Id;
	public string RelativePath => Id.RelativePath;
	public string Name => Id.FileName;
	public string Extension => Util.FileExtension(Name);
	/// <summary>
	/// Effective Id of the data contained in the file.
	/// </summary>
	public string Hash = Hash;
	public DateTimeOffset CreationTime = CreationTime;
	/// <summary>
	/// Size in bytes.
	/// </summary>
	public long Size = Size;

	/// <summary>
	/// Current non-versioned file.
	/// </summary>
	public bool IsLatest => Id.Version == null;
	/// <summary>
	/// Versioned file.
	/// </summary>
	public bool IsVersion => !IsLatest;
	public DateTimeOffset? DeleteAt = DeleteAt;
}

public record VFileDataInfo(
	string Hash,
	string Directory,
	string FileName,
	long Size,
	long SizeOnDisk,
	DateTimeOffset CreationTime,
	VFileCompression Compression)
{
	public string Hash = Hash;
	public string Directory = Directory;
	public string FileName = FileName;
	public long Size = Size;
	public long SizeOnDisk = SizeOnDisk;
	public DateTimeOffset CreationTime = CreationTime;
	public VFileCompression Compression = Compression;
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

public record VFileStorageOptions(
	VFileExistsBehavior ExistsBehavior,
	VFileCompression Compression,
	TimeSpan? TTL,
	int? MaxVersions,
	TimeSpan? VersionTTL);