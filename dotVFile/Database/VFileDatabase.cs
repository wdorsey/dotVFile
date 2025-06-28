namespace dotVFile;

internal class VFileDatabase
{
	public VFileDatabase(VFileDatabaseOptions opts)
	{
		VFileDirectory = opts.VFileDirectory;
		Hooks = opts.Hooks;
		Repository = new(Path.Combine(VFileDirectory, $"{opts.Name}.vfile.db"));
		CreateDatabase();
	}

	public string VFileDirectory { get; }
	public IVFileHooks Hooks { get; }
	public SqliteRepository Repository { get; }

	public void CreateDatabase()
	{
		Repository.CreateDatabase();
	}

	public void DropDatabase()
	{
		Repository.DropDatabase();
	}

	public void DeleteDatabase()
	{
		Repository.DeleteDatabase();
	}

	public Db.VFileInfo? GetVFileInfoByFileId(string fileId)
	{
		return Repository.GetVFileInfoByFileId(fileId, Db.VFileInfoVersionQuery.Latest).SingleOrDefault();
	}

	public List<Db.VFileInfo> GetVFileInfosByFileId(string fileId, Db.VFileInfoVersionQuery versionQuery)
	{
		return Repository.GetVFileInfoByFileId(fileId, versionQuery);
	}

	public Db.VFileDataInfo? GetVFileDataInfoByFileId(string fileId)
	{
		return Repository.GetVFileDataInfoByFileId(fileId);
	}

	public Db.VFileDataInfo? GetVFileDataInfoByHash(string hash)
	{
		return Repository.GetVFileDataInfoByHash(hash);
	}

	public Db.VFile? GetVFileByFileId(string fileId)
	{
		return Repository.GetVFileByFileId(fileId);
	}

	public Db.VFileInfo SaveVFileInfo(VFileInfo info)
	{
		return SaveVFileInfo(info.AsList()).Single();
	}

	public List<Db.VFileInfo> SaveVFileInfo(List<VFileInfo> infos)
	{
		var dbInfos = infos.Select(x => VFileInfoToDbVFileInfo(x).Stamp()).ToList();

		Repository.InsertVFileInfo(dbInfos);

		foreach (var info in dbInfos)
		{
			Hooks.Log($"Saved Db.VFileInfo, Id: {info.Id}");
		}

		return dbInfos;
	}

	public Db.VFileData SaveVFileData(VFileData data)
	{
		return SaveVFileData(data.AsList()).Single();
	}

	public List<Db.VFileData> SaveVFileData(List<VFileData> data)
	{
		var infos = new List<Db.VFileDataInfo>();
		var files = new List<Db.VFile>();
		var result = new List<Db.VFileData>();
		foreach (var d in data)
		{
			infos.Add(VFileDataInfoToDbVFileDataInfo(d.DataInfo));
			files.Add(BytesToDbVFile(d.Content));
		}

		Repository.InsertVFileData(infos, files);

		for (var i = 0; i < infos.Count; i++)
		{
			var info = infos[i];
			var file = files[i];

			Hooks.Log($"Saved Db.VFileDataInfo, Id: {info.Id}");
			Hooks.Log($"Saved Db.VFile, Id: {file.Id}");

			result.Add(new(info, file));
		}

		return result;
	}

	private static Db.VFileInfo VFileInfoToDbVFileInfo(VFileInfo info)
	{
		return new Db.VFileInfo(
			info.FullPath,
			info.Hash,
			info.RelativePath,
			info.Name,
			info.Extension,
			info.Size,
			info.Version,
			info.DeleteAt,
			info.CreationTime);
	}

	private static Db.VFileDataInfo VFileDataInfoToDbVFileDataInfo(VFileDataInfo info)
	{
		return new Db.VFileDataInfo(
			info.Hash,
			info.Size,
			info.SizeOnDisk,
			(byte)info.Compression,
			info.CreationTime);
	}

	private static Db.VFile BytesToDbVFile(byte[] bytes)
	{
		return new Db.VFile(bytes);
	}
}
