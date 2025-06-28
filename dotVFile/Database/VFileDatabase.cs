namespace dotVFile;

internal class VFileDatabase
{
	public VFileDatabase(VFileDatabaseOptions opts)
	{
		RootPath = opts.RootPath;
		Hooks = opts.Hooks;
		Repository = new(Path.Combine(RootPath, "vfile.db"));
		Repository.CreateDatabase();
	}

	public string RootPath { get; }
	public IVFileHooks Hooks { get; }
	public SqliteRepository Repository { get; }

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

	public Db.VFileInfo SaveVFileInfo(VFileInfo info)
	{
		var dbInfo = VFileInfoToDbVFileInfo(info).Stamp();

		Repository.InsertVFileInfo(dbInfo);

		Hooks.Log($"Saved Db.VFileInfo, Id: {dbInfo.Id}");

		return dbInfo;
	}

	public Db.VFileDataInfo SaveVFileDataInfo(VFileDataInfo info)
	{
		var dbInfo = VFileDataInfoToDbVFileDataInfo(info).Stamp();

		Repository.InsertVFileDataInfo(dbInfo);

		Hooks.Log($"Saved Db.VFileDataInfo, Id: {dbInfo.Id}");

		return dbInfo;
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
			info.Directory,
			info.FileName,
			info.Size,
			info.SizeOnDisk,
			(byte)info.Compression,
			info.CreationTime);
	}
}
