namespace dotVFile;

internal class VFileDatabase
{
	public VFileDatabase(VFileDatabaseOptions opts)
	{
		RootPath = opts.RootPath;
		Hooks = opts.Hooks;
		Repository = new(Path.Combine(RootPath, "vfile.db"));
		Repository.CreateDatabaseSchema();
	}

	public string RootPath { get; }
	public IVFileHooks Hooks { get; }
	public SqliteRepository Repository { get; }

	public Db.VFileInfo SaveVFileInfo(VFileInfo vfile)
	{
		var dbVFile = new Db.VFileInfo(
			vfile.Hash,
			vfile.FullPath,
			vfile.RelativePath,
			vfile.Name,
			vfile.Extension,
			vfile.Size,
			vfile.Version,
			vfile.DeleteAt,
			DateTimeOffset.Now).Stamp();

		Repository.SaveVFileInfo(dbVFile);

		return dbVFile;
	}
}
