using Microsoft.Data.Sqlite;

namespace dotVFile;

internal record VFileDatabaseOptions(
	string Name,
	string VFileDirectory,
	IVFileHooks Hooks);

internal static class Db
{
	public record Entity
	{
		public long RowId;
		public Guid Id;
		public DateTimeOffset CreateTimestamp;
	}

	public record VFileInfo : Entity
	{
		public long VFileContentRowId;
		public string FilePath = string.Empty;
		public string Directory = string.Empty;
		public string FileName = string.Empty;
		public string FileExtension = string.Empty;
		public DateTimeOffset? Versioned;
		public DateTimeOffset? DeleteAt;
		public DateTimeOffset CreationTime;
	}

	public record VFileContent : Entity
	{
		public string Hash = string.Empty;
		public long Size;
		public long SizeStored;
		public byte Compression;
		public byte[]? Content;
		public DateTimeOffset CreationTime;
	}

	public record VFile(
		VFileInfo VFileInfo,
		VFileContent VFileContent);

	public record StoreVFilesResult
	{
		public List<VFileInfo> NewVFileInfos = [];
		public List<VFileInfo> UpdatedVFileInfos = [];
		public List<VFileContent> NewVFileContents = [];
	}

	public record VFileInfoQuery
	{
		public List<long> RowIds = [];
		public List<Guid> Ids = [];
		public List<string> FilePaths = [];
		public List<string> Directories = [];
		/// <summary>
		/// Default is Both, which generates no sql.
		/// </summary>
		public VFileInfoVersionQuery VersionQuery = VFileInfoVersionQuery.Both;
	}

	public record VFileContentQuery
	{
		public List<long> RowIds = [];
		public List<Guid> Ids = [];
		public List<string> Hashes = [];
	}

	public record SelectColumn(
		string Name,
		string? TableAlias = null,
		bool PrefixAlias = false)
	{
		public string Name = Name;
		public string? TableAlias = TableAlias;
		/// <summary>
		/// Prefix the TableAlias to the selected columns.
		/// if alias is 'x': SELECT x.RowId AS xRowId
		/// </summary>
		public bool ColumnNamePrefixAlias = PrefixAlias;
	}

	public record Select(
		List<SelectColumn> SelectColumns,
		SqlExpr From,
		SqlExpr Where)
	{
		public List<SqliteParameter> Parameters => [
			.. From.Parameters,
			.. Where.Parameters];
	}

	public record Delete(
		SqlExpr From,
		SqlExpr Where)
	{
		public List<SqliteParameter> Parameters => [
			.. From.Parameters,
			.. Where.Parameters];
	}

	public record SqlExpr
	{
		public SqlExpr(string sql) : this(sql, []) { }
		public SqlExpr(string sql, List<SqliteParameter> parameters)
		{
			Sql = sql;
			Parameters = parameters;
		}

		public string Sql;
		public List<SqliteParameter> Parameters = [];
	}
}