namespace BlobVFS;

public record VFSDatabaseOptions(
	string RootPath,
	IVFSCallbacks? Callbacks);

public static class Db
{
	public record Entity(Guid Id, DateTimeOffset CreateTimestamp);

	public record VFileInfo(
		string Hash,
		string FullPath,
		string RelativePath,
		string FileName,
		string Extension,
		long Size,
		bool IsLatest,
		bool IsVersion,
		DateTimeOffset? DeleteAt,
		DateTimeOffset CreationTime,
		Guid Id,
		DateTimeOffset CreateTimestamp) : Entity(Id, CreateTimestamp);

	public record VFileDataInfo(
		string Hash,
		string Directory,
		string FileName,
		long Size,
		long SizeOnDisk,
		int Compression,
		DateTimeOffset CreationTime,
		Guid Id,
		DateTimeOffset CreateTimestamp) : Entity(Id, CreateTimestamp);

	public record VFileMap(
		string Hash,
		Guid VFileInfoId,
		Guid VFileDataInfoId,
		Guid Id,
		DateTimeOffset CreateTimestamp) : Entity(Id, CreateTimestamp);
}