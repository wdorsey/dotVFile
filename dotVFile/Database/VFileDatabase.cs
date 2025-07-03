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
CREATE TABLE IF NOT EXISTS VFile (
	RowId					INTEGER NOT NULL UNIQUE,
	Id						TEXT NOT NULL,
	DirectoryRowId			INTEGER NOT NULL,
	FileContentRowId		INTEGER NOT NULL,
	FileName				TEXT NOT NULL,
	FileExtension			TEXT NOT NULL,
	Versioned				TEXT,
	DeleteAt				TEXT,
	CreateTimestamp			TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT),
	FOREIGN KEY(DirectoryRowId) REFERENCES Directory(RowId),
	FOREIGN KEY(FileContentRowId) REFERENCES FileContent(RowId)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFile_Id ON VFile(Id);
CREATE INDEX IF NOT EXISTS        VFile_Versioned ON VFile(Versioned) WHERE Versioned IS NOT NULL;
CREATE INDEX IF NOT EXISTS        VFile_FileNameVersioned ON VFile(FileName, Versioned);
CREATE INDEX IF NOT EXISTS        VFile_DeleteAt ON VFile(DeleteAt) WHERE DeleteAt IS NOT NULL;
CREATE INDEX IF NOT EXISTS		  VFile_DirectoryRowId ON VFile(DirectoryRowId);
CREATE INDEX IF NOT EXISTS		  VFile_FileContentRowId ON VFile(FileContentRowId);

CREATE TABLE IF NOT EXISTS FileContent (
	RowId			INTEGER NOT NULL UNIQUE,
	Id				TEXT NOT NULL,
	Hash			TEXT NOT NULL,
	Size			INTEGER NOT NULL,
	SizeContent		INTEGER NOT NULL,
	Compression		INTEGER NOT NULL,
	Content			BLOB NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT)
);
CREATE UNIQUE INDEX IF NOT EXISTS FileContent_Id ON FileContent(Id);
CREATE UNIQUE INDEX IF NOT EXISTS FileContent_Hash ON FileContent(Hash);

CREATE TABLE IF NOT EXISTS Directory (
	RowId			INTEGER NOT NULL UNIQUE,
	Id				TEXT NOT NULL,
	Path			TEXT NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT)
);
CREATE UNIQUE INDEX IF NOT EXISTS Directory_Id ON Directory(Id);
CREATE UNIQUE INDEX IF NOT EXISTS Directory_Path ON Directory(Path);
";
		DbUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DropDatabase()
	{
		const string sql = @"
-- indexes
DROP INDEX IF EXISTS VFile_Id;
DROP INDEX IF EXISTS VFile_Versioned;
DROP INDEX IF EXISTS VFile_FileNameVersioned;
DROP INDEX IF EXISTS VFile_DeleteAt;
DROP INDEX IF EXISTS VFile_DirectoryRowId;
DROP INDEX IF EXISTS VFile_FileContentRowId;

DROP INDEX IF EXISTS FileContent_Id;
DROP INDEX IF EXISTS FileContent_Hash;

DROP INDEX IF EXISTS Directory_Id;
DROP INDEX IF EXISTS Directory_Path;

-- tables
DROP TABLE IF EXISTS VFile;
DROP TABLE IF EXISTS FileContent;
DROP TABLE IF EXISTS Directory;
";
		DbUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DeleteDatabase()
	{
		SqliteConnection.ClearAllPools();
		Util.DeleteFile(DatabaseFilePath);
	}

	public Db.UnreferencedEntities GetUnreferencedEntities()
	{
		var result = new Db.UnreferencedEntities();

		List<(string tableName, string columnName)> vfileReferences = [
			("Directory", "DirectoryRowId"),
			("FileContent", "FileContentRowId")];

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);

		foreach (var (tableName, columnName) in vfileReferences)
		{
			cmd.CommandText += $@"
SELECT
	x.RowId
FROM
	{tableName} x 
	LEFT JOIN VFile f ON f.{columnName} = x.RowId
WHERE
	f.RowId IS NULL;
";
		}

		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			result.DirectoryRowIds.Add(reader.GetInt64("RowId"));
		}
		reader.NextResult();
		while (reader.Read())
		{
			result.FileContentRowIds.Add(reader.GetInt64("RowId"));
		}

		return result;
	}

	public List<Db.VFile> GetExpiredVFile(DateTimeOffset cutoff)
	{
		var results = new List<Db.VFile>();

		const string sql = @"
SELECT
	*
FROM
	VFile
WHERE
	DeleteAt < @Cutoff
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("@Cutoff", SqliteType.Text, cutoff.ToDefaultString());

		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(GetVFile(reader));
		}

		return results;
	}

	public HashSet<string> GetDirectories()
	{
		var results = new HashSet<string>();

		// Path is a Unique column
		const string sql = @"
SELECT
	Path
FROM
	Directory;
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(reader.GetString("Path"));
		}

		return results;
	}

	public List<Db.VFileModel> GetVFilesById(IEnumerable<Guid> ids)
	{
		if (ids.IsEmpty()) return [];

		var inClause = DbUtil.BuildInClause(ids.Select(x => x.ToString()), "Id", "VFile", SqliteType.Text);
		var sql = GetVFilesSql(inClause.Sql);

		return ExecuteVFiles(sql, inClause.Parameters);
	}

	public List<Db.VFileModel> GetVFilesByDirectory(
		IEnumerable<string> directories,
		VFileInfoVersionQuery versionQuery)
	{
		if (directories.IsEmpty()) return [];

		var inClause = DbUtil.BuildInClause(directories, "Path", "Directory", SqliteType.Text);
		var version = GetVersionedSql(versionQuery);
		var where = $"{inClause.Sql} AND {version}";
		var sql = GetVFilesSql(where);

		return ExecuteVFiles(sql, inClause.Parameters);
	}

	public List<Db.VFileModel> GetVFilesByFilePath(
		IEnumerable<VFilePath> paths,
		VFileInfoVersionQuery versionQuery)
	{
		if (paths.IsEmpty()) return [];

		var results = new List<Db.VFileModel>();
		var idx = 0;
		var versionSql = GetVersionedSql(versionQuery);
		foreach (var pathList in paths.Partition(50))
		{
			var parameters = new List<SqliteParameter>();
			var filePathClauses = new List<string>();
			foreach (var path in pathList)
			{
				var pathParam = DbUtil.ParameterName("Path", idx);
				var fileNameParam = DbUtil.ParameterName("FileName", idx);
				filePathClauses.Add($"(Directory.Path = {pathParam} AND VFile.FileName = {fileNameParam} AND {versionSql})");
				parameters.Add(DbUtil.NewParameter(pathParam, SqliteType.Text, path.Directory));
				parameters.Add(DbUtil.NewParameter(fileNameParam, SqliteType.Text, path.FileName));
				idx++;
			}

			var where = string.Join(" OR ", filePathClauses);
			var sql = GetVFilesSql(where);

			results.AddRange(ExecuteVFiles(sql, parameters));
		}

		return results;
	}

	private List<Db.VFileModel> ExecuteVFiles(string sql, List<SqliteParameter> parameters)
	{
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddRange(parameters);
		connection.Open();
		var reader = cmd.ExecuteReader();
		return GetVFileModels(reader);
	}

	private static string GetVFilesSql(string where)
	{
		// sql returns 4 result sets:
		// select VFile
		// select FileContent
		// select Directory

		var join = @"
	VFile
	INNER JOIN FileContent ON FileContent.RowId = VFile.FileContentRowId
	INNER JOIN Directory ON Directory.RowId = VFile.DirectoryRowId";

		return $@"
SELECT
	VFile.*
FROM {join}
WHERE 
	{where};

SELECT DISTINCT
	FileContent.RowId,
	FileContent.Id,
	FileContent.Hash,
	FileContent.Size,
	FileContent.SizeContent,
	FileContent.Compression,
	FileContent.CreateTimestamp
FROM {join}
WHERE 
	{where};

SELECT DISTINCT
	Directory.*
FROM {join}
WHERE 
	{where};
";
	}

	private static List<Db.VFileModel> GetVFileModels(SqliteDataReader reader)
	{
		var results = new List<Db.VFileModel>();
		var vfiles = new List<Db.VFile>();
		var fcMap = new Dictionary<long, Db.FileContent>();
		var dirMap = new Dictionary<long, Db.Directory>();

		while (reader.Read())
		{
			vfiles.Add(GetVFile(reader));
		}
		reader.NextResult();
		while (reader.Read())
		{
			var result = GetFileContent(reader);
			fcMap.Add(result.RowId, result);
		}
		reader.NextResult();
		while (reader.Read())
		{
			var result = GetDirectory(reader);
			dirMap.Add(result.RowId, result);
		}

		foreach (var vfile in vfiles)
		{
			results.Add(new(
				vfile,
				fcMap[vfile.FileContentRowId],
				dirMap[vfile.DirectoryRowId]));
		}

		return results;
	}

	public void DeleteVFiles(List<long> rowIds)
	{
		DbUtil.ExecuteDeleteByRowId(ConnectionString, "VFile", rowIds);
	}

	public void DeleteFileContent(List<long> rowIds)
	{
		DbUtil.ExecuteDeleteByRowId(ConnectionString, "FileContent", rowIds);
	}

	public void DeleteDirectory(List<long> rowIds)
	{
		DbUtil.ExecuteDeleteByRowId(ConnectionString, "Directory", rowIds);
	}

	public Db.UnreferencedEntities DeleteUnreferencedEntities()
	{
		var result = GetUnreferencedEntities();
		DeleteFileContent(result.FileContentRowIds);
		DeleteDirectory(result.DirectoryRowIds);
		return result;
	}

	public List<Db.VFile> DeleteExpiredVFiles()
	{
		var vfiles = GetExpiredVFile(DateTimeOffset.Now);

		DeleteVFiles([.. vfiles.Select(x => x.RowId)]);

		return vfiles;
	}

	public List<Db.FileContent> FetchContent(List<Db.FileContent> contents)
	{
		if (contents.IsEmpty()) return contents;

		var rowIdMap = contents.ToDictionary(x => x.RowId);
		var clause = DbUtil.BuildInClause(contents.Select(x => x.RowId), "RowId", null, SqliteType.Integer);
		var sql = $@"
SELECT
	RowId,
	Content
FROM
	FileContent
WHERE
	{clause.Sql};
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddRange(clause.Parameters);
		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var rowId = reader.GetInt64("RowId");
			rowIdMap[rowId].Content = reader.GetBytes("Content");
		}

		return contents;
	}

	public Db.FileContent SaveFileContent(VFileInfo info, byte[] content)
	{
		// first check if content already exists
		var sql = @"
SELECT
	RowId,
	Id,
	Hash,
	Size,
	SizeContent,
	Compression,
	CreateTimestamp
FROM
	FileContent
WHERE
	Hash = @Hash
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("@Hash", SqliteType.Text, info.Hash);
		connection.Open();
		var reader = cmd.ExecuteReader();
		if (reader.Read())
		{
			// return existing
			return GetFileContent(reader);
		}

		// does not exist, insert
		sql = $@"
INSERT INTO FileContent (
	Id,
	Hash,
	Size,
	SizeContent,
	Compression,
	Content,
	CreateTimestamp)
VALUES (
	@Id,
	@Hash,
	@Size,
	@SizeContent,
	@Compression,
	@Content,
	@CreateTimestamp);
{DbUtil.SelectInsertedRowId}
";
		var dbContent = ToDbFileContent(info, content).Stamp();
		cmd = new SqliteCommand(sql, connection)
			.AddEntityParameters(dbContent)
			.AddParameter("@Hash", SqliteType.Text, dbContent.Hash)
			.AddParameter("@Size", SqliteType.Integer, dbContent.Size)
			.AddParameter("@SizeContent", SqliteType.Integer, dbContent.SizeContent)
			.AddParameter("@Compression", SqliteType.Integer, dbContent.Compression)
			.AddParameter("@Content", SqliteType.Blob, dbContent.Content ?? Util.EmptyBytes());

		reader = cmd.ExecuteReader();
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

		// build DeleteVFileInfos
		cmd.BuildDeleteByRowId("VFile", "RowId", [.. state.DeleteVFiles.Select(x => x.RowId)]);

		// build UpdateVFileInfos
		foreach (var update in state.UpdateVFiles)
		{
			// only update-able fields are Versioned and DeleteAt
			cmd.CommandText += $@"
UPDATE VFile
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
		}

		// build NewVFileInfos
		foreach (var info in state.NewVFiles)
		{
			cmd.CommandText += $@"
INSERT INTO Directory (
	Id,
	Path,
	CreateTimestamp)
SELECT
	{DbUtil.ParameterName("Id", idx)},
	{DbUtil.ParameterName("Path", idx)},
	{DbUtil.ParameterName("CreateTimestamp", idx)}
WHERE NOT EXISTS (
	SELECT 
		1
	FROM 
		Directory 
	WHERE 
		Path = {DbUtil.ParameterName("Path", idx)});
";
			var dbDirectory = new Db.Directory { Path = info.VFilePath.Directory }.Stamp();
			cmd.AddEntityParameters(dbDirectory, idx)
				.AddParameter("Path", idx, SqliteType.Text, dbDirectory.Path);
			idx++;

			cmd.CommandText += $@"
INSERT INTO VFile (
	Id,
	DirectoryRowId,
	FileContentRowId,
	FileName,
	FileExtension,
	Versioned,
	DeleteAt,
	CreateTimestamp)
VALUES (
	{DbUtil.ParameterName("Id", idx)},
	(SELECT RowId FROM Directory WHERE Path = {DbUtil.ParameterName("Path", idx)}),
	(SELECT RowId FROM FileContent WHERE Hash = {DbUtil.ParameterName("Hash", idx)}),
	{DbUtil.ParameterName("FileName", idx)},
	{DbUtil.ParameterName("FileExtension", idx)},
	{DbUtil.ParameterName("Versioned", idx)},
	{DbUtil.ParameterName("DeleteAt", idx)},
	{DbUtil.ParameterName("CreateTimestamp", idx)});
{DbUtil.SelectInsertedRowId}
";
			var dbVFile = ToDbVFile(info).Stamp();
			cmd.AddEntityParameters(dbVFile, idx)
				.AddParameter("Path", idx, SqliteType.Text, dbDirectory.Path)
				.AddParameter("Hash", idx, SqliteType.Text, info.Hash)
				.AddParameter("FileName", idx, SqliteType.Text, dbVFile.FileName)
				.AddParameter("FileExtension", idx, SqliteType.Text, dbVFile.FileExtension)
				.AddParameter("Versioned", idx, SqliteType.Text, (dbVFile.Versioned?.ToDefaultString()).NullCoalesce())
				.AddParameter("DeleteAt", idx, SqliteType.Text, (dbVFile.DeleteAt?.ToDefaultString()).NullCoalesce());
			idx++;
			result.NewVFiles.Add(dbVFile);
		}

		try
		{
			var reader = cmd.ExecuteReader();
			result.NewVFiles.ReadInsertedRowIds(reader);
			transaction.Commit();
		}
		catch (SqliteException e)
		{
			Hooks.ErrorHandler(new(
				VFileErrorCodes.DatabaseException,
				e.ToString(),
				nameof(SaveStoreVFilesState)));
			transaction.Rollback();
			return null; // null indicates error
		}

		return result;
	}

	private static Db.VFile GetVFile(SqliteDataReader reader)
	{
		return new Db.VFile
		{
			DirectoryRowId = reader.GetInt64("DirectoryRowId"),
			FileContentRowId = reader.GetInt64("FileContentRowId"),
			FileName = reader.GetString("FileName"),
			FileExtension = reader.GetString("FileExtension"),
			Versioned = reader.GetDateTimeOffsetNullable("Versioned"),
			DeleteAt = reader.GetDateTimeOffsetNullable("DeleteAt")
		}.GetEntityValues(reader);
	}

	private static Db.FileContent GetFileContent(SqliteDataReader reader)
	{
		return new Db.FileContent
		{
			Hash = reader.GetString("Hash"),
			Size = reader.GetInt64("Size"),
			SizeContent = reader.GetInt64("SizeContent"),
			Compression = reader.GetByte("Compression")
		}.GetEntityValues(reader);
	}

	private static Db.Directory GetDirectory(SqliteDataReader reader)
	{
		return new Db.Directory
		{
			Path = reader.GetString("Path")
		}.GetEntityValues(reader);
	}

	private static string GetVersionedSql(VFileInfoVersionQuery versionQuery)
	{
		return versionQuery switch
		{
			VFileInfoVersionQuery.Latest => "VFile.Versioned IS NULL",
			VFileInfoVersionQuery.Versions => "VFile.Versioned IS NOT NULL",
			VFileInfoVersionQuery.Both => "1=1",
			_ => throw new ArgumentOutOfRangeException(nameof(versionQuery), $"{versionQuery}")
		};
	}

	private static Db.VFile ToDbVFile(VFileInfo info)
	{
		return new Db.VFile
		{
			Id = info.Id,
			FileName = info.VFilePath.FileName,
			FileExtension = info.VFilePath.FileExtension,
			Versioned = info.Versioned,
			DeleteAt = info.DeleteAt
		};
	}

	private static Db.FileContent ToDbFileContent(VFileInfo info, byte[] content)
	{
		return new Db.FileContent
		{
			Id = info.ContentId,
			Hash = info.Hash,
			Size = info.Size,
			SizeContent = info.SizeStored,
			Compression = (byte)info.Compression,
			Content = content
		};
	}
}
