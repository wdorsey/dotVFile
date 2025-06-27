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
	long Size,
	DateTimeOffset CreationTime,
	DateTimeOffset? DeleteAt)
{
	public VFileId VFileId = VFileId;
	public string FullPath => VFileId.Id;
	public string RelativePath => VFileId.RelativePath;
	public string Name => VFileId.FileName;
	public string? Version => VFileId.Version;
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
	/// Versioned file.
	/// </summary>
	public bool IsVersion => Version.HasValue();
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

public record VFileRelativePath(params string[] Path)
{
	public List<string> Paths = [.. Path];
}

public record VFileStorageOptions(
	VFileExistsBehavior ExistsBehavior,
	VFileCompression Compression,
	TimeSpan? TTL,
	int? MaxVersions,
	TimeSpan? VersionTTL);