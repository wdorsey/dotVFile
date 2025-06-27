using Microsoft.Data.Sqlite;

namespace dotVFile;

internal class SqliteRepository(string dbFilePath)
{
	public string ConnectionString { get; } = $"Data Source={dbFilePath};";

	public void CreateDatabaseSchema()
	{
		const string sql = @"
CREATE TABLE IF NOT EXISTS VFileInfo (
	Id				INTEGER NOT NULL UNIQUE,
	FileId			TEXT NOT NULL,
	Hash			TEXT NOT NULL UNIQUE,
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
CREATE INDEX IF NOT EXISTS VFileInfo_Hash ON VFileInfo(Hash);
CREATE INDEX IF NOT EXISTS VFileInfo_Version ON VFileInfo(Version) WHERE Version IS NOT NULL;
CREATE INDEX IF NOT EXISTS VFileInfo_DeleteAt ON VFileInfo(DeleteAt) WHERE DeleteAt IS NOT NULL;

CREATE TABLE IF NOT EXISTS VFileDataInfo (
	Id				INTEGER NOT NULL UNIQUE,
	Hash			TEXT NOT NULL UNIQUE,
	Directory		TEXT NOT NULL,
	FileName		TEXT NOT NULL,
	Size			INTEGER NOT NULL,
	SizeOnDisk		INTEGER NOT NULL,
	Compression		INTEGER NOT NULL,
	CreationTime	TEXT NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(Id AUTOINCREMENT)
);
CREATE INDEX IF NOT EXISTS VFileDataInfo_Hash ON VFileDataInfo(Hash);

CREATE TABLE IF NOT EXISTS VFileMap (
	Id				INTEGER NOT NULL UNIQUE,
	Hash			TEXT NOT NULL,
	VFileInfoId		INTEGER NOT NULL,
	VFileDataInfoId	INTEGER NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(Id AUTOINCREMENT),
	FOREIGN KEY(VFileInfoId) REFERENCES VFileInfo(Id),
	FOREIGN KEY(VFileDataInfoId) REFERENCES VFileDataInfo(Id)
);
CREATE INDEX IF NOT EXISTS VFileMap_Hash ON VFileMap(Hash);
CREATE INDEX IF NOT EXISTS VFileMap_VFileInfoId ON VFileMap(VFileInfoId);
CREATE INDEX IF NOT EXISTS VFileMap_VFileDataInfoId ON VFileMap(VFileDataInfoId);
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		cmd.ExecuteNonQuery();
	}

	public Db.VFileInfo? GetVFileInfoByFileId(string fileId)
	{
		const string sql = @"
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
	AND Version IS NULL
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@FileId", fileId);
		connection.Open();
		var reader = cmd.ExecuteReader();
		if (reader.Read())
		{
			var info = new Db.VFileInfo(
				reader["FileId"].ToString()!,
				reader["Hash"].ToString()!,
				reader["RelativePath"].ToString()!,
				reader["FileName"].ToString()!,
				reader["Extension"].ToString()!,
				Convert.ToInt64(reader["Size"]),
				reader["Version"]?.ToString(),
				DbUtil.ConvertDateTimeOffsetNullable(reader["DeleteAt"]),
				DbUtil.ConvertDateTimeOffset(reader["CreationTime"].ToString()!))
			{
				Id = Convert.ToInt64(reader["Id"]),
				CreateTimestamp = DbUtil.ConvertDateTimeOffset(reader["CreateTimestamp"].ToString()!)
			};

			return info;
		}

		return null;
	}

	public void InsertVFileInfo(Db.VFileInfo info)
	{
		const string sql = @"
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
	@FileId, 
	@Hash, 
	@RelativePath,
	@FileName,
	@Extension,
	@Size,
	@Version,
	@DeleteAt,
	@CreationTime,
	@CreateTimestamp);
SELECT last_insert_rowid();
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@FileId", info.FileId);
		cmd.Parameters.AddWithValue("@Hash", info.Hash);
		cmd.Parameters.AddWithValue("@RelativePath", info.RelativePath);
		cmd.Parameters.AddWithValue("@FileName", info.FileName);
		cmd.Parameters.AddWithValue("@Extension", info.Extension);
		cmd.Parameters.AddWithValue("@Size", info.Size);
		cmd.Parameters.AddWithValue("@Version", DbUtil.NullCoalesce(info.Version));
		cmd.Parameters.AddWithValue("@DeleteAt", DbUtil.NullCoalesce(info.DeleteAt));
		cmd.Parameters.AddWithValue("@CreationTime", info.CreationTime);
		cmd.Parameters.AddWithValue("@CreateTimestamp", info.CreateTimestamp);
		connection.Open();
		info.Id = (long)(cmd.ExecuteScalar() ?? 0);
	}

	public void InsertVFileDataInfo(Db.VFileDataInfo info)
	{
		const string sql = @"
INSERT INTO VFileDataInfo(
	Hash,
	Directory,
	FileName,
	Size,
	SizeOnDisk,
	Compression,
	CreationTime,
	CreateTimestamp)
VALUES (
	@Hash,
	@Directory,
	@FileName,
	@Size,
	@SizeOnDisk,
	@Compression,
	@CreationTime,
	@CreateTimestamp);
SELECT last_insert_rowid();
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@Hash", info.Hash);
		cmd.Parameters.AddWithValue("@Directory", info.Directory);
		cmd.Parameters.AddWithValue("@FileName", info.FileName);
		cmd.Parameters.AddWithValue("@Size", info.Size);
		cmd.Parameters.AddWithValue("@SizeOnDisk", info.SizeOnDisk);
		cmd.Parameters.AddWithValue("@Compression", info.Compression);
		cmd.Parameters.AddWithValue("@CreationTime", info.CreationTime);
		cmd.Parameters.AddWithValue("@CreateTimestamp", info.CreateTimestamp);
		connection.Open();
		info.Id = (long)(cmd.ExecuteScalar() ?? 0);
	}

	public void InsertVFileMap(Db.VFileMap map)
	{
		const string sql = @"
INSERT INTO VFileMap(
	Hash,
	VFileInfoId,
	VFileDataInfoId,
	CreateTimestamp)
VALUES (
	@Hash,
	@VFileInfoId,
	@VFileDataInfoId,
	@CreateTimestamp);
SELECT last_insert_rowid();
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@Hash", map.Hash);
		cmd.Parameters.AddWithValue("@VFileInfoId", map.VFileInfoId);
		cmd.Parameters.AddWithValue("@VFileDataInfoId", map.VFileDataInfoId);
		cmd.Parameters.AddWithValue("@CreateTimestamp", map.CreateTimestamp);
		connection.Open();
		map.Id = (long)(cmd.ExecuteScalar() ?? 0);
	}
}
