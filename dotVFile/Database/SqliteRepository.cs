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
	Hash			TEXT NOT NULL UNIQUE,
	FullPath		TEXT NOT NULL,
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

	public void SaveVFileInfo(Db.VFileInfo vfile)
	{
		const string sql = $@"
INSERT INTO VFileInfo(
	Hash,
	FullPath,
	RelativePath,
	FileName,
	Extension,
	Size,
	Version,
	DeleteAt,
	CreationTime,
	CreateTimestamp)
VALUES (
	@Hash, 
	@FullPath, 
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
		cmd.Parameters.AddWithValue("@Hash", vfile.Hash);
		cmd.Parameters.AddWithValue("@FullPath", vfile.FullPath);
		cmd.Parameters.AddWithValue("@RelativePath", vfile.RelativePath);
		cmd.Parameters.AddWithValue("@FileName", vfile.FileName);
		cmd.Parameters.AddWithValue("@Extension", vfile.Extension);
		cmd.Parameters.AddWithValue("@Size", vfile.Size);
		cmd.Parameters.AddWithValue("@Version", NullMaybe(vfile.Version));
		cmd.Parameters.AddWithValue("@DeleteAt", NullMaybe(vfile.DeleteAt));
		cmd.Parameters.AddWithValue("@CreationTime", vfile.CreationTime);
		cmd.Parameters.AddWithValue("@CreateTimestamp", vfile.CreateTimestamp);
		connection.Open();
		vfile.Id = (long)(cmd.ExecuteScalar() ?? 0);
	}

	private static object NullMaybe(object? value)
	{
		return value ?? DBNull.Value;
	}
}
