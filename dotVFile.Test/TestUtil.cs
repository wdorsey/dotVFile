namespace dotVFile.Test;

public class TestHooks : IVFileHooks
{

	public TestHooks()
	{
		Util.DeleteFile(TestUtil.LogFilePath);
	}

	public void ErrorHandler(VFileError err)
	{
		TestUtil.WriteLine(err.ToString());
	}

	public void DebugLog(string msg)
	{
		TestUtil.WriteLine(msg);
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

public class TestContext
{
	public List<string> FailedAsserts = [];

	public void Assert(bool result, string context)
	{
		if (!result)
			FailedAsserts.Add(context);
	}
}

public static class TestUtil
{
	public static readonly string LogFilePath = Path.Combine(Environment.CurrentDirectory, "test-log.txt");
	public static readonly Random Rand = new();
	public static string TestFilesDir { get; } = Path.Combine(Environment.CurrentDirectory, "TestFiles");
	public static string ResultsDir { get; } = Path.Combine(Environment.CurrentDirectory, "TestResults");
	public static string TestFileMetadataDir = Path.Combine("test", "metadata");
	public static List<TestFile> TestFiles = [];
	private static bool TestFilesLoaded = false;

	public static void RunTest(string testName, Action<TestContext> testFn)
	{
		var context = new TestContext();

		try
		{
			var timer = new Timer(string.Empty).Start();

			testFn(context);

			timer.Stop();

			var result = context.FailedAsserts.Count > 0 ? "failed" : "passed";
			WriteLine($"{testName}...{result}...{timer.Elapsed.TimeString()}");

			if (context.FailedAsserts.Count > 0)
			{
				WriteLine(new { context.FailedAsserts }.ToJson(true)!);
			}
		}
		catch (Exception e)
		{
			WriteLine($"test '{testName}' threw exception.");
			WriteLine(e.ToString());
			throw;
		}
	}

	public static void WriteLine(string msg)
	{
		var prefix = $"{DateTime.Now:HH:mm:ss.fff}> ";
		var text = prefix + msg;
		Console.WriteLine(text);
		File.AppendAllText(LogFilePath, text + Environment.NewLine);
	}

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
			new(["aaa", "bbbb", "ccc", "xxxx", "yyyy", "zzzz"], "test-file-8.json"),
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

	public static void RunTests(VFile vfile)
	{
		Util.DeleteDirectoryContent(ResultsDir, true);
		LoadTestFiles();

		RunOptionsTests(vfile);
		RunVFileAPITests(vfile);

		vfile.Tools.LogMetrics();
	}

	public static void RunOptionsTests(VFile vfile)
	{
		var cases = new List<TestCase>()
		{
			new("Default", VFileStoreOptions.Default()),
			new("Compression", new(VFileCompression.Compress, null, VFileVersionOptions.Default())),
			new("TTL", new(VFileCompression.None, TimeSpan.FromMinutes(1), VFileVersionOptions.Default())),
			new("VersionBehavior.Error", VFileStoreOptions.Default().SetVersionOpts(new(VFileExistsBehavior.Error, null, null))),
			new("VersionBehavior.Version", VFileStoreOptions.Default().SetVersionOpts(new(VFileExistsBehavior.Version, null, null))),
			new("VersionBehavior.Version2", VFileStoreOptions.Default().SetVersionOpts(new(VFileExistsBehavior.Version, 3, TimeSpan.FromMinutes(1)))),
		};

		foreach (var @case in cases)
		{
			vfile.DANGER_WipeData();
			var opts = @case.Opts;

			string TestName(string name) => $"{@case.Name} - {name}";

			RunTest(TestName("StoreVFiles/GetBytes"), ctx =>
			{
				// test actual content
				// not much here, just verifying Store => Get => Assert bytes match.
				// run 2 times to make sure nothing is wrong with saving the same files.
				var requests = TestFiles.Select(x => new StoreVFileRequest(x.VFilePath, new(x.FilePath), opts)).ToList();
				for (var i = 0; i < 2; i++)
				{
					var infos = vfile.StoreVFiles(requests);
					for (var k = 0; k < infos.Count; k++)
					{
						// order of infos should mirror TestFiles
						var info = infos[k];
						var file = TestFiles[k];
						var bytes = vfile.GetBytes(info) ?? throw new Exception($"null vfile: {info.VFilePath.FilePath}");
						// write files for debugging
						// WriteFiles(file, vfile, @case.Name);
						AssertFileContent(file, bytes, ctx);
					}
				}
			});

			var requests = GenerateMetadataRequests(opts, false);
			vfile.StoreVFiles(requests);

			if (opts.Compression == VFileCompression.Compress)
			{
				RunTest(TestName("VFileCompression.Compress"), ctx =>
				{
					var infos = GetMetadataVFileInfos(vfile, requests.Count, ctx);

					foreach (var info in infos)
					{
						ctx.Assert(info.SizeStored <= info.Size, $"Compressed Size is not smaller than SizeStored: {info.VFilePath.FilePath}");
					}
				});
			}

			if (opts.TTL.HasValue)
			{
				RunTest(TestName("opts.TTL"), ctx =>
				{
					var infos = GetMetadataVFileInfos(vfile, requests.Count, ctx);

					foreach (var info in infos)
					{
						ctx.Assert(info.DeleteAt.HasValue, $"DeleteAt null: {info.VFilePath.FilePath}");
					}
				});
			}

			if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Overwrite)
			{
				RunTest(TestName("VFileExistsBehavior.Overwrite"), ctx =>
				{
					// store new files with different content
					requests = GenerateMetadataRequests(opts, true);
					vfile.StoreVFiles(requests);
					var versions = vfile.GetVFileInfoVersions(new VDirectory(TestFileMetadataDir), VFileInfoVersionQuery.Versions);
					ctx.Assert(versions.Count == 0, $"versions found w/ Overwrite behavior: versions.Count={versions.Count}");
				});
			}
			else if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Error)
			{
				RunTest(TestName("VFileExistsBehavior.Error"), ctx =>
				{
					// store new files with different content
					requests = GenerateMetadataRequests(opts, true);
					var result = vfile.StoreVFiles(requests);
					ctx.Assert(result.IsEmpty(), "VFiles stored w/ Error behavior.");
				});
			}
			else if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Version)
			{
				RunTest(TestName("VFileExistsBehavior.Version"), ctx =>
				{
					// store new files with different content
					requests = GenerateMetadataRequests(opts, true);
					var result = vfile.StoreVFiles(requests);
					var versions = vfile.GetVFileInfoVersions(new VDirectory(TestFileMetadataDir), VFileInfoVersionQuery.Versions);
					ctx.Assert(versions.Count == result.Count, $"Version count mismatch: versions.Count={versions.Count}");

					if (opts.VersionOpts.MaxVersionsRetained.HasValue)
					{
						var max = opts.VersionOpts.MaxVersionsRetained.Value;
						for (var i = 0; i < max + 1; i++)
						{
							// store new files with different content
							requests = GenerateMetadataRequests(opts, true);
							vfile.StoreVFiles(requests);
						}
						versions = vfile.GetVFileInfoVersions(new VDirectory(TestFileMetadataDir), VFileInfoVersionQuery.Versions);
						var expected = max * result.Count;
						ctx.Assert(versions.Count == expected, $"MaxVersionsRetained: Expected {expected} versions, got {versions.Count}");
					}

					if (opts.VersionOpts.TTL.HasValue)
					{
						// store new files with different content
						requests = GenerateMetadataRequests(opts, true);
						vfile.StoreVFiles(requests);
						versions = vfile.GetVFileInfoVersions(new VDirectory(TestFileMetadataDir), VFileInfoVersionQuery.Versions);
						foreach (var version in versions)
						{
							ctx.Assert(version.DeleteAt.HasValue, $"DeleteAt null: {version.VFilePath.FilePath}");
						}
					}
				});
			}

			vfile.Clean();
		}
	}

	public static void RunVFileAPITests(VFile vfile)
	{
		vfile.DANGER_WipeData();

		// store twice to generate versions
		var opts = new VFileStoreOptions(VFileCompression.None, null, new(VFileExistsBehavior.Version, null, null));
		var requests = GenerateMetadataRequests(opts, true);
		vfile.StoreVFiles(requests);
		requests = GenerateMetadataRequests(opts, true);
		vfile.StoreVFiles(requests);
		var notFound = "__not_found__";

		var context = "GetVFileInfosByPath";
		RunTest(context, ctx =>
		{
			var rq = requests.ChooseOne();
			var info = vfile.GetVFileInfo(rq.Path);
			AssertRequestFileInfo(rq, info, false, ctx, context);

			var infos = vfile.GetVFileInfos([.. requests.Select(x => x.Path)]);
			AssertRequestsVFileInfos(requests, infos, false, ctx, context);

			infos = vfile.GetVFileInfoVersions([.. requests.Select(x => x.Path)], VFileInfoVersionQuery.Versions);
			AssertRequestsVFileInfos(requests, infos, true, ctx, context);

			infos = vfile.GetVFileInfoVersions([.. requests.Select(x => x.Path)], VFileInfoVersionQuery.Both);
			ctx.Assert(infos.Count == requests.Count * 2, context);

			info = vfile.GetVFileInfo(new VFilePath(notFound));
			ctx.Assert(info == null, "info == null");
		});

		context = "GetVFileInfosByDirectory";
		RunTest(context, ctx =>
		{
			foreach (var rq in requests.GroupBy(x => x.Path.Directory.Path))
			{
				var infos = vfile.GetVFileInfos(new VDirectory(rq.Key));
				AssertRequestsVFileInfos([.. rq], infos, false, ctx, context);
			}

			// recursive test
			var result = vfile.GetVFileInfos(new VDirectory(VDirectory.DirectorySeparator.ToString()), true);
			AssertRequestsVFileInfos(requests, result, false, ctx, context);

			result = vfile.GetVFileInfos(new VDirectory(notFound));
			ctx.Assert(result.Count == 0, "result.Count == 0");

			// get Versions by Directory is tested good enough by RunOptionsTests
		});

		context = "GetBytes";
		RunTest(context, ctx =>
		{
			var rq = requests.ChooseOne();
			var expected = rq.Content.GetContent();

			var bytes = vfile.GetBytes(rq.Path);
			AssertContent(expected, bytes, ctx, context);

			var info = vfile.GetVFileInfo(rq.Path);
			bytes = vfile.GetBytes(info!);
			AssertContent(expected, bytes, ctx, context);
		});

		context = "CopyVFiles";
		RunTest(context, ctx =>
		{
			var to = new VDirectory("copy/to/here");

			var rq = requests.ChooseOne();
			var path = new VFilePath(to, rq.Path.FileName);
			var info = vfile.GetVFileInfo(rq.Path)!;
			var copy = vfile.CopyVFile(info, path, opts);
			AssertRequestFileInfo(rq with { Path = path }, copy, false, ctx, context);
			AssertContent(vfile.GetBytes(info)!, vfile.GetBytes(copy!)!, ctx, context);

			var infos = vfile.GetVFileInfos([.. requests.Select(x => x.Path)]);
			var copies = vfile.CopyVFiles(infos, to, opts);
			AssertRequestsVFileInfos(
				[.. requests.Select(x => x with { Path = new(to, x.Path.FileName) })],
				copies, false, ctx, context);
		});
	}

	private static List<VFileInfo> GetMetadataVFileInfos(VFile vfile, int expectedCount, TestContext ctx)
	{
		var vfiles = vfile.GetVFileInfos(new VDirectory(TestFileMetadataDir));

		ctx.Assert(vfiles.Count == expectedCount, $"GetVFileInfos by directory did not return expected file count. vfiles.Count={vfiles.Count}, expectedCount={expectedCount}");

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

	public static void AssertFileContent(TestFile file, byte[]? bytes)
	{
		var ctx = new TestContext();

		AssertFileContent(file, bytes, ctx);

		if (ctx.FailedAsserts.Count > 0)
		{
			WriteLine(new { ctx.FailedAsserts }.ToJson(true)!);
			throw new Exception();
		}
	}

	public static void AssertFileContent(TestFile file, byte[]? bytes, TestContext ctx)
	{
		AssertContent(new VFileContent(file.FilePath).GetContent(), bytes, ctx, file.FileName);
	}

	public static void AssertContent(byte[] expected, byte[]? result, TestContext ctx, string context)
	{
		ctx.Assert(result != null, $"result is null. {context}");

		ctx.Assert(expected.Length == result!.Length, $"file content Length mismatch. {context}");
		for (var i = 0; i < expected.Length; i++)
		{
			ctx.Assert(expected[i] == result[i], $"content bytes not equal. {context}");
		}
	}

	public static void AssertRequestsVFileInfos(
		List<StoreVFileRequest> requests,
		List<VFileInfo> infos,
		bool expectVersioned,
		TestContext ctx,
		string context)
	{
		ctx.Assert(requests.Count == infos.Count, context);
		var infoPathMap = infos.ToDictionary(x => x.VFilePath.FilePath);

		foreach (var request in requests)
		{
			var info = infoPathMap.GetValueOrDefault(request.Path.FilePath);
			AssertRequestFileInfo(request, info, expectVersioned, ctx, context);
		}
	}

	public static void AssertRequestFileInfo(
		StoreVFileRequest request,
		VFileInfo? info,
		bool expectVersioned,
		TestContext ctx,
		string context)
	{
		ctx.Assert(info != null, $"{context}: info is null");
		ctx.Assert(request.Path.FilePath == info!.VFilePath.FilePath, $"{context}: {request.Path.FilePath} == {info.VFilePath.FilePath}");
		ctx.Assert(expectVersioned ? info.Versioned != null : info.Versioned == null, $"{context}: incorrect Versioned");
	}

	public static void WriteFiles(TestFile file, VFileInfo info, byte[] bytes, string testName)
	{
		var dir = Path.Combine(ResultsDir, testName);
		var (name, _) = Util.FileNameAndExtension(file.FileName);

		var filePath = Path.Combine(dir, file.FileName);
		var vfilePath = Path.Combine(dir, $"vfile_{file.FileName}");
		var vfileInfoPath = Path.Combine(dir, $"VFileInfo_{name}.json");

		Util.WriteFile(filePath, new VFileContent(file.FilePath).GetContent());
		Util.WriteFile(vfilePath, bytes);
		Util.WriteFile(vfileInfoPath, Util.GetBytes(info, true, false));
	}

	public static T ChooseOne<T>(this List<T> list)
	{
		if (list.IsEmpty()) throw new Exception("empty list");
		return list[Rand.Next(0, list.Count)];
	}
}
