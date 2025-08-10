namespace dotVFile.Test;

public record TestFile(
	List<string> Directories,
	string FileName)
{
	public VFilePath VFilePath = new(Path.Combine([.. Directories]), FileName);
	public VFileContent VFileContent = new(Util.EmptyBytes());
	public string FileExtension = Util.FileExtension(FileName);
	public string FilePath = string.Empty;
	// used to change content of TestFile when storing ToBytes() as it's own VFile
	public DateTimeOffset Update;

	public byte[] ToBytes(bool update)
	{
		if (update)
			Update = DateTimeOffset.Now;
		return Util.GetBytes(
			new
			{
				VFilePath,
				FileExtension,
				FilePath,
				Update
			}, true, false);
	}
}

public record TestCase(string Name, StoreOptions Opts);

public class TestContext(string testName)
{
	public string TestName { get; } = testName;
	public List<string> Failures = [];
	public TimeSpan Elapsed { get; set; }

	public void Assert(bool result, string context)
	{
		if (!result)
			Failures.Add(context);
	}
}

public record CacheTestCase(
	CacheResult Result,
	bool ExpectedCacheHit,
	byte[] ExpectedContent)
{
	public CacheResult Result { get; set; } = Result;
	public bool ExpectedCacheHit { get; set; } = ExpectedCacheHit;
	public byte[] ExpectedContent { get; set; } = ExpectedContent;
}

public record RandomDbEntity(
	Guid Id,
	string Name,
	int Count,
	DateTimeOffset Timestamp);