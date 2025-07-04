using Microsoft.Data.Sqlite;

namespace dotVFile;

internal record VFileDatabaseOptions(
	string Name,
	string Directory,
	string Version,
	IVFileHooks Hooks,
	VFilePermissions Permissions);

internal static class Db
{
	public record Entity
	{
		public long RowId;
		public Guid Id;
		public DateTimeOffset CreateTimestamp;
	}

	public record VFile : Entity
	{
		public long DirectoryRowId;
		public long FileContentRowId;
		public string FileName = string.Empty;
		public string FileExtension = string.Empty;
		public DateTimeOffset? Versioned;
		public DateTimeOffset? DeleteAt;
	}

	// By rule, the Content is never selected in any GetVFile function.
	// Content can only be retrieved via the FetchContent function.
	public record FileContent : Entity
	{
		public string Hash = string.Empty;
		public long Size;
		public long SizeContent;
		public byte Compression;
		public byte[]? Content;
	}

	public record Directory : Entity
	{
		public string Path = string.Empty;
	}

	public record VFileModel(
		VFile VFile,
		FileContent FileContent,
		Directory Directory);

	public record SystemInfo(
		Guid ApplicationId,
		string Version,
		DateTimeOffset? LastClean,
		DateTimeOffset LastUpdate);

	public record StoreVFilesResult
	{
		public List<VFile> NewVFiles = [];
	}

	public record UnreferencedEntities
	{
		public List<long> DirectoryRowIds = [];
		public List<long> FileContentRowIds = [];
	}

	public record SqlExpr(string Sql, List<SqliteParameter> Parameters);
}