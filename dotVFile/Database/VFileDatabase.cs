using System.Data;
using System.Text;
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

	public Db.VFile? GetLatestVFile(string filePath)
	{
		return GetVFiles(filePath.AsList(), VFileInfoVersionQuery.Latest).SingleOrDefault();
	}

	public List<Db.VFile> GetVFiles(
		List<string> filePaths,
		VFileInfoVersionQuery versionQuery)
	{
		var results = new List<Db.VFile>();

		var versionedCond = VersionedSql(versionQuery);

		foreach (var list in filePaths.Distinct().Partition(50))
		{
			var inParams = DbUtil.BuildInParams(list, "@Path", SqliteType.Text);
			var where = $" i.FilePath IN ({inParams.Sql}) {versionedCond} ";
			results.AddRange(GetVFiles(where, inParams.Parameters));
		}

		return results;
	}

	public Db.VFile? GetVFile(Guid id)
	{
		return GetVFiles(id.AsList()).SingleOrDefault();
	}

	public List<Db.VFile> GetVFiles(List<Guid> ids)
	{
		var results = new List<Db.VFile>();

		foreach (var list in ids.Distinct().Partition(50))
		{
			var inParams = DbUtil.BuildInParams(list.Select(x => x.ToString()), "@Id", SqliteType.Text);
			var where = $@"	i.Id IN ({inParams.Sql}) ";
			results.AddRange(GetVFiles(where, inParams.Parameters));
		}

		return results;
	}

	public List<Db.VFile> GetVFilesByDirectory(string directory, VFileInfoVersionQuery versionQuery)
	{
		var versionedCond = VersionedSql(versionQuery);
		var where = $" i.Directory = @Directory {versionedCond} ";
		var param = new SqliteParameter("@Directory", directory);
		return GetVFiles(where, [param]);
	}

	private List<Db.VFile> GetVFiles(string whereSql, List<SqliteParameter> parameters)
	{
		var results = new List<Db.VFile>();

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
	{whereSql};
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddRange(parameters);
		connection.Open();
		var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var info = new Db.VFileInfo().ReadEntityValues(reader);
			info.VFileContentRowId = reader.GetInt64("VFileContentRowId");
			info.FilePath = reader.GetString("FilePath");
			info.Directory = reader.GetString("Directory");
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

			results.Add(new(info, content));
		}

		return results;
	}

	private static string VersionedSql(VFileInfoVersionQuery versionQuery)
	{
		return versionQuery switch
		{
			VFileInfoVersionQuery.Latest => " AND Versioned IS NULL ",
			VFileInfoVersionQuery.Versions => " AND Versioned IS NOT NULL ",
			VFileInfoVersionQuery.Both => string.Empty,
			_ => throw new ArgumentOutOfRangeException(nameof(versionQuery), $"{versionQuery}")
		};
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

	public void DeleteVFileContent(List<long> rowIds)
	{
		if (rowIds.IsEmpty()) return;
		int idx = 0;
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		foreach (var rowId in rowIds)
		{
			cmd.CommandText += $@"
DELETE FROM VFileContent WHERE RowId = @RowId_{idx};
";
			cmd.Parameters.AddWithValue($"@RowId_{idx}", rowId);
			idx++;
		}
		connection.Open();
		cmd.ExecuteNonQuery();
	}

	public List<Db.VFileContent> FetchContent(List<Db.VFileContent> vfiles)
	{
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();

		foreach (var list in vfiles.Partition(50))
		{
			var dict = list.ToDictionary(x => x.RowId, x => x);
			var rowIds = string.Join(',', list.Select(x => x.RowId));
			var sql = $@"
SELECT
	RowId,
	Content
FROM
	VFileContent
WHERE
	RowId IN ({rowIds})
";
			var cmd = new SqliteCommand(sql, connection);
			var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				var rowId = reader.GetInt64("RowId");
				dict[rowId].Content = (byte[])reader["Content"];
			}
		}

		return vfiles;
	}

	public Db.VFileInfo? GetVFileInfoByFilePath(string filePath)
	{
		const string sql = @"
SELECT
	RowId,
	VFileContentRowId,
	FilePath,
	Directory,
	FileName,
	FileExtension,
	Versioned,
	DeleteAt,
	CreationTime,
	CreateTimestamp
FROM
	VFileInfo
WHERE
	FilePath = @FilePath
	AND Versioned IS NULL
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@FilePath", filePath);
		connection.Open();
		var reader = cmd.ExecuteReader();

		if (reader.Read())
		{
			var info = new Db.VFileInfo().ReadEntityValues(reader);
			info.VFileContentRowId = reader.GetInt64("VFileContentRowId");
			info.FilePath = reader.GetString("FilePath");
			info.Directory = reader.GetString("Directory");
			info.FileName = reader.GetString("FileName");
			info.FileExtension = reader.GetString("FileExtension");
			info.Versioned = reader.GetDateTimeOffsetNullable("Versioned");
			info.DeleteAt = reader.GetDateTimeOffsetNullable("DeleteAt");
			info.CreationTime = reader.GetDateTimeOffset("CreationTime");

			return info;
		}

		return null;
	}

	/// <summary>
	/// Does not pull Content bytes, use FetchContent.
	/// </summary>
	public Db.VFileContent? GetVFileContentByHash(string hash)
	{
		const string sql = @"
SELECT
	RowId,
	Id,
	Hash,
	Size,
	SizeStored,
	Compression,
	CreationTime,
	CreateTimestamp
FROM
	VFileContent
WHERE
	Hash = @Hash
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddWithValue("@Hash", hash);
		connection.Open();
		var reader = cmd.ExecuteReader();

		if (reader.Read())
		{
			var content = new Db.VFileContent().ReadEntityValues(reader);
			content.Hash = reader.GetString("Hash");
			content.Size = reader.GetInt64("Size");
			content.SizeStored = reader.GetInt64("SizeStored");
			content.Compression = reader.GetByte("Compression");
			content.CreationTime = reader.GetDateTimeOffset("CreationTime");

			return content;
		}

		return null;
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
{SelectInsertedRowId}
";
		using var connection = new SqliteConnection(ConnectionString);
		var cmd = new SqliteCommand(sql, connection);
		var db = ToDbVFileContent(info, content).Stamp();
		cmd.Parameters.AddWithValue($"@Id", db.Id.ToString());
		cmd.Parameters.AddWithValue($"@Hash", db.Hash);
		cmd.Parameters.AddWithValue($"@Size", db.Size);
		cmd.Parameters.AddWithValue($"@SizeStored", db.SizeStored);
		cmd.Parameters.AddWithValue($"@Compression", db.Compression);
		cmd.Parameters.AddWithValue($"@Content", db.Content);
		cmd.Parameters.AddWithValue($"@CreationTime", db.CreationTime.ToDefaultString());
		cmd.Parameters.AddWithValue($"@CreateTimestamp", db.CreateTimestamp.ToDefaultString());
		connection.Open();
		var reader = cmd.ExecuteReader();
		return db.ReadRowId(reader);
	}

	public Db.StoreVFilesResult SaveStoreVFilesState(StoreVFilesState state)
	{
		// All VFileInfo changes written transactionally.
		// order:
		//	DeleteVFileInfos
		//  UpdateVFileInfos
		//	NewVFileInfos (requires link to VFileContentRowId)
		var result = new Db.StoreVFilesResult();
		int idx = 0;
		using var connection = new SqliteConnection(ConnectionString);
		connection.Open();
		using var transaction = connection.BeginTransaction();
		var cmd = new SqliteCommand(string.Empty, connection, transaction);

		foreach (var delete in state.DeleteVFileInfos)
		{
			cmd.CommandText += $@"
DELETE FROM VFileInfo WHERE RowId = @RowId_{idx};
";
			cmd.Parameters.AddWithValue($"@RowId_{idx}", delete.RowId);
			idx++;
		}

		foreach (var update in state.UpdateVFileInfos)
		{
			// only update-able fields are Versioned and DeleteAt
			cmd.CommandText += $@"
UPDATE VFileInfo
SET 
	Versioned = @Versioned_{idx},
	DeleteAt = @DeleteAt_{idx}
WHERE
	RowId = @RowId_{idx};
";
			cmd.Parameters.AddWithValue($"@Versioned_{idx}", (update.Versioned?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@DeleteAt_{idx}", (update.DeleteAt?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@RowId_{idx}", update.RowId);
			idx++;
			result.UpdatedVFileInfos.Add(update);
		}

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
	@Id_{idx},
	(SELECT RowId FROM VFileContent WHERE Hash = @Hash_{idx}),
	@FilePath_{idx},
	@Directory_{idx},
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
			cmd.Parameters.AddWithValue($"@FilePath_{idx}", db.FilePath);
			cmd.Parameters.AddWithValue($"@Directory_{idx}", db.Directory);
			cmd.Parameters.AddWithValue($"@FileName_{idx}", db.FileName);
			cmd.Parameters.AddWithValue($"@FileExtension_{idx}", db.FileExtension);
			cmd.Parameters.AddWithValue($"@Versioned_{idx}", (db.Versioned?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@DeleteAt_{idx}", (db.DeleteAt?.ToDefaultString()).NullCoalesce());
			cmd.Parameters.AddWithValue($"@CreationTime_{idx}", db.CreationTime.ToDefaultString());
			cmd.Parameters.AddWithValue($"@CreateTimestamp_{idx}", db.CreateTimestamp.ToDefaultString());
			idx++;
			result.NewVFileInfos.Add(db);
		}

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
