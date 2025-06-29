namespace dotVFile.Test;

public class TestHooks : IVFileHooks
{
	public void Error(VFileError err)
	{
		Console.WriteLine(err.ToString());
	}

	public void Log(string msg)
	{
		Console.WriteLine(msg);
	}
}

public record TestFile(
	List<string> RelativePath,
	string FileName)
{
	public VFileId? VFileId { get; set; }
	public VFilePath VFilePath { get; } = new(string.Join(VFS.PathDirectorySeparator, RelativePath), FileName);
	public List<byte[]> Content { get; } = [Util.GetFileBytes(Path.Combine(TestUtil.TestFilesDir, FileName))];
}

public static class TestUtil
{
	public static string TestFilesDir { get; } = Path.Combine(Environment.CurrentDirectory, "TestFiles");
	public static string ResultsDir { get; } = Path.Combine(Environment.CurrentDirectory, "Results");

	public static void RunStandardTest(VFS vfs, VFileStorageOptions opts, string testName)
	{
		var files = GetTestFiles();
		var requests = files.Select(x => new StoreVFileRequest(x.VFilePath, x.Content.Last(), opts)).ToList();
		var infos = vfs.StoreVFiles(requests);
		for (var i = 0; i < infos.Count; i++)
		{
			var file = files[i];
			var info = infos[i];
			Console.WriteLine(info.VFileId.ToString());
			var vfile = vfs.GetVFile(file.VFilePath) ?? throw new Exception($"null vfile {info.VFileId}");
			WriteFiles(file, vfile, testName);
			AssertFile(file, vfile);
		}
	}

	public static List<TestFile> GetTestFiles()
	{
		return
		[
			new([], "test-file-1.json"),
			new([], "test-file-1 - Copy.json"),
			new(["/"], "test-file-2.json"),
			new(["/"], "test-file-2 - Copy.json"),
			new(["a"], "test-file-3.json"),
			new(["a", "b"], "test-file-4.json"),
			new(["b", "c"], "test-file-5.json"),
			new(["c", "b", "a"], "test-file-6.json"),
			new(["a", "c"], "test-file-7.json"),
			new(["a", "b", "x"], "test-file-8.json"),
			new(["x", "x"], "test-file-9.json"),
			new(["hello", "world"], "test-file-10.json"),
			new(["img"], "demon-slayer-infinity-castle-18-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-47-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-48-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-61-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-62-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-75-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-76-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-82-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-83-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-89-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-90-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-96-days.jpg"),
			new(["img"], "demon-slayer-infinity-castle-97-days.jpg")
		];
	}

	public static void AssertFile(TestFile file, VFile vfile)
	{
		// We only assert that the Content (byte[]) matches.
		// The reason for this is because that is all that ultimately matters.
		// At the end of the day, all this system does is store and get files. StoreVFile and GetVFile.
		// So, if we're storing files and then getting them via it's VFileId (path + fileName),
		// then the content matching proves it's doing everything correctly. At least, everything critical.
		// If the content matches, everything is working.
		// Trying to verify every bit individually would make for a whole lot more work
		// in writing this testing while providing very little value.

		byte[] expected = file.Content.Last();
		if (expected.Length != vfile.Content.Length)
		{
			Console.WriteLine($"file content Length mismatch. {file.FileName}");
			throw new Exception();
		}
		for (var i = 0; i < expected.Length; i++)
		{
			if (expected[i] != vfile.Content[i])
			{
				Console.WriteLine($"bytes not equal. {file.FileName}");
				throw new Exception();
			}
		}
	}

	public static void WriteFiles(TestFile file, VFile vfile, string testName)
	{
		var dir = Path.Combine(ResultsDir, testName);
		var (name, _) = Util.FileNameAndExtension(file.FileName);

		var filePath = Path.Combine(dir, file.FileName);
		var vfilePath = Path.Combine(dir, $"vfile_{file.FileName}");
		var vfileInfoPath = Path.Combine(dir, $"VFileInfo_{name}.json");

		Util.WriteFile(filePath, file.Content.Last());
		Util.WriteFile(vfilePath, vfile.Content);
		Util.WriteFile(vfileInfoPath, Util.GetBytes(vfile.VFileInfo, true, false));
	}
}
