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
	ApplicationId	TEXT NOT NULL,
	Version			TEXT NOT NULL,
	LastClean		TEXT,
	LastUpdate		TEXT NOT NULL
);

-- defrag the database file.
VACUUM;
";

		DbUtil.ExecuteNonQuery(ConnectionString, sql);

		// seed root directory
		var dir = new Db.Directory
		{
			Name = "",
			Path = "/"
		}.Stamp();
		sql = @"
INSERT INTO Directory (
	Id,
	Name,
	Path,
	CreateTimestamp)
SELECT
	@Id,
	@Name,
	@Path,
	@CreateTimestamp
WHERE NOT EXISTS (SELECT 1 FROM Directory WHERE Path = @Path);
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.AddParameter("Id", SqliteType.Text, dir.Id.ToString())
			.AddParameter("Name", SqliteType.Text, dir.Name)
			.AddParameter("Path", SqliteType.Text, dir.Path)
			.AddParameter("CreateTimestamp", SqliteType.Text, dir.CreateTimestamp.ToDefaultString());
		connection.Open();
		cmd.ExecuteNonQuery();

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
DROP INDEX IF EXISTS Directory_ParentDirectoryRowId;
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
		if (permission == VFilePermission.All)
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

	public Db.UnreferencedFileContent GetUnreferencedFileContent()
	{
		VerifyPermission(Permissions.Read);

		var result = new Db.UnreferencedFileContent();

		using var connection = new SqliteConnection(ConnectionString);
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

		connection.Open();
		var reader = cmd.ExecuteReader();
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
		if (paths.IsEmpty()) return [];

		VerifyPermission(Permissions.Read);

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
				x.Directory.Path,
				x.FileName
			}).ToJson());

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.Add(parameter);

		connection.Open();
		var reader = cmd.ExecuteReader();
		return GetVFileModels(reader);
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
		VerifyPermission(Permissions.Write);

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildDeleteByRowId("VFile", "RowId", rowIds);
		connection.Open();
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
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildDeleteByRowId("Directory", "RowId", rowIds);
		connection.Open();
		cmd.ExecuteNonQuery();
	}

	public Db.UnreferencedFileContent DeleteUnreferencedFileContent()
	{
		VerifyPermission(Permissions.Write);
		var result = GetUnreferencedFileContent();
		DeleteFileContent(result.FileContentRowIds);
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

	public void SaveFileContent(List<(VFileInfo Info, byte[] Bytes)> contents)
	{
		// @note: this function does not return the Db.FileContents because
		// the result was not being used at all and this function is coded
		// in a very specific way for perfomance reasons that makes
		// it non-trivial to get the inserted RowIds.

		if (contents.Count == 0) return;

		VerifyPermission(Permissions.Write);

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
				// results.Add(existing);
				existingHashes.Add(existing.Hash);
			}

			var newContent = contents.Where(x => !existingHashes.Contains(x.Info.Hash))
				.Select(x => (ToDbFileContent(x.Info).Stamp(), x.Bytes))
				.ToList();

			if (newContent.Count > 0)
			{
				sql = $@"
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
	json_each(@FileContent);
";
				cmd = new SqliteCommand(sql, connection, transaction);
				cmd.AddParameter("@FileContent", SqliteType.Text,
					newContent.Select(x => new
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
				foreach (var item in newContent)
				{
					cmd.CommandText += $@"
INSERT INTO FileContentBlob (
	FileContentRowId,
	Content)
VALUES (
	(SELECT RowId FROM FileContent WHERE Hash = {DbUtil.ParameterName("Hash", idx)}),
	{DbUtil.ParameterName("Content", idx)});
";
					cmd.AddParameter("Hash", idx, SqliteType.Text, item.Item1.Hash)
						.AddParameter("Content", idx, SqliteType.Blob, item.Bytes);
					idx++;
				}

				cmd.ExecuteNonQuery();
			}

			transaction.Commit();
		}
		catch (Exception)
		{
			transaction.Rollback();
			throw;
		}
	}

	public void SaveStoreVFilesState(StoreVFilesState state)
	{
		// @note: no returned value for performance reasons
		// and because it was not used.

		VerifyPermission(Permissions.Write);

		// All state changes written transactionally.
		// order:
		//	Delete VFiles
		//  Update VFiles
		//  Insert new Directories
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
				state.NewVFiles.SelectMany(x => x.VFilePath.Directory.AllDirectoriesInPath())
					.DistinctBy(x => x.Path)
					.Select(dir =>
					{
						var dbDir = ToDbDirectory(dir).Stamp();
						return new
						{
							Id = dbDir.Id.ToString(),
							dbDir.Name,
							dbDir.Path,
							ParentPath = dir.ParentDirectory().Path,
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
	Id,	
	(SELECT RowId FROM Directory WHERE Path = q.Path),
	(SELECT RowId FROM FileContent WHERE Hash = q.Hash),
	FileName,
	FileExtension,
	Versioned,
	DeleteAt,
	CreateTimestamp
FROM
	q;
";
			cmd.AddParameter("@NewVFiles", SqliteType.Text,
				state.NewVFiles.Select(info =>
				{
					var dbVFile = ToDbVFile(info).Stamp();
					return new
					{
						Id = dbVFile.Id.ToString(),
						info.VFilePath.Directory.Path,
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
		catch (SqliteException)
		{
			transaction.Rollback();
			throw;
		}
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
