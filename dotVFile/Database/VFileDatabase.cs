using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;

namespace dotVFile;

internal class VFileDatabase
{
	public VFileDatabase(VFileDatabaseOptions opts)
	{
		ApplicationId = Guid.NewGuid();
		Directory = opts.Directory;
		Version = opts.Version;
		Permissions = opts.Permissions;
		Tools = opts.Tools;
		DatabaseFilePath = new(Path.Combine(Directory, $"{opts.Name}.vfile.db"));
		ConnectionString = $"Data Source={DatabaseFilePath};";
		CreateDatabase(); // also calls SetSystemInfo()
	}

	public Guid ApplicationId { get; }
	public string Directory { get; }
	public string Version { get; }
	public VFilePermissions Permissions { get; }
	public VFileTools Tools { get; }
	public string DatabaseFilePath { get; }
	public string ConnectionString { get; }

	public void CreateDatabase()
	{
		var sql = @"
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
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT)
);
CREATE UNIQUE INDEX IF NOT EXISTS FileContent_Id ON FileContent(Id);
CREATE UNIQUE INDEX IF NOT EXISTS FileContent_Hash ON FileContent(Hash);

CREATE TABLE IF NOT EXISTS FileContentBlob (
	FileContentRowId	INTEGER NOT NULL,
	Content				BLOB NOT NULL,
	PRIMARY KEY(FileContentRowId),
	FOREIGN KEY(FileContentRowId) REFERENCES FileContent(RowId)
);
CREATE UNIQUE INDEX IF NOT EXISTS FileContentBlob_FileContentRowId ON FileContentBlob(FileContentRowId);

CREATE TABLE IF NOT EXISTS Directory (
	RowId			INTEGER NOT NULL UNIQUE,
	Id				TEXT NOT NULL,
	Path			TEXT NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT)
);
CREATE UNIQUE INDEX IF NOT EXISTS Directory_Id ON Directory(Id);
CREATE UNIQUE INDEX IF NOT EXISTS Directory_Path ON Directory(Path);

CREATE TABLE IF NOT EXISTS SystemInfo (
	ApplicationId	TEXT NOT NULL,
	Version			TEXT NOT NULL,
	LastClean		TEXT,
	LastUpdate		TEXT NOT NULL
);

-- defrag the database file.
VACUUM;
";

		DbUtil.ExecuteNonQuery(ConnectionString, sql);
		SetSystemInfo();
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

DROP INDEX IF EXISTS FileContentBlob_FileContentRowId;

DROP INDEX IF EXISTS Directory_Id;
DROP INDEX IF EXISTS Directory_Path;

-- tables
DROP TABLE IF EXISTS VFile;
DROP TABLE IF EXISTS FileContentBlob;
DROP TABLE IF EXISTS FileContent;
DROP TABLE IF EXISTS Directory;
DROP TABLE IF EXISTS SystemInfo;
";
		DbUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DeleteDatabase()
	{
		SqliteConnection.ClearAllPools();
		Util.DeleteFile(DatabaseFilePath);
	}

	public Db.SystemInfo GetSystemInfo()
	{
		// this always assumes the row in SystemInfo exists.

		// SystemInfo only ever has 1 row
		const string sql = @"SELECT * FROM SystemInfo";

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);

		connection.Open();
		var reader = cmd.ExecuteReader();
		reader.Read();
		return new(
			reader.GetGuid("ApplicationId"),
			reader.GetString("Version"),
			reader.GetDateTimeOffsetNullable("LastClean"),
			reader.GetDateTimeOffset("LastUpdate"));
	}

	private Db.SystemInfo SetSystemInfo()
	{
		return UpdateSystemInfo(new(
			ApplicationId,
			Version,
			null,
			DateTimeOffset.Now));
	}

	public Db.SystemInfo UpdateSystemInfo(Db.SystemInfo info)
	{
		var db = info with { LastUpdate = DateTimeOffset.Now };

		const string sql = @"
-- create SystemInfo row if not exists
INSERT INTO SystemInfo (
	ApplicationId,
	Version,
	LastClean,
	LastUpdate)
SELECT
	@ApplicationId,
	@Version,
	@LastClean,
	@LastUpdate
WHERE NOT EXISTS (SELECT 1 FROM SystemInfo);

-- no where clause needed, only 1 row in the table
UPDATE SystemInfo
SET 
	ApplicationId = @ApplicationId,
	Version = @Version,
	LastClean = @LastClean,
	LastUpdate = @LastUpdate;
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("@ApplicationId", SqliteType.Text, db.ApplicationId)
			.AddParameter("@Version", SqliteType.Text, db.Version)
			.AddParameter("@LastClean", SqliteType.Text, (db.LastClean?.ToDefaultString()).NullCoalesce())
			.AddParameter("@LastUpdate", SqliteType.Text, db.LastUpdate.ToDefaultString());

		connection.Open();
		cmd.ExecuteNonQuery();

		return db;
	}

	public bool VerifyPermission(VFilePermission permission)
	{
		// Multi means access is always allowed
		if (permission == VFilePermission.MultiApplication)
			return true;

		var info = GetSystemInfo();

		if (ApplicationId != info.ApplicationId)
		{
			Tools.ErrorHandler(new(
				VFileErrorCodes.MultipleApplicationInstances,
				"Detected multiple applications using same VFile instance simultaneously." +
				"VFilePermission is set to VFilePermission.SingleApplication.",
				null));
			throw new Exception(VFileErrorCodes.MultipleApplicationInstances);
		}

		return true;
	}

	public Db.UnreferencedEntities GetUnreferencedEntities()
	{
		VerifyPermission(Permissions.Read);

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
		VerifyPermission(Permissions.Read);

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
		VerifyPermission(Permissions.Read);

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

	public List<Db.FileContent> GetFileContent(List<long> rowIds)
	{
		VerifyPermission(Permissions.Read);

		var results = DbUtil.ExecuteGetById(
			ConnectionString,
			rowIds,
			"FileContent",
			"RowId",
			"*",
			reader => ReadEntities(reader, "", GetFileContent));

		return results;
	}

	public List<Db.Directory> GetDirectories(List<long> rowIds)
	{
		VerifyPermission(Permissions.Read);

		var results = DbUtil.ExecuteGetById(
			ConnectionString,
			rowIds,
			"Directory",
			"RowId",
			"*",
			reader => ReadEntities(reader, "", GetDirectory));

		return results;
	}

	public List<Db.VFileModel> GetVFilesById(IEnumerable<Guid> ids)
	{
		if (ids.IsEmpty()) return [];

		VerifyPermission(Permissions.Read);

		var inClause = DbUtil.BuildInClause(ids.Select(x => x.ToString()), "Id", "VFile");
		var sql = GetVFilesSql(inClause.Sql);

		return ExecuteVFiles(sql, inClause.Parameters);
	}

	public List<Db.VFileModel> GetVFilesByDirectory(
		IEnumerable<string> directories,
		VFileInfoVersionQuery versionQuery)
	{
		if (directories.IsEmpty()) return [];

		VerifyPermission(Permissions.Read);

		var inClause = DbUtil.BuildInClause(directories, "Path", "Directory");
		var version = GetVersionedSql(versionQuery);
		var where = $"{inClause.Sql} AND {version}";
		var sql = GetVFilesSql(where);

		return ExecuteVFiles(sql, inClause.Parameters);
	}

	public List<Db.VFileModel> GetVFilesByFilePath(
		IEnumerable<VFilePath> paths,
		VFileInfoVersionQuery versionQuery)
	{
		// @TODO: this probably can be optimized
		if (paths.IsEmpty()) return [];

		VerifyPermission(Permissions.Read);

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
				parameters.Add(DbUtil.NewParameter(pathParam, SqliteType.Text, path.Directory.Path));
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
		var results = new List<Db.VFileModel>();

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddRange(parameters);

		connection.Open();
		var reader = cmd.ExecuteReader();
		var vfiles = new List<Db.VFile>();
		while (reader.Read())
		{
			vfiles.Add(GetVFile(reader));
		}

		var contentMap = GetFileContent([.. vfiles.Select(x => x.FileContentRowId)])
			.ToDictionary(x => x.RowId);

		var directoryMap = GetDirectories([.. vfiles.Select(x => x.DirectoryRowId)])
			.ToDictionary(x => x.RowId);

		foreach (var vfile in vfiles)
		{
			results.Add(new(
				vfile,
				contentMap[vfile.FileContentRowId],
				directoryMap[vfile.DirectoryRowId]));
		}

		return results;
	}

	private static string GetVFilesSql(string where)
	{
		return $@"
SELECT
	VFile.RowId,
	VFile.Id,
	VFile.DirectoryRowId,
	VFile.FileContentRowId,
	VFile.FileName,
	VFile.FileExtension,
	VFile.Versioned,
	VFile.DeleteAt,
	VFile.CreateTimestamp
FROM
	VFile
	INNER JOIN FileContent ON FileContent.RowId = VFile.FileContentRowId
	INNER JOIN Directory ON Directory.RowId = VFile.DirectoryRowId
WHERE 
	{where};
";
	}

	public void DeleteVFiles(List<long> rowIds)
	{
		VerifyPermission(Permissions.Write);

		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildDeleteByRowId("VFile", "RowId", rowIds);
		cmd.ExecuteNonQuery();
	}

	public void DeleteFileContent(List<long> rowIds)
	{
		VerifyPermission(Permissions.Write);

		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			var cmd = new SqliteCommand(string.Empty, connection, transaction);
			cmd.BuildDeleteByRowId("FileContentBlob", "FileContentRowId", rowIds);
			cmd.BuildDeleteByRowId("FileContent", "RowId", rowIds);
			cmd.ExecuteNonQuery();
			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	public void DeleteDirectory(List<long> rowIds)
	{
		VerifyPermission(Permissions.Write);

		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildDeleteByRowId("Directory", "RowId", rowIds);
		cmd.ExecuteNonQuery();
	}

	public Db.UnreferencedEntities DeleteUnreferencedEntities()
	{
		VerifyPermission(Permissions.Write);
		var result = GetUnreferencedEntities();
		DeleteFileContent(result.FileContentRowIds);
		DeleteDirectory(result.DirectoryRowIds);
		return result;
	}

	public List<Db.VFile> DeleteExpiredVFiles()
	{
		VerifyPermission(Permissions.Write);

		var vfiles = GetExpiredVFile(DateTimeOffset.Now);
		DeleteVFiles([.. vfiles.Select(x => x.RowId)]);
		return vfiles;
	}

	public byte[] GetContentBytes(Db.FileContent content)
	{
		VerifyPermission(Permissions.Read);

		const string sql = $@"
SELECT
	Content
FROM
	FileContentBlob
WHERE
	FileContentRowId = @RowId;
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("@RowId", SqliteType.Integer, content.RowId);
		connection.Open();
		var reader = cmd.ExecuteReader();
		reader.Read();
		var result = reader.GetBytes("Content");

		return result;
	}

	public Db.FileContent SaveFileContent(VFileInfo info, byte[] bytes)
	{
		return SaveFileContent([(info, bytes)]).Single();
	}

	public List<Db.FileContent> SaveFileContent(List<(VFileInfo Info, byte[] Bytes)> contents)
	{
		if (contents.Count == 0) return [];

		VerifyPermission(Permissions.Write);

		var results = new List<Db.FileContent>();

		// check for existing FileContent first.
		// SaveVFiles blindly calls this function without checking.
		var existingHashes = new HashSet<string>();
		var hashIn = DbUtil.BuildInClause(contents.Select(x => x.Info.Hash), "Hash", "FileContent");
		var sql = $"SELECT * FROM FileContent WHERE {hashIn.Sql}";

		// we use a transaction because it is WAY faster to do bulk operations
		// within a single global transaction, because otherwise sqlite basically
		// creates an entire new transaction for every single operation.
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			var cmd = new SqliteCommand(sql, connection, transaction);
			cmd.Parameters.AddRange(hashIn.Parameters);

			connection.Open();
			var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				var existing = GetFileContent(reader);
				results.Add(existing);
				existingHashes.Add(existing.Hash);
			}

			var idx = 0;
			cmd = new SqliteCommand(string.Empty, connection, transaction);
			var newContent = new List<Db.FileContent>();
			foreach (var (info, bytes) in contents)
			{
				if (existingHashes.Contains(info.Hash))
					continue;

				var content = ToDbFileContent(info).Stamp();

				cmd.CommandText += $@"
INSERT INTO FileContent (
	Id,
	Hash,
	Size,
	SizeContent,
	Compression,
	CreateTimestamp)
VALUES (
	{DbUtil.ParameterName("Id", idx)},
	{DbUtil.ParameterName("Hash", idx)},
	{DbUtil.ParameterName("Size", idx)},
	{DbUtil.ParameterName("SizeContent", idx)},
	{DbUtil.ParameterName("Compression", idx)},
	{DbUtil.ParameterName("CreateTimestamp", idx)});

{DbUtil.SelectInsertedRowId}

INSERT INTO FileContentBlob (
	FileContentRowId,
	Content)
VALUES (
	(last_insert_rowid()),
	{DbUtil.ParameterName("Content", idx)});
";
				cmd.AddEntityParameters(content, idx)
					.AddParameter("@Hash", idx, SqliteType.Text, content.Hash)
					.AddParameter("@Size", idx, SqliteType.Integer, content.Size)
					.AddParameter("@SizeContent", idx, SqliteType.Integer, content.SizeContent)
					.AddParameter("@Compression", idx, SqliteType.Integer, content.Compression)
					.AddParameter("@Content", idx, SqliteType.Blob, bytes);
				newContent.Add(content);
				idx++;
			}

			if (newContent.Count > 0)
			{
				reader = cmd.ExecuteReader();
				foreach (var content in newContent)
				{
					results.Add(content.ReadRowId(reader));
					reader.NextResult();
				}
			}

			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}

		return results;
	}

	public Db.StoreVFilesResult SaveStoreVFilesState(StoreVFilesState state)
	{
		VerifyPermission(Permissions.Write);

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

		try
		{
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
				var dbDirectory = new Db.Directory { Path = info.VFilePath.Directory.Path }.Stamp();
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
			var reader = cmd.ExecuteReader();
			result.NewVFiles.ReadInsertedRowIds(reader);
			transaction.Commit();
		}
		catch (SqliteException)
		{
			transaction.Rollback();
			throw;
		}

		return result;
	}

	private static List<T> ReadEntities<T>(
		SqliteDataReader reader,
		string prefix,
		Func<SqliteDataReader, string, T> read)
	{
		var results = new List<T>();
		while (reader.Read())
		{
			results.Add(read(reader, prefix));
		}
		return results;
	}

	private static Db.VFile GetVFile(SqliteDataReader reader, string prefix = "")
	{
		return new Db.VFile
		{
			DirectoryRowId = reader.GetInt64(prefix + "DirectoryRowId"),
			FileContentRowId = reader.GetInt64(prefix + "FileContentRowId"),
			FileName = reader.GetString(prefix + "FileName"),
			FileExtension = reader.GetString(prefix + "FileExtension"),
			Versioned = reader.GetDateTimeOffsetNullable(prefix + "Versioned"),
			DeleteAt = reader.GetDateTimeOffsetNullable(prefix + "DeleteAt")
		}.GetEntityValues(reader, prefix);
	}

	private static Db.FileContent GetFileContent(SqliteDataReader reader, string prefix = "")
	{
		return new Db.FileContent
		{
			Hash = reader.GetString(prefix + "Hash"),
			Size = reader.GetInt64(prefix + "Size"),
			SizeContent = reader.GetInt64(prefix + "SizeContent"),
			Compression = reader.GetByte(prefix + "Compression")
		}.GetEntityValues(reader, prefix);
	}

	private static Db.Directory GetDirectory(SqliteDataReader reader, string prefix = "")
	{
		return new Db.Directory
		{
			Path = reader.GetString(prefix + "Path")
		}.GetEntityValues(reader, prefix);
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

	private static Db.FileContent ToDbFileContent(VFileInfo info)
	{
		return new Db.FileContent
		{
			Id = info.ContentId,
			Hash = info.Hash,
			Size = info.Size,
			SizeContent = info.SizeStored,
			Compression = (byte)info.Compression
		};
	}
}
