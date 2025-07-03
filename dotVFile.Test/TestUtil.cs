namespace dotVFile.Test;

public class TestHooks : IVFileHooks
{
	public void ErrorHandler(VFileError err)
	{
		Console.WriteLine(err.ToString());
	}

	public void DebugLog(string msg)
	{
		Console.WriteLine(msg);
	}
}

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

public record TestCase(string Name, VFileStoreOptions Opts);

public static class TestUtil
{
	public static readonly Random Rand = new();
	public static string TestFilesDir { get; } = Path.Combine(Environment.CurrentDirectory, "TestFiles");
	public static string ResultsDir { get; } = Path.Combine(Environment.CurrentDirectory, "TestResults");
	public static string TestFileMetadataDir = Path.Combine("test", "metadata");
	public static List<TestFile> TestFiles = [];
	private static bool TestFilesLoaded = false;

	public static void LoadTestFiles()
	{
		if (TestFilesLoaded) return;

		TestFiles = [
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

		foreach (var file in TestFiles)
		{
			file.FilePath = Path.Combine(TestFilesDir, file.FileName);
			file.Update = DateTimeOffset.Now;
		}

		TestFilesLoaded = true;
	}

	public static void RunTests(VFS vfs)
	{
		Util.DeleteDirectoryContent(ResultsDir, true);
		LoadTestFiles();

		var cases = new List<TestCase>()
		{
			new("Default", VFS.GetDefaultStoreOptions()),
			new("Compression", new(VFileCompression.Compress, null, VFS.GetDefaultVersionOptions())),
			new("TTL", new(VFileCompression.None, TimeSpan.FromMinutes(1), VFS.GetDefaultVersionOptions())),
			new("VersionBehavior.Error", VFS.GetDefaultStoreOptions().SetVersionOpts(new(VFileExistsBehavior.Error, null, null))),
			new("VersionBehavior.Version", VFS.GetDefaultStoreOptions().SetVersionOpts(new(VFileExistsBehavior.Version, null, null))),
			new("VersionBehavior.Version2", VFS.GetDefaultStoreOptions().SetVersionOpts(new(VFileExistsBehavior.Version, 3, TimeSpan.FromMinutes(1)))),
		};

		Console.WriteLine("=== RUN TESTS ===");

		foreach (var @case in cases)
		{
			vfs.DANGER_WipeData();
			RunTest(vfs, @case);
		}

		vfs.Clean();
	}

	public static void RunTest(VFS vfs, TestCase @case)
	{
		var opts = @case.Opts;
		Console.WriteLine($"=== {@case.Name} Test ===");
		// test file's actual content
		// not much here, just verifying Store => Get => Assert bytes match
		// run 2 times to make sure nothing is wrong with saving the same files.
		var requests = TestFiles.Select(x => new StoreVFileRequest(x.VFilePath, new(x.FilePath), opts)).ToList();
		for (var i = 0; i < 2; i++)
		{
			var infos = vfs.StoreVFiles(requests);
			for (var k = 0; k < infos.Count; k++)
			{
				// order of infos should mirror TestFiles
				var info = infos[k];
				var file = TestFiles[k];
				var vfile = vfs.GetVFile(info) ?? throw new Exception($"null vfile: {info.VFilePath.FilePath}");
				// write files for debugging
				// WriteFiles(file, vfile, @case.Name);
				AssertFileContent(file, vfile);
			}
		}

		requests = GenerateMetadataRequests(opts, false);
		vfs.StoreVFiles(requests);

		if (opts.Compression == VFileCompression.Compress)
		{
			var vfiles = GetMetadataVFileInfos(vfs, requests.Count);

			foreach (var vfile in vfiles)
			{
				Assert(vfile.SizeStored <= vfile.Size, $"Compressed Size is not smaller than SizeStored: {vfile.VFilePath.FilePath}");
			}
		}

		if (opts.TTL.HasValue)
		{
			var vfiles = GetMetadataVFileInfos(vfs, requests.Count);

			foreach (var vfile in vfiles)
			{
				Assert(vfile.DeleteAt.HasValue, $"DeleteAt null: {vfile.VFilePath.FilePath}");
			}
		}

		if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Overwrite)
		{
			// store new files with different content
			requests = GenerateMetadataRequests(opts, true);
			vfs.StoreVFiles(requests);
			var versions = vfs.GetVFileInfoVersions(TestFileMetadataDir, false, VFileInfoVersionQuery.Versions);
			Assert(versions.Count == 0, $"versions found w/ Overwrite behavior: versions.Count={versions.Count}");
		}
		else if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Error)
		{
			// store new files with different content
			requests = GenerateMetadataRequests(opts, true);
			var result = vfs.StoreVFiles(requests);
			Assert(result.IsEmpty(), "VFiles stored w/ Error behavior.");
		}
		else if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Version)
		{
			// store new files with different content
			requests = GenerateMetadataRequests(opts, true);
			var result = vfs.StoreVFiles(requests);
			var versions = vfs.GetVFileInfoVersions(TestFileMetadataDir, false, VFileInfoVersionQuery.Versions);
			Assert(versions.Count == result.Count, $"Version count mismatch: versions.Count={versions.Count}");

			if (opts.VersionOpts.MaxVersionsRetained.HasValue)
			{
				var max = opts.VersionOpts.MaxVersionsRetained.Value;
				for (var i = 0; i < max + 1; i++)
				{
					// store new files with different content
					requests = GenerateMetadataRequests(opts, true);
					vfs.StoreVFiles(requests);
				}
				versions = vfs.GetVFileInfoVersions(TestFileMetadataDir, false, VFileInfoVersionQuery.Versions);
				var expected = max * result.Count;
				Assert(versions.Count == expected, $"MaxVersionsRetained: Expected {expected} versions, got {versions.Count}");
			}

			if (opts.VersionOpts.TTL.HasValue)
			{
				// store new files with different content
				requests = GenerateMetadataRequests(opts, true);
				vfs.StoreVFiles(requests);
				versions = vfs.GetVFileInfoVersions(TestFileMetadataDir, false, VFileInfoVersionQuery.Versions);
				foreach (var vfile in versions)
				{
					Assert(vfile.DeleteAt.HasValue, $"DeleteAt null: {vfile.VFilePath.FilePath}");
				}
			}
		}

		// @TODO: test Get functions
	}

	private static List<VFileInfo> GetMetadataVFileInfos(VFS vfs, int expectedCount)
	{
		var vfiles = vfs.GetVFileInfos(TestFileMetadataDir, false);

		Assert(vfiles.Count == expectedCount, $"GetVFileInfos by directory did not return expected file count. vfiles.Count={vfiles.Count}, expectedCount={expectedCount}");

		return vfiles;
	}

	private static List<StoreVFileRequest> GenerateMetadataRequests(
		VFileStoreOptions opts,
		bool update)
	{
		return [.. TestFiles.Select(x =>
		{
			var (name, _) = Util.FileNameAndExtension(x.FileName);
			return new StoreVFileRequest(
				new VFilePath(TestFileMetadataDir, $"{name}.json"),
				new VFileContent(x.ToBytes(update)),
				opts);
		})];
	}

	public static void AssertFileContent(VFS vfs, TestFile file, VFileInfo? info)
	{
		if (info == null) throw new Exception("info is null");

		AssertFileContent(file, vfs.GetVFile(info));
	}

	public static void AssertFileContent(TestFile file, VFile? vfile)
	{
		if (vfile == null) throw new Exception("vfile is null");

		byte[] expected = new VFileContent(file.FilePath).GetContent();
		Assert(expected.Length == vfile.Content.Length, $"file content Length mismatch. {file.FileName}");
		for (var i = 0; i < expected.Length; i++)
		{
			Assert(expected[i] == vfile.Content[i], $"bytes not equal. {file.FileName}");
		}
	}

	public static bool Assert(bool cond, string context)
	{
		if (!cond)
		{
			Console.WriteLine($"ASSERT FAILED: {context}");
			throw new Exception("Assert Failed");
		}
		return true;
	}

	public static void WriteFiles(TestFile file, VFile vfile, string testName)
	{
		var dir = Path.Combine(ResultsDir, testName);
		var (name, _) = Util.FileNameAndExtension(file.FileName);

		var filePath = Path.Combine(dir, file.FileName);
		var vfilePath = Path.Combine(dir, $"vfile_{file.FileName}");
		var vfileInfoPath = Path.Combine(dir, $"VFileInfo_{name}.json");

		Util.WriteFile(filePath, new VFileContent(file.FilePath).GetContent());
		Util.WriteFile(vfilePath, vfile.Content);
		Util.WriteFile(vfileInfoPath, Util.GetBytes(vfile.VFileInfo, true, false));
	}
}
