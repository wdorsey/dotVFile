using Microsoft.Data.Sqlite;

namespace dotVFile;

internal record VFileDatabaseOptions(
	string Name,
	string Directory,
	string Version,
	VFilePermissions Permissions,
	VFileTools Tools);

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

	public record FileContent : Entity
	{
		public string Hash = string.Empty;
		public long Size;
		public long SizeContent;
		public byte Compression;
	}

	// Content bytes are split into a seperate table.
	// FileContentRowId is PK.
	// not of type Entity as it just an extension of FileContent
	public record FileContentBlob
	{
		public long FileContentRowId;
		public byte[]? Content;
	}

	public record Directory : Entity
	{
		public long? ParentDirectoryRowId;
		public string Name = string.Empty;
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

	public record UnreferencedFileContent
	{
		public List<long> FileContentRowIds = [];
	}

	public record SqlExpr(string Sql, List<SqliteParameter> Parameters);
}