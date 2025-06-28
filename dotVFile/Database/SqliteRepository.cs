using Microsoft.Data.Sqlite;

namespace dotVFile;

internal class SqliteRepository(string dbFilePath)
{
	public string DatabaseFilePath { get; } = dbFilePath;
	public string ConnectionString { get; } = $"Data Source={dbFilePath};";

	public void CreateDatabase()
	{
		const string sql = @"
CREATE TABLE IF NOT EXISTS VFileInfo (
	Id				INTEGER NOT NULL UNIQUE,
	FileId			TEXT NOT NULL,
	Hash			TEXT NOT NULL,
	RelativePath	TEXT NOT NULL,
	FileName		TEXT NOT NULL,
	Extension		TEXT,
	Size			INTEGER NOT NULL,
	Version			TEXT,
	DeleteAt		TEXT,
	CreationTime	TEXT NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(Id AUTOINCREMENT)
);
CREATE INDEX IF NOT EXISTS VFileInfo_FileId ON VFileInfo(FileId);
CREATE INDEX IF NOT EXISTS VFileInfo_FileIdLatest ON VFileInfo(FileId, Version) WHERE VERSION IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS VFileInfo_FileIdVersion ON VFileInfo(FileId, Version);
CREATE INDEX IF NOT EXISTS VFileInfo_Hash ON VFileInfo(Hash);
CREATE INDEX IF NOT EXISTS VFileInfo_Version ON VFileInfo(Version) WHERE Version IS NOT NULL;
CREATE INDEX IF NOT EXISTS VFileInfo_DeleteAt ON VFileInfo(DeleteAt) WHERE DeleteAt IS NOT NULL;

CREATE TABLE IF NOT EXISTS VFileDataInfo (
	Id				INTEGER NOT NULL UNIQUE,
	Hash			TEXT NOT NULL UNIQUE,
	Size			INTEGER NOT NULL,
	SizeOnDisk		INTEGER NOT NULL,
	Compression		INTEGER NOT NULL,
	CreationTime	TEXT NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(Id AUTOINCREMENT)
);
CREATE INDEX IF NOT EXISTS VFileDataInfo_Hash ON VFileDataInfo(Hash);

CREATE TABLE IF NOT EXISTS VFile (
	Id				INTEGER NOT NULL UNIQUE,
	VFileDataInfoId	INTEGER NOT NULL,
	File			BLOB NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(Id AUTOINCREMENT),
	FOREIGN KEY(VFileDataInfoId) REFERENCES VFileDataInfo(Id)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFile_VFileDataInfoId ON VFile(VFileDataInfoId);
";
		SqliteUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DropDatabase()
	{
		const string sql = @"
-- indexes
DROP INDEX IF EXISTS VFileInfo_FileId;
DROP INDEX IF EXISTS VFileInfo_FileIdLatest;
DROP INDEX IF EXISTS VFileInfo_FileIdVersion;
DROP INDEX IF EXISTS VFileInfo_Hash;
DROP INDEX IF EXISTS VFileInfo_Version;
DROP INDEX IF EXISTS VFileInfo_DeleteAt;
DROP INDEX IF EXISTS VFileDataInfo_Hash;
DROP INDEX IF EXISTS VFile_VFileDataInfoId;

-- tables
DROP TABLE IF EXISTS VFileInfo;
DROP TABLE IF EXISTS VFile;
DROP TABLE IF EXISTS VFileDataInfo;
";
		SqliteUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DeleteDatabase()
	{
		SqliteConnection.ClearAllPools();
		Util.DeleteFile(DatabaseFilePath);
	}

	public List<Db.VFileInfo> GetVFileInfoByFileId(string fileId, Db.VFileInfoVersionQuery versionQuery)
	{
		string sql = $@"
SELECT 
	Id,
	FileId,
	Hash,
	RelativePath,
	FileName,
	Extension,
	Size,
	Version,
	DeleteAt,
	CreationTime,
	CreateTimestamp
FROM 
	VFileInfo 
WHERE 
	FileId = @FileId
";
		switch (versionQuery)
		{
			case Db.VFileInfoVersionQuery.Latest:
				sql += " AND Version IS NULL";
				break;
			case Db.VFileInfoVersionQuery.Versions:
				sql += " AND Version IS NOT NULL";
				break;
				// VFileInfoVersionQuery.Both does nothing
		}

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@FileId", fileId);
		connection.Open();
		return ExecuteVFileInfo(cmd);
	}

	private static List<Db.VFileInfo> ExecuteVFileInfo(SqliteCommand cmd)
	{
		var infos = new List<Db.VFileInfo>();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			infos.Add(new Db.VFileInfo(
				reader["FileId"].ToString()!,
				reader["Hash"].ToString()!,
				reader["RelativePath"].ToString()!,
				reader["FileName"].ToString()!,
				reader["Extension"].ToString()!,
				Convert.ToInt32(reader["Size"]),
				reader["Version"]?.ToString(),
				DbUtil.ConvertDateTimeOffsetNullable(reader["DeleteAt"]),
				DbUtil.ConvertDateTimeOffset(reader["CreationTime"].ToString()!))
				.ReadEntityValues(reader));
		}

		return infos;
	}

	public Db.VFileDataInfo? GetVFileDataInfoByFileId(string fileId)
	{
		const string sql = @"
SELECT
	di.Id,
	di.Hash,
	di.Size,
	di.SizeOnDisk,
	di.Compression,
	di.CreationTime,
	di.CreateTimestamp
FROM 
	VFileInfo fi
	INNER JOIN VFileDataInfo di ON di.Hash = fi.Hash
WHERE 
	fi.FileId = @FileId
	AND fi.Version IS NULL
LIMIT 1
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@FileId", fileId);
		connection.Open();
		return ExecuteVFileDataInfo(cmd).SingleOrDefault();
	}

	public Db.VFileDataInfo? GetVFileDataInfoByHash(string hash)
	{
		const string sql = @"
SELECT 
	Id,
	Hash,
	Size,
	SizeOnDisk,
	Compression,
	CreationTime,
	CreateTimestamp
FROM 
	VFileDataInfo 
WHERE 
	Hash = @Hash
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@Hash", hash);
		connection.Open();
		return ExecuteVFileDataInfo(cmd).SingleOrDefault();
	}

	private static List<Db.VFileDataInfo> ExecuteVFileDataInfo(SqliteCommand cmd)
	{
		var infos = new List<Db.VFileDataInfo>();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			infos.Add(new Db.VFileDataInfo(
				reader["Hash"].ToString()!,
				Convert.ToInt32(reader["Size"]),
				Convert.ToInt32(reader["SizeOnDisk"]),
				Convert.ToByte(reader["Compression"]),
				DbUtil.ConvertDateTimeOffset(reader["CreationTime"].ToString()!))
				.ReadEntityValues(reader));
		}

		return infos;
	}

	public Db.VFile? GetVFileByFileId(string fileId)
	{
		const string sql = @"
SELECT
	f.Id,
	f.VFileDataInfoId,
	f.File,
	f.CreateTimestamp
FROM
	VFile f
	INNER JOIN VFileDataInfo di ON di.Id = f.VFileDataInfoId
	INNER JOIN VFileInfo fi ON fi.Hash = di.Hash
WHERE
	fi.FileId = @FileId
	AND fi.Version IS NULL
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@FileId", fileId);
		connection.Open();
		var reader = cmd.ExecuteReader();
		if (reader.Read())
		{
			var file = new Db.VFile((byte[])reader["File"]).ReadEntityValues(reader);
			file.VFileDataInfoId = Convert.ToInt64(reader["VFileDataInfoId"]);
			return file;
		}

		return null;
	}

	public void InsertVFileInfo(List<Db.VFileInfo> rows)
	{
		SqliteUtil.ExecuteInsert(
			ConnectionString,
			rows,
			(cmd, rows, i) => SqliteUtil.VFileInfoInsert(cmd, rows, i));
	}

	public void InsertVFileData(List<Db.VFileDataInfo> infos, List<Db.VFile> files)
	{
		if (infos.Count != files.Count)
			throw new Exception("infos.Count != files.Count");

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.VFileDataInsert(infos, files, 0);
		connection.Open();
		var reader = cmd.ExecuteReader();
		for (var i = 0; i < infos.Count; i++)
		{
			if (reader.Read())
			{
				var info = infos[i];
				var file = files[i];
				info.Id = Convert.ToInt64(reader["Id"]);
				file.VFileDataInfoId = info.Id;
				reader.NextResult();
				reader.Read();
				file.Id = Convert.ToInt64(reader["Id"]);
				reader.NextResult();
			}
		}
	}
}

internal static class SqliteUtil
{
	private const string SelectInsertedId = " SELECT last_insert_rowid() AS Id; ";

	public static T ReadEntityValues<T>(this T entity, SqliteDataReader reader) where T : Db.Entity
	{
		entity.Id = Convert.ToInt64(reader["Id"]);
		entity.CreateTimestamp = DbUtil.ConvertDateTimeOffset(reader["CreateTimestamp"].ToString()!);
		return entity;
	}

	public static void ExecuteNonQuery(string connectionString, string sql)
	{
		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		cmd.ExecuteNonQuery();
	}

	public static void ExecuteInsert<T>(
		string connectionString,
		List<T> rows,
		Action<SqliteCommand, List<T>, int> generateInsert)
		where T : Db.Entity
	{
		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		generateInsert(cmd, rows, 0);
		connection.Open();
		cmd.ExecuteInsert(rows);
	}

	public static void ExecuteInsert<T>(this SqliteCommand cmd, List<T> rows) where T : Db.Entity
	{
		var reader = cmd.ExecuteReader();
		foreach (var row in rows)
		{
			if (reader.Read())
			{
				row.Id = Convert.ToInt64(reader["Id"]);
				reader.NextResult();
			}
		}
	}

	public static SqliteCommand VFileInfoInsert(this SqliteCommand cmd, Db.VFileInfo row, int pIndex)
	{
		cmd.CommandText += $@"
INSERT INTO VFileInfo(
	FileId,
	Hash,
	RelativePath,
	FileName,
	Extension,
	Size,
	Version,
	DeleteAt,
	CreationTime,
	CreateTimestamp)
VALUES (
	@FileId_{pIndex}, 
	@Hash_{pIndex}, 
	@RelativePath_{pIndex},
	@FileName_{pIndex},
	@Extension_{pIndex},
	@Size_{pIndex},
	@Version_{pIndex},
	@DeleteAt_{pIndex},
	@CreationTime_{pIndex},
	@CreateTimestamp_{pIndex});
{SelectInsertedId}
";
		cmd.Parameters.AddWithValue($"@FileId_{pIndex}", row.FileId);
		cmd.Parameters.AddWithValue($"@Hash_{pIndex}", row.Hash);
		cmd.Parameters.AddWithValue($"@RelativePath_{pIndex}", row.RelativePath);
		cmd.Parameters.AddWithValue($"@FileName_{pIndex}", row.FileName);
		cmd.Parameters.AddWithValue($"@Extension_{pIndex}", row.Extension);
		cmd.Parameters.AddWithValue($"@Size_{pIndex}", row.Size);
		cmd.Parameters.AddWithValue($"@Version_{pIndex}", DbUtil.NullCoalesce(row.Version));
		cmd.Parameters.AddWithValue($"@DeleteAt_{pIndex}", DbUtil.NullCoalesce(row.DeleteAt));
		cmd.Parameters.AddWithValue($"@CreationTime_{pIndex}", row.CreationTime);
		cmd.Parameters.AddWithValue($"@CreateTimestamp_{pIndex}", row.CreateTimestamp);

		return cmd;
	}

	public static SqliteCommand VFileInfoInsert(this SqliteCommand cmd, List<Db.VFileInfo> rows, int pIndex)
	{
		foreach (var row in rows)
		{
			cmd.VFileInfoInsert(row, pIndex++);
		}

		return cmd;
	}

	public static SqliteCommand VFileDataInsert(this SqliteCommand cmd, Db.VFileDataInfo info, Db.VFile file, int pIndex)
	{
		cmd.CommandText += $@"
INSERT INTO VFileDataInfo(
	Hash,
	Size,
	SizeOnDisk,
	Compression,
	CreationTime,
	CreateTimestamp)
VALUES (
	@Hash_{pIndex},
	@Size_{pIndex},
	@SizeOnDisk_{pIndex},
	@Compression_{pIndex},
	@CreationTime_{pIndex},
	@InfoCreateTimestamp_{pIndex});
{SelectInsertedId}

INSERT INTO VFile(
	VFileDataInfoId,
	File,
	CreateTimestamp)
VALUES (
	last_insert_rowid(),
	@File_{pIndex},
	@FileCreateTimestamp_{pIndex});
{SelectInsertedId}
";
		cmd.Parameters.AddWithValue($"@Hash_{pIndex}", info.Hash);
		cmd.Parameters.AddWithValue($"@Size_{pIndex}", info.Size);
		cmd.Parameters.AddWithValue($"@SizeOnDisk_{pIndex}", info.SizeOnDisk);
		cmd.Parameters.AddWithValue($"@Compression_{pIndex}", info.Compression);
		cmd.Parameters.AddWithValue($"@CreationTime_{pIndex}", info.CreationTime);
		cmd.Parameters.AddWithValue($"@InfoCreateTimestamp_{pIndex}", info.CreateTimestamp);
		cmd.Parameters.AddWithValue($"@File_{pIndex}", file.File);
		cmd.Parameters.AddWithValue($"@FileCreateTimestamp_{pIndex}", file.CreateTimestamp);

		return cmd;
	}

	public static SqliteCommand VFileDataInsert(this SqliteCommand cmd, List<Db.VFileDataInfo> infos, List<Db.VFile> files, int pIndex)
	{
		for (var i = 0; i < infos.Count; i++)
		{
			cmd.VFileDataInsert(infos[i], files[i], pIndex++);
		}

		return cmd;
	}
}
