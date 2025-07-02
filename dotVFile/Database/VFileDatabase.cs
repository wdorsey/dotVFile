using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;

namespace dotVFile;

internal class VFileDatabase
{
	public VFileDatabase(VFileDatabaseOptions opts)
	{
		VFileDirectory = opts.VFileDirectory;
		Hooks = opts.Hooks;
		DatabaseFilePath = new(Path.Combine(VFileDirectory, $"{opts.Name}.vfile.db"));
		ConnectionString = $"Data Source={DatabaseFilePath};";
		CreateDatabase();
	}

	public string VFileDirectory { get; }
	public IVFileHooks Hooks { get; }
	public string DatabaseFilePath { get; }
	public string ConnectionString { get; }

	public void CreateDatabase()
	{
		const string sql = @"
CREATE TABLE IF NOT EXISTS VFileInfo (
	RowId					INTEGER NOT NULL UNIQUE,
	Id						TEXT NOT NULL,
	VFileContentRowId		INTEGER NOT NULL,
	FilePath				TEXT NOT NULL,
	Directory				TEXT NOT NULL,
	FileName				TEXT NOT NULL,
	FileExtension			TEXT NOT NULL,
	Versioned				TEXT,
	DeleteAt				TEXT,
	CreationTime			TEXT NOT NULL,
	CreateTimestamp			TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT),
	FOREIGN KEY(VFileContentRowId) REFERENCES VFileContent(RowId)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFileInfo_Id ON VFileInfo(Id);
CREATE INDEX IF NOT EXISTS        VFileInfo_VFileContentRowId ON VFileInfo(VFileContentRowId);
CREATE INDEX IF NOT EXISTS        VFileInfo_FilePath ON VFileInfo(FilePath);
CREATE INDEX IF NOT EXISTS        VFileInfo_FilePathLatest ON VFileInfo(FilePath, Versioned) WHERE Versioned IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS VFileInfo_FilePathVersioned ON VFileInfo(FilePath, Versioned);
CREATE INDEX IF NOT EXISTS        VFileInfo_Directory ON VFileInfo(Directory);
CREATE INDEX IF NOT EXISTS        VFileInfo_DeleteAt ON VFileInfo(DeleteAt) WHERE DeleteAt IS NOT NULL;

CREATE TABLE IF NOT EXISTS VFileContent (
	RowId			INTEGER NOT NULL UNIQUE,
	Id				TEXT NOT NULL,
	Hash			TEXT NOT NULL,
	Size			INTEGER NOT NULL,
	SizeStored		INTEGER NOT NULL,
	Compression		INTEGER NOT NULL,
	Content			BLOB NOT NULL,
	CreationTime	TEXT NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFileContent_Id ON VFileContent(Id);
CREATE UNIQUE INDEX IF NOT EXISTS VFileContent_Hash ON VFileContent(Hash);
";
		DbUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DropDatabase()
	{
		const string sql = @"
-- indexes
DROP INDEX IF EXISTS VFileInfo_Id;
DROP INDEX IF EXISTS VFileInfo_VFileContentInfoRowId;
DROP INDEX IF EXISTS VFileInfo_FilePath;
DROP INDEX IF EXISTS VFileInfo_FilePathLatest;
DROP INDEX IF EXISTS VFileInfo_FilePathVersioned;
DROP INDEX IF EXISTS VFileInfo_Directory;
DROP INDEX IF EXISTS VFileInfo_DeleteAt;
DROP INDEX IF EXISTS VFileContent_Id;
DROP INDEX IF EXISTS VFileContent_Hash;

-- tables
DROP TABLE IF EXISTS VFileInfo;
DROP TABLE IF EXISTS VFileContent;
";
		DbUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DeleteDatabase()
	{
		SqliteConnection.ClearAllPools();
		Util.DeleteFile(DatabaseFilePath);
	}

	public List<Db.VFile> QueryVFiles(Db.VFileInfoQuery query)
	{
		var result = new List<Db.VFile>();
		const string infoAlias = "i";
		const string contentAlias = "c";

		List<Db.SelectColumn> columns =
		[
			.. VFileInfoColumns(infoAlias, true),
			.. VFileContentColumns(contentAlias, true)
		];

		var from = new Db.SqlExpr(@$"
	VFileInfo {infoAlias}
	INNER JOIN VFileContent {contentAlias} ON {contentAlias}.RowId = {infoAlias}.VFileContentRowId
");

		List<Db.SqlExpr> inQueries = [
			.. DbUtil.BuildInClause(query.RowIds, "RowId", infoAlias, SqliteType.Integer),
			.. DbUtil.BuildInClause(query.Ids.Select(x => x.ToString()), "Id", infoAlias, SqliteType.Text),
			.. DbUtil.BuildInClause(query.FilePaths, "FilePath", infoAlias, SqliteType.Text),
			.. DbUtil.BuildInClause(query.Directories, "Directory", infoAlias, SqliteType.Text)];

		var where = inQueries.Merge(" OR ").Wrap("(", ")")
			.And(BuildVersionedSql(query.VersionQuery));

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildSelect(new(columns, from, where));

		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var info = GetVFileInfo(reader, infoAlias);
			var content = GetVFileContent(reader, contentAlias);
			result.Add(new(info, content));
		}

		return result;
	}

	public List<Db.VFileContent> QueryVFileContent(Db.VFileContentQuery query)
	{
		var result = new List<Db.VFileContent>();

		var columns = VFileContentColumns(null, false);
		var from = new Db.SqlExpr("\tVFileContent");

		List<Db.SqlExpr> inQueries = [
			.. DbUtil.BuildInClause(query.RowIds, "RowId", null, SqliteType.Integer),
			.. DbUtil.BuildInClause(query.Ids.Select(x => x.ToString()), "Id", null, SqliteType.Text),
			.. DbUtil.BuildInClause(query.Hashes, "Hash", null, SqliteType.Text)];

		var where = inQueries.Merge(" OR ").Wrap("(", ")");

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildSelect(new(columns, from, where));

		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var content = GetVFileContent(reader, string.Empty);
			result.Add(content);
		}

		return result;
	}

	private static Db.VFileInfo GetVFileInfo(SqliteDataReader reader, string tableAlias)
	{
		var info = new Db.VFileInfo().GetEntityValues(reader, tableAlias);
		info.VFileContentRowId = reader.GetInt64(tableAlias + "VFileContentRowId");
		info.FilePath = reader.GetString(tableAlias + "FilePath");
		info.Directory = reader.GetString(tableAlias + "Directory");
		info.FileName = reader.GetString(tableAlias + "FileName");
		info.FileExtension = reader.GetString(tableAlias + "FileExtension");
		info.Versioned = reader.GetDateTimeOffsetNullable(tableAlias + "Versioned");
		info.DeleteAt = reader.GetDateTimeOffsetNullable(tableAlias + "DeleteAt");
		info.CreationTime = reader.GetDateTimeOffset(tableAlias + "CreationTime");
		return info;
	}

	private static Db.VFileContent GetVFileContent(SqliteDataReader reader, string tableAlias)
	{
		var content = new Db.VFileContent().GetEntityValues(reader, tableAlias);
		content.Hash = reader.GetString(tableAlias + "Hash");
		content.Size = reader.GetInt64(tableAlias + "Size");
		content.SizeStored = reader.GetInt64(tableAlias + "SizeStored");
		content.Compression = reader.GetByte(tableAlias + "Compression");
		content.CreationTime = reader.GetDateTimeOffset(tableAlias + "CreationTime");
		return content;
	}

	private static List<Db.SelectColumn> VFileInfoColumns(string? tableAlias, bool prefixAlias)
	{
		var columns = new List<Db.SelectColumn>()
			.AddEntityColumns(tableAlias, prefixAlias);
		columns.Add(new("VFileContentRowId", tableAlias, prefixAlias));
		columns.Add(new("FilePath", tableAlias, prefixAlias));
		columns.Add(new("Directory", tableAlias, prefixAlias));
		columns.Add(new("FileName", tableAlias, prefixAlias));
		columns.Add(new("FileExtension", tableAlias, prefixAlias));
		columns.Add(new("Versioned", tableAlias, prefixAlias));
		columns.Add(new("DeleteAt", tableAlias, prefixAlias));
		columns.Add(new("CreationTime", tableAlias, prefixAlias));
		return columns;
	}

	/// <summary>
	/// Does not select Content column.
	/// </summary>
	private static List<Db.SelectColumn> VFileContentColumns(string? tableAlias, bool prefixAlias)
	{
		var columns = new List<Db.SelectColumn>()
			.AddEntityColumns(tableAlias, prefixAlias);
		columns.Add(new("Hash", tableAlias, prefixAlias));
		columns.Add(new("Size", tableAlias, prefixAlias));
		columns.Add(new("SizeStored", tableAlias, prefixAlias));
		columns.Add(new("Compression", tableAlias, prefixAlias));
		columns.Add(new("CreationTime", tableAlias, prefixAlias));
		return columns;
	}

	private static Db.SqlExpr BuildVersionedSql(VFileInfoVersionQuery versionQuery)
	{
		var sql = versionQuery switch
		{
			VFileInfoVersionQuery.Latest => "\tVersioned IS NULL",
			VFileInfoVersionQuery.Versions => "\tVersioned IS NOT NULL",
			VFileInfoVersionQuery.Both => string.Empty,
			_ => throw new ArgumentOutOfRangeException(nameof(versionQuery), $"{versionQuery}")
		};

		return new(sql);
	}

	public List<long> GetUnreferencedVFileContentRowIds()
	{
		var results = new List<long>();

		const string sql = @"
SELECT
	c.RowId
FROM
	VFileContent c 
	LEFT JOIN VFileInfo i ON i.VFileContentRowId = c.RowId
WHERE
	i.RowId IS NULL
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(reader.GetInt64("RowId"));
		}

		return results;
	}

	public List<string> GetDistinctDirectories()
	{
		var results = new List<string>();

		const string sql = @"
SELECT DISTINCT
	Directory
FROM
	VFileInfo
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(reader.GetString("Directory"));
		}

		return results;
	}

	public void DeleteVFileContent(List<long> rowIds)
	{
		DbUtil.ExecuteDeleteByRowId(ConnectionString, "VFileContent", rowIds);
	}

	public List<Db.VFileContent> FetchContent(List<Db.VFileContent> vfiles)
	{
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();

		var rowIdMap = vfiles.ToDictionary(x => x.RowId);
		var clauses = DbUtil.BuildInClause(vfiles.Select(x => x.RowId), "RowId", null, SqliteType.Integer);

		foreach (var clause in clauses)
		{
			var sql = $@"
SELECT
	RowId,
	Content
FROM
	VFileContent
WHERE
	{clause.Sql};
";
			var cmd = new SqliteCommand(sql, connection);
			cmd.Parameters.AddRange(clause.Parameters);
			var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				var rowId = reader.GetInt64("RowId");
				rowIdMap[rowId].Content = reader.GetBytes("Content");
			}
		}

		return vfiles;
	}

	public Db.VFileContent SaveVFileContent(VFileInfo info, byte[] content)
	{
		const string sql = $@"
INSERT INTO VFileContent (
	Id,
	Hash,
	Size,
	SizeStored,
	Compression,
	Content,
	CreationTime,
	CreateTimestamp)
VALUES (
	@Id,
	@Hash,
	@Size,
	@SizeStored,
	@Compression,
	@Content,
	@CreationTime,
	@CreateTimestamp);
{DbUtil.SelectInsertedRowId}
";
		var dbContent = ToDbVFileContent(info, content).Stamp();
		using var connection = new SqliteConnection(ConnectionString);

		var cmd = new SqliteCommand(sql, connection)
			.AddEntityParameters(dbContent)
			.AddParameter("@Hash", SqliteType.Text, dbContent.Hash)
			.AddParameter("@Size", SqliteType.Integer, dbContent.Size)
			.AddParameter("@SizeStored", SqliteType.Integer, dbContent.SizeStored)
			.AddParameter("@Compression", SqliteType.Integer, dbContent.Compression)
			.AddParameter("@Content", SqliteType.Blob, dbContent.Content ?? Util.EmptyBytes())
			.AddParameter("@CreationTime", SqliteType.Text, dbContent.CreationTime.ToDefaultString());
		connection.Open();
		var reader = cmd.ExecuteReader();
		return dbContent.ReadRowId(reader);
	}

	public Db.StoreVFilesResult? SaveStoreVFilesState(StoreVFilesState state)
	{
		// All VFileInfo changes written transactionally.
		// order:
		//	DeleteVFileInfos
		//  UpdateVFileInfos
		//	NewVFileInfos
		var result = new Db.StoreVFilesResult();
		int idx = 0;
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		var cmd = new SqliteCommand(string.Empty, connection, transaction);

		// DeleteVFileInfos
		cmd.BuildDeleteByRowId(new("VFileInfo"), [.. state.DeleteVFileInfos.Select(x => x.RowId)]);

		// UpdateVFileInfos
		foreach (var update in state.UpdateVFileInfos)
		{
			// only update-able fields are Versioned and DeleteAt
			cmd.CommandText += $@"
UPDATE VFileInfo
SET 
	Versioned = {DbUtil.ParameterName("Versioned", idx)},
	DeleteAt = {DbUtil.ParameterName("DeleteAt", idx)}
WHERE
	RowId = {DbUtil.ParameterName("RowId", idx)};
";
			cmd.AddParameter("Versioned", idx, SqliteType.Text, (update.Versioned?.ToDefaultString()).NullCoalesce());
			cmd.AddParameter("DeleteAt", idx, SqliteType.Text, (update.DeleteAt?.ToDefaultString()).NullCoalesce());
			cmd.AddParameter("RowId", idx, SqliteType.Integer, update.RowId);
			idx++;
			result.UpdatedVFileInfos.Add(update);
		}

		// NewVFileInfos
		foreach (var info in state.NewVFileInfos)
		{
			cmd.CommandText += $@"
INSERT INTO VFileInfo (
	Id,
	VFileContentRowId,
	FilePath,
	Directory,
	FileName,
	FileExtension,
	Versioned,
	DeleteAt,
	CreationTime,
	CreateTimestamp)
VALUES (
	{DbUtil.ParameterName("Id", idx)},
	(SELECT RowId FROM VFileContent WHERE Hash = {DbUtil.ParameterName("Hash", idx)}),
	{DbUtil.ParameterName("FilePath", idx)},
	{DbUtil.ParameterName("Directory", idx)},
	{DbUtil.ParameterName("FileName", idx)},
	{DbUtil.ParameterName("FileExtension", idx)},
	{DbUtil.ParameterName("Versioned", idx)},
	{DbUtil.ParameterName("DeleteAt", idx)},
	{DbUtil.ParameterName("CreationTime", idx)},
	{DbUtil.ParameterName("CreateTimestamp", idx)});
{DbUtil.SelectInsertedRowId}
";
			var dbInfo = ToDbVFileInfo(info).Stamp();
			cmd.AddEntityParameters(dbInfo, idx)
				.AddParameter("Hash", idx, SqliteType.Text, info.Hash)
				.AddParameter("FilePath", idx, SqliteType.Text, dbInfo.FilePath)
				.AddParameter("Directory", idx, SqliteType.Text, dbInfo.Directory)
				.AddParameter("FileName", idx, SqliteType.Text, dbInfo.FileName)
				.AddParameter("FileExtension", idx, SqliteType.Text, dbInfo.FileExtension)
				.AddParameter("Versioned", idx, SqliteType.Text, (dbInfo.Versioned?.ToDefaultString()).NullCoalesce())
				.AddParameter("DeleteAt", idx, SqliteType.Text, (dbInfo.DeleteAt?.ToDefaultString()).NullCoalesce())
				.AddParameter("CreationTime", idx, SqliteType.Text, dbInfo.CreationTime.ToDefaultString());
			idx++;
			result.NewVFileInfos.Add(dbInfo);
		}

		try
		{
			var reader = cmd.ExecuteReader();
			result.NewVFileContents.ReadInsertedRowIds(reader);
			result.NewVFileInfos.ReadInsertedRowIds(reader);
			transaction.Commit();
		}
		catch (SqliteException e)
		{
			Hooks.ErrorHandler(new(
				VFileErrorCodes.DatabaseException,
				e.ToString(),
				nameof(SaveVFileContent)));
			transaction.Rollback();
			return null; // null indicates error
		}

		return result;
	}

	private static Db.VFileInfo ToDbVFileInfo(VFileInfo info)
	{
		return new Db.VFileInfo
		{
			Id = info.Id,
			FilePath = info.FilePath,
			Directory = info.Directory,
			FileName = info.FileName,
			FileExtension = info.FileExtension,
			Versioned = info.Versioned,
			DeleteAt = info.DeleteAt,
			CreationTime = info.CreationTime
		};
	}

	private static Db.VFileContent ToDbVFileContent(VFileInfo info, byte[] content)
	{
		return new Db.VFileContent
		{
			Id = info.ContentId,
			Hash = info.Hash,
			Size = info.Size,
			SizeStored = info.SizeStored,
			Compression = (byte)info.Compression,
			Content = content,
			CreationTime = info.ContentCreationTime
		};
	}
}
