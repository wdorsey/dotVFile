using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;

namespace dotVFile;

internal class VFileDatabase
{
	public const string FileNameSuffix = ".vfile.db";

	public VFileDatabase(VFileDatabaseOptions opts)
	{
		Directory = opts.Directory;
		Version = opts.Version;
		Tools = opts.Tools;
		DatabaseFileName = $"{opts.Name}{FileNameSuffix}";
		DatabaseFilePath = new(Path.Combine(Directory, DatabaseFileName));
		ConnectionString = $"Data Source={DatabaseFilePath};";
		CreateDatabase();
	}

	public VFileDatabase(FileInfo databaseFilePath, string version, VFileTools tools)
	{
		Directory = databaseFilePath.DirectoryName ?? string.Empty;
		Version = version;
		Tools = tools;
		DatabaseFileName = databaseFilePath.Name;
		DatabaseFilePath = databaseFilePath.FullName;
		ConnectionString = $"Data Source={DatabaseFilePath};";
		CreateDatabase();
	}

	public string Directory { get; }
	public string Version { get; }
	public VFileTools Tools { get; }
	public string DatabaseFileName { get; }
	public string DatabaseFilePath { get; }
	public string ConnectionString { get; }

	public void CreateDatabase()
	{
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
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
CREATE UNIQUE INDEX IF NOT EXISTS VFile_CompositeId ON VFile(DirectoryRowId, FileName, Versioned);
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
	RowId					INTEGER NOT NULL UNIQUE,
	Id						TEXT NOT NULL,
	ParentDirectoryRowId	INTEGER NULL,
	Name					TEXT NOT NULL,
	Path					TEXT NOT NULL,
	CreateTimestamp			TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT),
	FOREIGN KEY(ParentDirectoryRowId) REFERENCES Directory(RowId)
);
CREATE UNIQUE INDEX IF NOT EXISTS Directory_Id ON Directory(Id);
CREATE INDEX IF NOT EXISTS		  Directory_ParentDirectoryRowId ON Directory(ParentDirectoryRowId);
CREATE UNIQUE INDEX IF NOT EXISTS Directory_Path ON Directory(Path);

CREATE TABLE IF NOT EXISTS SystemInfo (
	Version			TEXT NOT NULL
);

-- create SystemInfo row if not exists
INSERT INTO SystemInfo (
	Version)
SELECT
	@Version
WHERE NOT EXISTS (SELECT 1 FROM SystemInfo);

-- no where clause needed, only 1 row in the table
UPDATE SystemInfo
SET    Version = @Version;

-- seed root directory
INSERT INTO Directory (
	Id,
	Name,
	Path,
	CreateTimestamp)
SELECT
	@DirectoryId,
	'',
	'/',
	@CreateTimestamp
WHERE NOT EXISTS (SELECT 1 FROM Directory WHERE Path = '/');
";
			var cmd = new SqliteCommand(sql, connection, transaction);
			cmd.AddParameter("Version", SqliteType.Text, Version)
				.AddParameter("DirectoryId", SqliteType.Text, Guid.NewGuid().ToString())
				.AddParameter("CreateTimestamp", SqliteType.Text, DateTimeOffset.Now.ToDefaultString());
			cmd.ExecuteNonQuery();
			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	public void DropDatabase()
	{
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			const string sql = @"
-- indexes
DROP INDEX IF EXISTS VFile_Id;
DROP INDEX IF EXISTS VFile_CompositeId;
DROP INDEX IF EXISTS VFile_Versioned;
DROP INDEX IF EXISTS VFile_FileNameVersioned;
DROP INDEX IF EXISTS VFile_DeleteAt;
DROP INDEX IF EXISTS VFile_DirectoryRowId;
DROP INDEX IF EXISTS VFile_FileContentRowId;

DROP INDEX IF EXISTS FileContent_Id;
DROP INDEX IF EXISTS FileContent_Hash;

DROP INDEX IF EXISTS FileContentBlob_FileContentRowId;

DROP INDEX IF EXISTS Directory_Id;
DROP INDEX IF EXISTS Directory_ParentDirectoryRowId;
DROP INDEX IF EXISTS Directory_Path;

-- tables
DROP TABLE IF EXISTS VFile;
DROP TABLE IF EXISTS FileContentBlob;
DROP TABLE IF EXISTS FileContent;
DROP TABLE IF EXISTS Directory;
DROP TABLE IF EXISTS SystemInfo;
";
			var cmd = new SqliteCommand(sql, connection, transaction);
			cmd.ExecuteNonQuery();
			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	public void Vacuum()
	{
		// defrag database file. this is _very_ slow on large files.
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand("VACUUM;", connection);
		cmd.ExecuteNonQuery();
	}

	public Db.SystemInfo GetSystemInfo()
	{
		// this always assumes the row in SystemInfo exists.

		// SystemInfo only ever has 1 row
		const string sql = @"SELECT * FROM SystemInfo";
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		var reader = cmd.ExecuteReader();
		reader.Read();
		return new(reader.GetString("Version"));
	}

	public DateTimeOffset? GetVFileMinDeleteAt()
	{
		const string sql = @"SELECT MIN(DeleteAt) AS DeleteAt FROM VFile WHERE DeleteAt IS NOT NULL";
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		return cmd.ExecuteScalar().ConvertDateTimeOffsetNullable();
	}

	public Db.UnreferencedFileContent GetUnreferencedFileContent()
	{
		var result = new Db.UnreferencedFileContent();

		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(string.Empty, connection);

		cmd.CommandText += $@"
SELECT
	FileContent.RowId
FROM
	FileContent
	LEFT JOIN VFile f ON f.FileContentRowId = FileContent.RowId
WHERE
	f.RowId IS NULL;
";
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			result.FileContentRowIds.Add(reader.GetInt64("RowId"));
		}

		return result;
	}

	public List<Db.VFile> GetExpiredVFiles(DateTimeOffset cutoff)
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
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("@Cutoff", SqliteType.Text, cutoff.ToDefaultString());
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(GetVFile(reader));
		}

		return results;
	}

	/// <summary>
	/// Gets all Directories in path.
	/// </summary>
	public List<Db.Directory> GetDirectories(string path)
	{
		var results = new List<Db.Directory>();

		const string sql = @"
SELECT
	d.*
FROM
	Directory d
	INNER JOIN Directory p ON p.RowId = d.ParentDirectoryRowId
WHERE
	p.Path = @Path
";
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("Path", SqliteType.Text, path);
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(GetDirectory(reader));
		}

		return results;
	}

	/// <summary>
	/// Gets Directory at path and all subdirectories.
	/// Returns in order from root down through subdirectories.
	/// </summary>
	public List<Db.Directory> GetDirectoriesRecursive(string path)
	{
		var results = new List<Db.Directory>();

		const string sql = @"
;WITH RECURSIVE dirs AS (
	SELECT 
		* 
	FROM 
		Directory 
	WHERE 
		Path = @Path
	UNION ALL
	SELECT 
		Directory.* 
	FROM 
		dirs 
		INNER JOIN Directory ON Directory.ParentDirectoryRowId = dirs.RowId
)
SELECT * FROM dirs ORDER BY Path;
";
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("Path", SqliteType.Text, path);
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(GetDirectory(reader));
		}

		return results;
	}

	public List<Db.Directory> GetDirectories(List<long> rowIds)
	{
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var results = DbUtil.ExecuteGetById(
			connection,
			rowIds,
			"Directory",
			"RowId",
			"*",
			reader => ReadEntities(reader, GetDirectory));

		return results;
	}

	public List<Db.FileContent> GetFileContent()
	{
		const string sql = @"SELECT * FROM FileContent";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		var reader = cmd.ExecuteReader();
		return ReadEntities(reader, GetFileContent);
	}

	public List<Db.FileContent> GetFileContent(List<long> rowIds)
	{
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var results = DbUtil.ExecuteGetById(
			connection,
			rowIds,
			"FileContent",
			"RowId",
			"*",
			reader => ReadEntities(reader, GetFileContent));

		return results;
	}

	public List<Db.VFileModel> GetVFilesById(IEnumerable<Guid> ids)
	{
		if (ids.IsEmpty()) return [];

		var inClause = DbUtil.BuildInClause(ids.Select(x => x.ToString()), "Id", "VFile");
		var sql = GetVFilesSql(inClause.Sql);

		return ExecuteVFiles(sql, inClause.Parameters);
	}

	public List<Db.VFileModel> GetVFilesByDirectory(
		IEnumerable<string> directories,
		VersionQuery versionQuery)
	{
		if (directories.IsEmpty()) return [];

		var inClause = DbUtil.BuildInClause(directories, "Path", "Directory");
		var version = GetVersionedSql(versionQuery);
		var where = $"{inClause.Sql} AND {version}";
		var sql = GetVFilesSql(where);

		return ExecuteVFiles(sql, inClause.Parameters);
	}

	public List<Db.VFileModel> GetVFilesByFilePath(
		IEnumerable<VFilePath> paths,
		VersionQuery versionQuery)
	{
		if (paths.IsEmpty()) return [];

		var versionedSql = GetVersionedSql(versionQuery);
		var sql = $@"
;WITH q AS (
	SELECT 
		value ->> 'Path' AS Path, 
		value ->> 'FileName' AS FileName
	FROM 
		json_each(@Paths)
)
SELECT
	VFile.*
FROM
	VFile
	INNER JOIN FileContent ON FileContent.RowId = VFile.FileContentRowId
	INNER JOIN Directory ON Directory.RowId = VFile.DirectoryRowId
	INNER JOIN q ON q.FileName = VFile.FileName AND q.Path = Directory.Path
WHERE
	{versionedSql}
";
		var parameter = DbUtil.NewParameter(
			"@Paths",
			SqliteType.Text,
			paths.Select(x => new
			{
				x.VDirectory.Path,
				x.FileName
			}).ToJson());

		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.Add(parameter);
		var reader = cmd.ExecuteReader();
		return GetVFileModels(reader);
	}

	private List<Db.VFileModel> ExecuteVFiles(string sql, List<SqliteParameter> parameters)
	{
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddRange(parameters);
		var reader = cmd.ExecuteReader();
		return GetVFileModels(reader);
	}

	private List<Db.VFileModel> GetVFileModels(SqliteDataReader reader)
	{
		var results = new List<Db.VFileModel>();
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
	VFile.*
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
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			var cmd = new SqliteCommand(string.Empty, connection, transaction);
			cmd.BuildDeleteByRowId("VFile", "RowId", rowIds);
			cmd.ExecuteNonQuery();
			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	public void DeleteFileContent(List<long> rowIds)
	{
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
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			var cmd = new SqliteCommand(string.Empty, connection, transaction);
			cmd.BuildDeleteByRowId("Directory", "RowId", rowIds);
			cmd.ExecuteNonQuery();
			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	public Db.UnreferencedFileContent DeleteUnreferencedFileContent()
	{
		var result = GetUnreferencedFileContent();
		DeleteFileContent(result.FileContentRowIds);
		return result;
	}

	public List<Db.VFile> DeleteExpiredVFiles()
	{
		var vfiles = GetExpiredVFiles(DateTimeOffset.Now);
		DeleteVFiles([.. vfiles.Select(x => x.RowId)]);
		return vfiles;
	}

	public byte[] GetContentBytes(Db.FileContent content)
	{
		const string sql = $@"
SELECT
	Content
FROM
	FileContentBlob
WHERE
	FileContentRowId = @RowId;
";
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("@RowId", SqliteType.Integer, content.RowId);
		var reader = cmd.ExecuteReader();
		reader.Read();
		var result = reader.GetBytes("Content");

		return result;
	}

	public void SaveFileContent(List<(VFileInfo Info, byte[] Bytes)> contents)
	{
		// @note: this function does not return the Db.FileContents because
		// the result was not being used at all and this function is coded
		// in a very specific way for perfomance reasons that makes
		// it non-trivial to get the inserted RowIds.

		if (contents.Count == 0) return;

		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			var dbContent = contents.Select(x => (ToDbFileContent(x.Info).Stamp(), x.Bytes)).ToList();

			var sql = $@"
INSERT INTO FileContent (
	Id,
	Hash,
	Size,
	SizeContent,
	Compression,
	CreateTimestamp)
SELECT
	value ->> 'Id',
	value ->> 'Hash',
	value ->> 'Size',
	value ->> 'SizeContent',
	value ->> 'Compression',
	value ->> 'CreateTimestamp'
FROM
	json_each(@FileContent)
WHERE NOT EXISTS (SELECT 1 FROM FileContent WHERE Hash = value ->> 'Hash');
";
			var cmd = new SqliteCommand(sql, connection, transaction);
			cmd.AddParameter("@FileContent", SqliteType.Text,
				dbContent.Select(x => new
				{
					Id = x.Item1.Id.ToString(),
					x.Item1.Hash,
					x.Item1.Size,
					x.Item1.SizeContent,
					x.Item1.Compression,
					CreateTimestamp = x.Item1.CreateTimestamp.ToDefaultString()
				}).ToJson()!);

			// can't use json stuff when storing the blob bytes
			var idx = 0;
			foreach (var item in dbContent)
			{
				cmd.CommandText += $@"
INSERT INTO FileContentBlob (
	FileContentRowId,
	Content)
SELECT
	fc.RowId,
	{DbUtil.ParameterName("Content", idx)}
FROM
	FileContent fc
	LEFT JOIN FileContentBlob b ON b.FileContentRowId = fc.RowId
WHERE 
	Hash = {DbUtil.ParameterName("Hash", idx)}
	AND b.FileContentRowId IS NULL;
";
				cmd.AddParameter("Hash", idx, SqliteType.Text, item.Item1.Hash)
					.AddParameter("Content", idx, SqliteType.Blob, item.Bytes);
				idx++;
			}

			cmd.ExecuteNonQuery();

			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	public void SaveStoreState(StoreState state)
	{
		// @note: no returned value for performance reasons
		// and because it was not used.

		// All state changes written transactionally.
		// order:
		//	Delete VFiles
		//	Update VFiles
		//	Insert new Directories
		//	Insert new VFiles

		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			var cmd = new SqliteCommand(string.Empty, connection, transaction);

			// Delete VFiles
			cmd.BuildDeleteByRowId("VFile", "RowId", [.. state.DeleteVFiles.Select(x => x.RowId)]);

			// Update VFiles
			cmd.CommandText += @"
;WITH q AS (
	SELECT 
		value ->> 'Versioned' AS Versioned, 
		value ->> 'DeleteAt' AS DeleteAt,
		value ->> 'RowId' AS RowId
	FROM 
		json_each(@Updates)
)
UPDATE VFile
SET
	Versioned = q.Versioned,
	DeleteAt = q.DeleteAt
FROM
	q
WHERE
	VFile.RowId = q.RowId;
";
			cmd.AddParameter("@Updates", SqliteType.Text,
				state.UpdateVFiles.Select(vfile => new
				{
					Versioned = (vfile.Versioned?.ToDefaultString()).NullCoalesce(),
					DeleteAt = (vfile.DeleteAt?.ToDefaultString()).NullCoalesce(),
					vfile.RowId
				}).ToJson()!);

			// Insert Directories
			cmd.CommandText += @"
;WITH q AS (
	SELECT 
		value ->> 'Id' AS Id, 
		value ->> 'Name' AS Name,
		value ->> 'Path' AS Path,
		value ->> 'CreateTimestamp' AS CreateTimestamp
	FROM 
		json_each(@NewDirectories)
)
INSERT INTO Directory (
	Id,
	Name,
	Path,
	CreateTimestamp)
SELECT
	q.Id,
	q.Name,
	q.Path,
	q.CreateTimestamp
FROM
	q
	LEFT JOIN Directory d ON d.Path = q.Path
WHERE 
	d.RowId IS NULL;

;WITH q AS (
	SELECT 
		value ->> 'Path' AS Path,
		value ->> 'ParentPath' AS ParentPath
	FROM 
		json_each(@NewDirectories)
)
UPDATE Directory
SET
	ParentDirectoryRowId = p.RowId
FROM
	q
	INNER JOIN Directory p ON p.Path = q.ParentPath
WHERE
	Directory.Path = q.Path;
";
			cmd.AddParameter("@NewDirectories", SqliteType.Text,
				state.NewVFiles.SelectMany(x => x.VFilePath.VDirectory.AllDirectoriesInPath())
					.DistinctBy(x => x.Path)
					.Select(dir =>
					{
						var dbDir = ToDbDirectory(dir).Stamp();
						return new
						{
							Id = dbDir.Id.ToString(),
							dbDir.Name,
							dbDir.Path,
							ParentPath = dir.ParentDirectory()?.Path ?? string.Empty,
							CreateTimestamp = dbDir.CreateTimestamp.ToDefaultString()
						};
					}).ToJson()!);

			// Insert VFiles
			cmd.CommandText += @"
;WITH q AS (
	SELECT 
		value ->> 'Id' AS Id, 
		value ->> 'Path' AS Path,
		value ->> 'Hash' AS Hash,
		value ->> 'FileName' AS FileName,
		value ->> 'FileExtension' AS FileExtension,
		value ->> 'Versioned' AS Versioned,
		value ->> 'DeleteAt' AS DeleteAt,
		value ->> 'CreateTimestamp' AS CreateTimestamp
	FROM 
		json_each(@NewVFiles)
)
INSERT INTO VFile (
	Id,
	DirectoryRowId,
	FileContentRowId,
	FileName,
	FileExtension,
	Versioned,
	DeleteAt,
	CreateTimestamp)
SELECT
	q.Id,	
	Directory.RowId,
	FileContent.RowId,
	q.FileName,
	q.FileExtension,
	q.Versioned,
	q.DeleteAt,
	q.CreateTimestamp
FROM
	q
	INNER JOIN Directory ON Directory.Path = q.Path
	INNER JOIN FileContent ON FileContent.Hash = q.Hash
WHERE NOT EXISTS (
	SELECT 1 
	FROM 
		VFile 
	WHERE 
		VFile.DirectoryRowId = Directory.RowId
		AND VFile.FileName = q.FileName
		AND VFile.Versioned = q.Versioned);
";
			cmd.AddParameter("@NewVFiles", SqliteType.Text,
				state.NewVFiles.Select(info =>
				{
					var dbVFile = ToDbVFile(info).Stamp();
					return new
					{
						Id = dbVFile.Id.ToString(),
						info.VFilePath.VDirectory.Path,
						info.Hash,
						dbVFile.FileName,
						dbVFile.FileExtension,
						Versioned = (dbVFile.Versioned?.ToDefaultString()).NullCoalesce(),
						DeleteAt = (dbVFile.DeleteAt?.ToDefaultString()).NullCoalesce(),
						CreateTimestamp = dbVFile.CreateTimestamp.ToDefaultString()
					};
				}).ToJson()!);


			cmd.ExecuteNonQuery();

			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	private static List<T> ReadEntities<T>(
		SqliteDataReader reader,
		Func<SqliteDataReader, T> read)
	{
		var results = new List<T>();
		while (reader.Read())
		{
			results.Add(read(reader));
		}
		return results;
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
			ParentDirectoryRowId = reader.GetInt64Nullable("ParentDirectoryRowId"),
			Name = reader.GetString("Name"),
			Path = reader.GetString("Path")
		}.GetEntityValues(reader);
	}

	private static string GetVersionedSql(VersionQuery versionQuery)
	{
		return versionQuery switch
		{
			VersionQuery.Latest => "VFile.Versioned IS NULL",
			VersionQuery.Versions => "VFile.Versioned IS NOT NULL",
			VersionQuery.Both => "1=1",
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

	private static Db.Directory ToDbDirectory(VDirectory dir)
	{
		return new Db.Directory
		{
			Id = Guid.NewGuid(),
			Name = dir.Name,
			Path = dir.Path
		};
	}
}
