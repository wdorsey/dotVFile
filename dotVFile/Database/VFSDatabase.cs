namespace dotVFile;

internal class VFSDatabase
{
	private readonly Dictionary<string, Db.VFileDataInfo> HashDataInfoMap = [];

	public VFSDatabase(VFSDatabaseOptions opts)
	{
		RootPath = opts.RootPath;
		Callbacks = opts.Callbacks;
	}

	public string RootPath { get; }
	public IVFSCallbacks? Callbacks { get; }

	public void Go()
	{
		try
		{
			var repository = new SqliteRepository();
			var path = Path.Combine(RootPath, "vfile.db");
			Callbacks?.Log(path);
			repository.Go(path);
		}
		catch (Exception e)
		{
			Callbacks?.HandleError(new("SQLITE_ERROR", e));
		}
	}
}
