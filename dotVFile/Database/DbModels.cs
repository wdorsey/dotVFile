namespace dotVFile;

public record VFileDatabaseOptions(
	string RootPath,
	IVFileHooks Hooks);

public static class Db
{
	public record Entity
	{
		public long Id;
		public DateTimeOffset CreateTimestamp;
	}

	public record VFileInfo(
		string Hash,
		string FullPath,
		string RelativePath,
		string FileName,
		string Extension,
		long Size,
		string? Version,
		DateTimeOffset? DeleteAt,
		DateTimeOffset CreationTime)
		: Entity;

	public record VFileDataInfo(
		string Hash,
		string Directory,
		string FileName,
		long Size,
		long SizeOnDisk,
		int Compression,
		DateTimeOffset CreationTime)
		: Entity;

	public record VFileMap(
		string Hash,
		int VFileInfoId,
		int VFileDataInfoId)
		: Entity;
}