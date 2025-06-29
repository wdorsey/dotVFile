using System.Data;
using Microsoft.Data.Sqlite;

namespace dotVFile;

internal class VFileDatabase
{
	private const string SelectInsertedRowId = " SELECT last_insert_rowid() AS RowId; ";

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
	FileId					TEXT NOT NULL,
	RelativePath			TEXT NOT NULL,
	FileName				TEXT NOT NULL,
	FileExtension			TEXT NOT NULL,
	Versioned				TEXT,
	DeleteAt				TEXT,
	CreationTime			TEXT NOT NULL,
	CreateTimestamp			TEXT NOT NULL,
	PRIMARY KEY(RowId AUTOINCREMENT),
	FOREIGN KEY(VFileContentRowId) REFERENCES VFileContent(VFileContentRowId)
);
CREATE UNIQUE INDEX IF NOT EXISTS VFileInfo_Id ON VFileInfo(Id);
CREATE INDEX IF NOT EXISTS        VFileInfo_VFileContentRowId ON VFileInfo(VFileContentRowId);
CREATE INDEX IF NOT EXISTS        VFileInfo_FileId ON VFileInfo(FileId);
CREATE INDEX IF NOT EXISTS        VFileInfo_FileIdLatest ON VFileInfo(FileId, Versioned) WHERE Versioned IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS VFileInfo_FileIdVersioned ON VFileInfo(FileId, Versioned);
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
DROP INDEX IF EXISTS VFileInfo_FileId;
DROP INDEX IF EXISTS VFileInfo_FileIdLatest;
DROP INDEX IF EXISTS VFileInfo_FileIdVersioned;
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

	public Db.VFile? GetVFile(string fileId)
	{
		return GetVFiles(fileId, Db.VFileInfoVersionQuery.Latest).SingleOrDefault();
	}

	public List<Db.VFile> GetVFiles(string fileId, Db.VFileInfoVersionQuery versionQuery)
	{
		var result = new List<Db.VFile>();

		var version = versionQuery switch
		{
			Db.VFileInfoVersionQuery.Latest => "AND Versioned IS NULL",
			Db.VFileInfoVersionQuery.Versions => "AND Versioned IS NOT NULL",
			Db.VFileInfoVersionQuery.Both => string.Empty,
			_ => throw new ArgumentOutOfRangeException(nameof(versionQuery), $"{versionQuery}")
		};

		var sql = $@"
SELECT 
	i.*,
	c.RowId as ContentRowId,
	c.Id as ContentId,
	c.Hash,
	c.Size,
	c.SizeStored,
	c.Compression,
	c.CreationTime as ContentCreationTime,
	c.CreateTimestamp as ContentCreateTimestamp
FROM
	VFileInfo i
	INNER JOIN VFileContent c ON c.RowId = i.VFileContentRowId
WHERE
	i.FileId = @FileId
	{version};
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@FileId", fileId);
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var info = new Db.VFileInfo().ReadEntityValues(reader);
			info.VFileContentRowId = reader.GetInt64("VFileContentRowId");
			info.FileId = reader.GetString("FileId");
			info.RelativePath = reader.GetString("RelativePath");
			info.FileName = reader.GetString("FileName");
			info.FileExtension = reader.GetString("FileExtension");
			info.Versioned = reader.GetDateTimeOffsetNullable("Versioned");
			info.DeleteAt = reader.GetDateTimeOffsetNullable("DeleteAt");
			info.CreationTime = reader.GetDateTimeOffset("CreationTime");

			var content = new Db.VFileContent().ReadEntityValues(reader, "Content");
			content.Hash = reader.GetString("Hash");
			content.Size = reader.GetInt64("Size");
			content.SizeStored = reader.GetInt64("SizeStored");
			content.Compression = reader.GetByte("Compression");
			content.CreationTime = reader.GetDateTimeOffset("ContentCreationTime");

			result.Add(new(info, content));
			reader.NextResult();
		}

		return result;
	}

	public byte[]? GetVFileContent(Guid contentId)
	{
		const string sql = @"
SELECT
	Content
FROM
	VFileContent
WHERE
	Id = @Id
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@Id", contentId.ToString());
		var result = cmd.ExecuteScalar();

		return result != null ? (byte[])result : null;
	}

	public Db.StoreVFilesResult SaveStoreVFilesState(StoreVFilesState state)
	{
		// order:
		//	NewVFileContents
		//	DeleteVFileInfos
		//  DeleteVFileContents (just clean-up of DeleteVFileInfos)
		//  UpdateVFileInfos
		//	NewVFileInfos (requires link to VFileContentRowId)
		var result = new Db.StoreVFilesResult();
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		int idx = 0;

		foreach (var (info, content) in state.NewVFileContents)
		{
			cmd.CommandText += $@"
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
	@Id_{idx},
	@Hash_{idx},
	@Size_{idx},
	@SizeStored_{idx},
	@Compression_{idx},
	@Content_{idx},
	@CreationTime_{idx},
	@CreateTimestamp_{idx});
{SelectInsertedRowId}
";
			var db = ToDbVFileContent(info, content).Stamp();
			cmd.Parameters.AddWithValue($"@Id_{idx}", db.Id.ToString());
			cmd.Parameters.AddWithValue($"@Hash_{idx}", db.Hash);
			cmd.Parameters.AddWithValue($"@Size_{idx}", db.Size);
			cmd.Parameters.AddWithValue($"@SizeStored_{idx}", db.SizeStored);
			cmd.Parameters.AddWithValue($"@Compression_{idx}", db.Compression);
			cmd.Parameters.AddWithValue($"@Content_{idx}", db.Content);
			cmd.Parameters.AddWithValue($"@CreationTime_{idx}", db.CreationTime.ToDefaultString());
			cmd.Parameters.AddWithValue($"@CreateTimestamp_{idx}", db.CreateTimestamp.ToDefaultString());
			idx++;
			result.NewVFileContents.Add(db);
		}

		foreach (var delete in state.DeleteVFileInfos)
		{
			cmd.CommandText += $@"
DELETE FROM VFileInfo WHERE Id = @Id_{idx};
";
			cmd.Parameters.AddWithValue($"@Id_{idx}", delete.Id.ToString());
			idx++;
		}

		foreach (var delete in state.DeleteVFileContents)
		{
			cmd.CommandText += $@"
DELETE FROM VFileContent WHERE Id = @Id_{idx};
";
			cmd.Parameters.AddWithValue($"@Id_{idx}", delete.Id.ToString());
			idx++;
		}

		foreach (var update in state.UpdateVFileInfos)
		{
			// only update-able fields are VFileId, Versioned, and DeleteAt
			cmd.CommandText += $@"
UPDATE VFileInfo
SET 
	FileId = @FileId_{idx},
	Versioned = @Versioned_{idx},
	DeleteAt = @DeleteAt_{idx}
WHERE
	Id = @Id_{idx};
";
			var db = ToDbVFileInfo(update);
			cmd.Parameters.AddWithValue($"@FileId_{idx}", db.FileId);
			cmd.Parameters.AddWithValue($"@Versioned_{idx}", (db.Versioned?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@DeleteAt_{idx}", (db.DeleteAt?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@Id_{idx}", update.Id.ToString());
			idx++;
			result.UpdatedVFileInfos.Add(db);
		}

		foreach (var info in state.NewVFileInfos)
		{
			cmd.CommandText += $@"
INSERT INTO VFileInfo (
	Id,
	VFileContentRowId,
	FileId,
	RelativePath,
	FileName,
	FileExtension,
	Versioned,
	DeleteAt,
	CreationTime,
	CreateTimestamp)
VALUES (
	@Id_{idx},
	(SELECT RowId FROM VFileContent WHERE Hash = @Hash_{idx}),
	@FileId_{idx},
	@RelativePath_{idx},
	@FileName_{idx},
	@FileExtension_{idx},
	@Versioned_{idx},
	@DeleteAt_{idx},
	@CreationTime_{idx},
	@CreateTimestamp_{idx});
{SelectInsertedRowId}
";
			var db = ToDbVFileInfo(info).Stamp();
			cmd.Parameters.AddWithValue($"@Id_{idx}", db.Id.ToString());
			cmd.Parameters.AddWithValue($"@Hash_{idx}", info.Hash);
			cmd.Parameters.AddWithValue($"@FileId_{idx}", db.FileId);
			cmd.Parameters.AddWithValue($"@RelativePath_{idx}", db.RelativePath);
			cmd.Parameters.AddWithValue($"@FileName_{idx}", db.FileName);
			cmd.Parameters.AddWithValue($"@FileExtension_{idx}", db.FileExtension);
			cmd.Parameters.AddWithValue($"@Versioned_{idx}", (db.Versioned?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@DeleteAt_{idx}", (db.DeleteAt?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@CreationTime_{idx}", db.CreationTime.ToDefaultString());
			cmd.Parameters.AddWithValue($"@CreateTimestamp_{idx}", db.CreateTimestamp.ToDefaultString());
			idx++;
			result.NewVFileInfos.Add(db);
		}

		connection.Open();
		using var transaction = connection.BeginTransaction();
		try
		{
			var reader = cmd.ExecuteReader();
			result.NewVFileContents.ReadRowId(reader);
			result.NewVFileInfos.ReadRowId(reader);
			transaction.Commit();
		}
		catch (SqliteException e)
		{
			Hooks.Error(new(VFileErrorCodes.SqliteException, e.Message, nameof(SaveStoreVFilesState)));
			transaction.Rollback();
			throw;
		}

		return result;
	}

	private static Db.VFileInfo ToDbVFileInfo(VFileInfo info)
	{
		return new Db.VFileInfo
		{
			Id = info.Id,
			FileId = info.FullPath,
			RelativePath = info.RelativePath,
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
