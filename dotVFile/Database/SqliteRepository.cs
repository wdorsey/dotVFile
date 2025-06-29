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
	RowId					INTEGER NOT NULL UNIQUE,
	Id						TEXT NOT NULL,
	VFileContentInfoRowId	INTEGER NOT NULL,
	FileId					TEXT NOT NULL,
	RelativePath			TEXT NOT NULL,
	FileName				TEXT NOT NULL,
	Extension				TEXT,
	Versioned				TEXT,
	DeleteAt				TEXT,
	CreationTime			TEXT NOT NULL,
	CreateTimestamp			TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT),
	FOREIGN KEY(VFileContentInfoRowId) REFERENCES VFileContentInfo(RowId)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFileInfo_Id ON VFileInfo(Id);
CREATE INDEX IF NOT EXISTS VFileInfo_VFileContentInfoRowId ON VFileInfo(VFileContentInfoRowId);
CREATE INDEX IF NOT EXISTS VFileInfo_FileId ON VFileInfo(FileId);
CREATE INDEX IF NOT EXISTS VFileInfo_FileIdLatest ON VFileInfo(FileId, Versioned) WHERE Versioned IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS VFileInfo_FileIdVersioned ON VFileInfo(FileId, Versioned);
CREATE INDEX IF NOT EXISTS VFileInfo_DeleteAt ON VFileInfo(DeleteAt) WHERE DeleteAt IS NOT NULL;

CREATE TABLE IF NOT EXISTS VFileContentInfo (
	RowId			INTEGER NOT NULL UNIQUE,
	Id				TEXT NOT NULL,
	Hash			TEXT NOT NULL UNIQUE,
	Size			INTEGER NOT NULL,
	SizeStored		INTEGER NOT NULL,
	Compression		INTEGER NOT NULL,
	CreationTime	TEXT NOT NULL,
	CreateTimestamp	TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFileContentInfo_Id ON VFileContentInfo(Id);
CREATE INDEX IF NOT EXISTS VFileContentInfo_Hash ON VFileContentInfo(Hash);

CREATE TABLE IF NOT EXISTS VFileContent (
	RowId					INTEGER NOT NULL UNIQUE,
	Id						TEXT NOT NULL,
	VFileContentInfoRowId	INTEGER NOT NULL,
	Content					BLOB NOT NULL,
	CreateTimestamp			TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT),
	FOREIGN KEY(VFileContentInfoRowId) REFERENCES VFileContentInfo(RowId)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFileContent_Id ON VFileContent(Id);
CREATE UNIQUE INDEX IF NOT EXISTS VFileContent_VFileContentInfoRowId ON VFileContent(VFileContentInfoRowId);
";
		SqliteUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DropDatabase()
	{
		const string sql = @"
-- indexes
DROP INDEX IF EXISTS VFileInfo_Id;
DROP INDEX IF EXISTS VFileInfo_VFileContentInfoRowId;
DROP INDEX IF EXISTS VFileInfo_FileId;
DROP INDEX IF EXISTS VFileInfo_FileIdLatest;
DROP INDEX IF EXISTS VFileInfo_FileIdVersioned;
DROP INDEX IF EXISTS VFileInfo_DeleteAt;
DROP INDEX IF EXISTS VFileContentInfo_Id;
DROP INDEX IF EXISTS VFileContentInfo_Hash;
DROP INDEX IF EXISTS VFileContent_Id;
DROP INDEX IF EXISTS VFileContent_VFileContentInfoRowId;

-- tables
DROP TABLE IF EXISTS VFileInfo;
DROP TABLE IF EXISTS VFileContent;
DROP TABLE IF EXISTS VFileContentInfo;
";
		SqliteUtil.ExecuteNonQuery(ConnectionString, sql);
	}

	public void DeleteDatabase()
	{
		SqliteConnection.ClearAllPools();
		Util.DeleteFile(DatabaseFilePath);
	}

	public void InsertVFileInfo(List<Db.VFileInfo> rows)
	{
		SqliteUtil.ExecuteInsert(
			ConnectionString,
			rows,
			(cmd, rows, i) => SqliteUtil.VFileInfoInsert(cmd, rows, i));
	}

	public void InsertVFileContent(List<Db.VFileContentInfo> infos, List<Db.VFileContent> contents)
	{
		if (infos.Count != contents.Count)
			throw new Exception("infos.Count != contents.Count");

		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.VFileDataInsert(infos, contents, 0);
		connection.Open();
		var reader = cmd.ExecuteReader();
		for (var i = 0; i < infos.Count; i++)
		{
			reader.Read();
			var info = infos[i];
			var content = contents[i];
			info.RowId = Convert.ToInt64(reader["RowId"]);
			content.VFileContentInfoRowId = info.RowId;
			reader.NextResult();
			reader.Read();
			content.RowId = Convert.ToInt64(reader["RowId"]);
			reader.NextResult();
		}
	}
}

internal static class SqliteUtil
{
	private const string SelectInsertedRowId = " SELECT last_insert_rowid() AS RowId; ";

	public static T ReadEntityValues<T>(this T entity, SqliteDataReader reader) where T : Db.Entity
	{
		entity.RowId = Convert.ToInt64(reader["RowId"]);
		entity.Id = Guid.Parse(reader["Id"].ToString()!);
		entity.CreateTimestamp = DateTimeOffset.Parse(reader["CreateTimestamp"].ToString()!);
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
			reader.Read();
			row.RowId = Convert.ToInt64(reader["RowId"]);
			reader.NextResult();
		}
	}

	public static SqliteCommand VFileInfoInsert(this SqliteCommand cmd, Db.VFileInfo info, int pIndex)
	{
		cmd.CommandText += $@"
INSERT INTO VFileInfo(
	Id,
	FileId,
	Hash,
	RelativePath,
	FileName,
	Extension,
	Versioned,
	DeleteAt,
	CreationTime,
	CreateTimestamp)
VALUES (
	@Id_{pIndex},
	@FileId_{pIndex}, 
	@Hash_{pIndex}, 
	@RelativePath_{pIndex},
	@FileName_{pIndex},
	@Extension_{pIndex},
	@Versioned_{pIndex},
	@DeleteAt_{pIndex},
	@CreationTime_{pIndex},
	@CreateTimestamp_{pIndex});
{SelectInsertedId}
";
		cmd.Parameters.AddWithValue($"@Id_{pIndex}", info.Id.ToString());
		cmd.Parameters.AddWithValue($"@FileId_{pIndex}", info.FileId);
		cmd.Parameters.AddWithValue($"@Hash_{pIndex}", info.Hash);
		cmd.Parameters.AddWithValue($"@RelativePath_{pIndex}", info.RelativePath);
		cmd.Parameters.AddWithValue($"@FileName_{pIndex}", info.FileName);
		cmd.Parameters.AddWithValue($"@Extension_{pIndex}", info.Extension);
		cmd.Parameters.AddWithValue($"@Versioned_{pIndex}", DbUtil.NullCoalesce(info.Versioned.ToDefaultString()));
		cmd.Parameters.AddWithValue($"@DeleteAt_{pIndex}", DbUtil.NullCoalesce(info.DeleteAt.ToDefaultString()));
		cmd.Parameters.AddWithValue($"@CreationTime_{pIndex}", info.CreationTime.ToDefaultString());
		cmd.Parameters.AddWithValue($"@CreateTimestamp_{pIndex}", info.CreateTimestamp.ToDefaultString());

		return cmd;
	}

	public static SqliteCommand VFileInfoInsert(this SqliteCommand cmd, List<Db.VFileInfo> infos, int pIndex)
	{
		foreach (var info in infos)
		{
			cmd.VFileInfoInsert(info, pIndex++);
		}

		return cmd;
	}

	public static SqliteCommand VFileInfoUpdate(this SqliteCommand cmd, Db.VFileInfo info, int pIndex)
	{
		// only update-able columns are Versioned and DeleteAt
		cmd.CommandText += $@"
UPDATE VFileInfo
SET 
	Versioned = @Versioned_{pIndex},
	DeleteAt = @DeleteAt_{pIndex}
WHERE
	Id = @Id_{pIndex}
";
		cmd.Parameters.AddWithValue($"@Versioned_{pIndex}", DbUtil.NullCoalesce(info.Versioned.ToDefaultString()));
		cmd.Parameters.AddWithValue($"@DeleteAt_{pIndex}", DbUtil.NullCoalesce(info.DeleteAt.ToDefaultString()));
		cmd.Parameters.AddWithValue($"@Id_{pIndex}", info.Id.ToString());

		return cmd;
	}

	public static SqliteCommand VFileDataInsert(this SqliteCommand cmd, Db.VFileContentInfo info, Db.VFile file, int pIndex)
	{
		cmd.CommandText += $@"
INSERT INTO VFileContentInfo(
	Id,
	Hash,
	Size,
	SizeStored,
	Compression,
	CreationTime,
	CreateTimestamp)
VALUES (
	@InfoId_{pIndex},
	@Hash_{pIndex},
	@Size_{pIndex},
	@SizeStored_{pIndex},
	@Compression_{pIndex},
	@CreationTime_{pIndex},
	@InfoCreateTimestamp_{pIndex});
{SelectInsertedId}

INSERT INTO VFile(
	Id,
	VFileContentInfoId,
	File,
	CreateTimestamp)
VALUES (
	@FileId_{pIndex},
	last_insert_rowid(),
	@File_{pIndex},
	@FileCreateTimestamp_{pIndex});
{SelectInsertedId}
";
		cmd.Parameters.AddWithValue($"@InfoId_{pIndex}", info.Id.ToString());
		cmd.Parameters.AddWithValue($"@Hash_{pIndex}", info.Hash);
		cmd.Parameters.AddWithValue($"@Size_{pIndex}", info.Size);
		cmd.Parameters.AddWithValue($"@SizeStored_{pIndex}", info.SizeStored);
		cmd.Parameters.AddWithValue($"@Compression_{pIndex}", info.Compression);
		cmd.Parameters.AddWithValue($"@CreationTime_{pIndex}", info.CreationTime.ToDefaultString());
		cmd.Parameters.AddWithValue($"@InfoCreateTimestamp_{pIndex}", info.CreateTimestamp.ToDefaultString());
		cmd.Parameters.AddWithValue($"@FileId_{pIndex}", file.Id.ToString());
		cmd.Parameters.AddWithValue($"@File_{pIndex}", file.File);
		cmd.Parameters.AddWithValue($"@FileCreateTimestamp_{pIndex}", file.CreateTimestamp.ToDefaultString());

		return cmd;
	}

	public static SqliteCommand VFileDataInsert(this SqliteCommand cmd, List<Db.VFileContentInfo> infos, List<Db.VFile> files, int pIndex)
	{
		for (var i = 0; i < infos.Count; i++)
		{
			cmd.VFileDataInsert(infos[i], files[i], pIndex++);
		}

		return cmd;
	}
}
