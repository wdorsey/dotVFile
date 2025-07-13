namespace dotVFile.Test;

public static class TestUtil
{
	public static readonly string LogFilePath = Path.Combine(Environment.CurrentDirectory, "test-log.txt");
	public static readonly Random Rand = new();
	public static string TestFilesDir { get; } = Path.Combine(Environment.CurrentDirectory, "TestFiles");
	public static string TestFileMetadataDir = Path.Combine("test", "metadata");
	public static List<TestFile> TestFiles = [];
	private static bool TestFilesLoaded = false;

	private static void WriteTestResult(TestContext context)
	{
		var result = context.Failures.Count > 0 ? "FAILED" : "passed";
		WriteLine($"{result}...{context.TestName} in {context.Elapsed.TimeString()}");
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

	public static void RunTests()
	{
		Util.DeleteFile(LogFilePath);
		LoadTestFiles();

		var vfile = new VFile(opts =>
		{
			opts.Name = "RunTests";
			opts.Directory = Path.Combine(Environment.CurrentDirectory, "vfile");
			// default store opts
			return opts;
		});

		vfile.SetMetricsMode(true);
		vfile.SetDebugMode(true, WriteLine);

		var results = new List<TestContext>();

		results.AddRange(RunOptionsTests(vfile));
		results.AddRange(RunVFileAPITests(vfile));

		vfile.Tools.LogMetrics();
		WriteLine(vfile.GetStats().ToJson(true)!);

		int passed = 0, failed = 0;
		foreach (var result in results)
		{
			WriteTestResult(result);
			if (result.Failures.Count == 0)
				passed++;
			else
				failed++;
		}

		if (failed > 0)
		{
			var failedMsg = $"=== {failed} TEST{Util.PluralChar(failed, plural: "S")} FAILED ===";
			WriteLine(failedMsg);
			foreach (var result in results)
			{
				if (result.Failures.Count > 0)
				{
					WriteLine(new { result.TestName, result.Failures }.ToJson(true)!);
				}
			}
			WriteLine(failedMsg);
		}
		else
		{
			WriteLine("=== all tests passed ===");
		}
	}

	public static TestContext RunTest(string testName, Action<TestContext> testFn)
	{
		var context = new TestContext(testName);

		try
		{
			var timer = new Timer(string.Empty).Start();

			testFn(context);

			timer.Stop();

			context.Elapsed = timer.Elapsed;
		}
		catch (Exception e)
		{
			context.Failures.Add(e.ToString());
		}

		WriteTestResult(context);

		return context;
	}

	public static List<TestContext> RunOptionsTests(VFile vfile)
	{
		var results = new List<TestContext>();

		StoreOptions Opts() => vfile.GetDefaultStoreOptions();

		var cases = new List<TestCase>()
		{
			new("Default", Opts()),
			new("Compression", Opts().SetCompression(VFileCompression.Compress)),
			new("TTL", Opts().SetTTL(TimeSpan.FromMinutes(1))),
			new("VersionBehavior.Error", Opts().SetExistsBehavior(VFileExistsBehavior.Error)),
			new("VersionBehavior.Version", Opts().SetExistsBehavior(VFileExistsBehavior.Version)),
			new("VersionBehavior.Version2", Opts().SetExistsBehavior(VFileExistsBehavior.Version)
				.SetMaxVersionsRetained(3)
				.SetVersionTTL(TimeSpan.FromMinutes(1))),
		};

		foreach (var @case in cases)
		{
			vfile.DANGER_WipeData();
			var opts = @case.Opts;

			WriteLine(opts.ToJson()!);

			string TestName(string name) => $"{@case.Name} - {name}";

			results.Add(RunTest(TestName("StoreVFiles/GetBytes"), ctx =>
			{
				// test actual content
				// not much here, just verifying Store => Get => Assert bytes match.
				// run 2 times to make sure nothing is wrong with saving the same files.
				var requests = TestFiles.Select(x => new StoreRequest(x.VFilePath, new(x.FilePath), opts)).ToList();
				for (var i = 0; i < 2; i++)
				{
					var vfiles = vfile.Store(requests);
					for (var k = 0; k < vfiles.Count; k++)
					{
						// order of infos should mirror TestFiles
						var info = vfiles[k];
						var file = TestFiles[k];
						var bytes = vfile.GetBytes(info) ?? throw new Exception($"null vfile: {info.VFilePath.FilePath}");
						// write files for debugging
						// WriteFiles(file, vfile, @case.Name);
						AssertFileContent(file, bytes, ctx);
					}
				}
			}));

			var requests = GenerateMetadataRequests(opts, false);
			vfile.Store(requests);

			if (opts.Compression == VFileCompression.Compress)
			{
				results.Add(RunTest(TestName("VFileCompression.Compress"), ctx =>
				{
					var infos = GetMetadataVFileInfos(vfile, requests.Count, ctx);

					foreach (var info in infos)
					{
						ctx.Assert(info.SizeStored <= info.Size, $"Compressed Size is not smaller than SizeStored: {info.VFilePath.FilePath}");
					}
				}));
			}

			if (opts.TTL.HasValue)
			{
				results.Add(RunTest(TestName("opts.TTL"), ctx =>
				{
					var infos = GetMetadataVFileInfos(vfile, requests.Count, ctx);

					foreach (var info in infos)
					{
						ctx.Assert(info.DeleteAt.HasValue, $"DeleteAt null: {info.VFilePath.FilePath}");
					}
				}));
			}

			if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Overwrite)
			{
				results.Add(RunTest(TestName("VFileExistsBehavior.Overwrite"), ctx =>
				{
					// store new files with different content
					requests = GenerateMetadataRequests(opts, true);
					vfile.Store(requests);
					var versions = vfile.GetVersions(new VDirectory(TestFileMetadataDir));
					ctx.Assert(versions.Count == 0, $"versions found w/ Overwrite behavior: versions.Count={versions.Count}");
				}));
			}
			else if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Error)
			{
				results.Add(RunTest(TestName("VFileExistsBehavior.Error"), ctx =>
				{
					// store files at same path with same content, should not error.
					requests = GenerateMetadataRequests(opts, false);
					var result = vfile.Store(requests);
					ctx.Assert(result.Count == requests.Count, "result.VFiles.Count == requests.Count");

					// try to store files at same path but with different content, should error
					try
					{
						requests = GenerateMetadataRequests(opts, true);
						vfile.Store(requests);
						ctx.Assert(false, "Store did not throw exception");
					}
					catch (Exception ex)
					{
						WriteLine(ex.ToString());
					}
				}));
			}
			else if (opts.VersionOpts.ExistsBehavior == VFileExistsBehavior.Version)
			{
				results.Add(RunTest(TestName("VFileExistsBehavior.Version"), ctx =>
				{
					// store new files with different content
					requests = GenerateMetadataRequests(opts, true);
					var result = vfile.Store(requests);
					var versions = vfile.GetVersions(new VDirectory(TestFileMetadataDir));
					ctx.Assert(versions.Count == result.Count, $"Version count mismatch: versions.Count={versions.Count}");

					if (opts.VersionOpts.MaxVersionsRetained.HasValue)
					{
						var max = opts.VersionOpts.MaxVersionsRetained.Value;
						for (var i = 0; i < max + 1; i++)
						{
							// store new files with different content
							requests = GenerateMetadataRequests(opts, true);
							vfile.Store(requests);
						}
						versions = vfile.GetVersions(new VDirectory(TestFileMetadataDir));
						var expected = max * result.Count;
						ctx.Assert(versions.Count == expected, $"MaxVersionsRetained: Expected {expected} versions, got {versions.Count}");
					}

					if (opts.VersionOpts.TTL.HasValue)
					{
						// store new files with different content
						requests = GenerateMetadataRequests(opts, true);
						vfile.Store(requests);
						versions = vfile.GetVersions(new VDirectory(TestFileMetadataDir));
						foreach (var version in versions)
						{
							ctx.Assert(version.DeleteAt.HasValue, $"DeleteAt null: {version.VFilePath.FilePath}");
						}
					}
				}));
			}

			vfile.Clean();
		}

		return results;
	}

	public static List<TestContext> RunVFileAPITests(VFile vfile)
	{
		var results = new List<TestContext>();

		vfile.DANGER_WipeData();

		// store twice to generate versions
		var opts = new StoreOptions(VFileCompression.None, null, new(VFileExistsBehavior.Version, null, null));
		var requests = GenerateMetadataRequests(opts, true);
		vfile.Store(requests);
		requests = GenerateMetadataRequests(opts, true);
		vfile.Store(requests);
		var notFound = "__not_found__";

		var context = "GetVFileInfosByPath";
		results.Add(RunTest(context, ctx =>
		{
			var rq = requests.ChooseOne();
			var info = vfile.Get(rq.Path);
			AssertRequestFileInfo(rq, info, false, ctx, context);

			var infos = vfile.Get([.. requests.Select(x => x.Path)]);
			AssertRequestsVFileInfos(requests, infos, false, ctx, context);

			infos = vfile.GetVersions([.. requests.Select(x => x.Path)], VersionQuery.Versions);
			AssertRequestsVFileInfos(requests, infos, true, ctx, context);

			infos = vfile.GetVersions([.. requests.Select(x => x.Path)], VersionQuery.Both);
			ctx.Assert(infos.Count == requests.Count * 2, context);

			info = vfile.Get(new VFilePath(notFound, notFound));
			ctx.Assert(info == null, "info == null");
		}));

		context = "GetVFileInfosByDirectory";
		results.Add(RunTest(context, ctx =>
		{
			foreach (var rq in requests.GroupBy(x => x.Path.VDirectory.Path))
			{
				var infos = vfile.Get(new VDirectory(rq.Key));
				AssertRequestsVFileInfos([.. rq], infos, false, ctx, context);
			}

			// recursive test
			var result = vfile.Get(new VDirectory(VDirectory.DirectorySeparator.ToString()), true);
			AssertRequestsVFileInfos(requests, result, false, ctx, context);

			result = vfile.Get(new VDirectory(notFound));
			ctx.Assert(result.Count == 0, "result.Count == 0");

			// get Versions by Directory is tested good enough by RunOptionsTests
		}));

		context = "GetBytes";
		results.Add(RunTest(context, ctx =>
		{
			var rq = requests.ChooseOne();
			var expected = rq.Content.GetContent();

			var bytes = vfile.GetBytes(rq.Path);
			AssertContent(expected, bytes, ctx, context);

			var info = vfile.Get(rq.Path);
			bytes = vfile.GetBytes(info!);
			AssertContent(expected, bytes, ctx, context);
		}));

		context = "CopyVFiles";
		results.Add(RunTest(context, ctx =>
		{
			var to = new VDirectory("copy/to/here");

			// single copy
			var rq = requests.ChooseOne();
			var path = new VFilePath(to, rq.Path.FileName);
			var info = vfile.Get(rq.Path)!;
			var copy = vfile.Copy(new CopyRequest(info, path), opts: opts);
			AssertRequestFileInfo(rq with { Path = path }, copy, false, ctx, context);
			AssertContent(vfile.GetBytes(info)!, vfile.GetBytes(copy!), ctx, context);

			// copy all requests
			var infos = vfile.Get([.. requests.Select(x => x.Path)]);
			var copyRequests = infos.Select(x => new CopyRequest(x, new VFilePath(to, x.FileName)));
			var copies = vfile.Copy([.. copyRequests], opts: opts);
			AssertRequestsVFileInfos(
				[.. requests.Select(x => x with { Path = new(to, x.Path.FileName) })],
				copies, false, ctx, $"{context} copy all requests");

			// copy by directory
			to = new VDirectory("copy/by/directory");
			copies = vfile.Copy(new VDirectory(TestFileMetadataDir), to, opts: opts);
			AssertRequestsVFileInfos(
				[.. requests.Select(x => x with { Path = new(to, x.Path.FileName) })],
				copies, false, ctx, $"{context} copy by directory");

			// copy by directory recursive
			var from = new VDirectory("recursive");
			to = new VDirectory("copy/by/directory/recursive");
			string RecursivePath(string subdir) => VDirectory.Join(from, new(subdir)).Path;
			VDirectory ToRecursiveDir(VDirectory fromRequest) => new(fromRequest.Path.Replace(from.Path, to.Path));

			var recursiveRequests = new List<StoreRequest>()
			{
				requests.ChooseOne() with { Path = new(RecursivePath(string.Empty), "file1.txt") },
				requests.ChooseOne() with { Path = new(RecursivePath("a"), "file2.txt") },
				requests.ChooseOne() with { Path = new(RecursivePath("a"), "file3.txt") },
				requests.ChooseOne() with { Path = new(RecursivePath("a/b/c"), "file4.txt") },
				requests.ChooseOne() with { Path = new(RecursivePath("a/b/c/d"), "file5.txt") },
				requests.ChooseOne() with { Path = new(RecursivePath("a/c"), "file6.txt") },
				requests.ChooseOne() with { Path = new(RecursivePath("a/c/d"), "file7.txt") }
			};

			var result = vfile.Store(recursiveRequests);
			ctx.Assert(result.Count == recursiveRequests.Count, "result.VFiles.Count == recursiveRequests.Count");
			copies = vfile.Copy(from, to, true, opts: opts);
			AssertRequestsVFileInfos(
				[.. recursiveRequests.Select(x => x with { Path = new(ToRecursiveDir(x.Path.VDirectory), x.Path.FileName) })],
				copies, false, ctx, $"{context} copy by directory recursive");
		}));

		context = "DeleteVFiles";
		results.Add(RunTest(context, ctx =>
		{
			var count = 5;

			// delete by Path
			var rq = requests.Take(count).ToList();
			var result = vfile.Delete([.. rq.Select(x => x.Path)], VersionQuery.Latest);
			ctx.Assert(result.Count == count, $"{result.Count} == count");
			foreach (var r in rq)
			{
				var found = false;
				foreach (var info in result)
				{
					if (r.Path.FilePath == info.FilePath)
					{
						found = true;
						break;
					}
				}
				ctx.Assert(found, "found");
				requests.Remove(r);
			}

			// delete by Info
			var rqInfos = requests.Take(count).Select(x => vfile.Get(x.Path)!).ToList();
			result = vfile.Delete(rqInfos);
			ctx.Assert(result.Count == count, $"{result.Count} == count");
			foreach (var r in rqInfos)
			{
				var found = false;
				foreach (var info in result)
				{
					if (r.FilePath == info.FilePath)
					{
						found = true;
						break;
					}
				}
				ctx.Assert(found, "found");
			}

			// delete directory, copy everything to a new directory for testing
			var to = new VDirectory("delete-by-directory");
			result = vfile.Copy(new VDirectory("/"), to, true, opts: opts);
			ctx.Assert(result.Count > 0, $"{result.Count} > 0"); // sanity
			var deleteResult = vfile.Delete(to);
			ctx.Assert(result.Count == deleteResult.Count, $"{result.Count} == {deleteResult.Count}");
			result = vfile.Get(to, true);
			ctx.Assert(result.Count == 0, $"{result.Count} == 0");
		}));

		context = "GetOrStore";
		results.Add(RunTest(context, ctx =>
		{
			void AssertCacheResults(
				List<CacheRequest> requests,
				List<CacheTestCase> testCases,
				string test)
			{
				ctx.Assert(requests.Count == testCases.Count, $"{test}: requests.Count == testCases.Count");
				for (var i = 0; i < requests.Count; i++)
				{
					var request = requests[i];
					var @case = testCases[i];
					ctx.Assert(request.Path.Equals(@case.Result.Request.Path), $"{test}: Results order check, Path.Equals");
					ctx.Assert(@case.Result.VFileInfo != null, $"{test}: VFileInfo != null");
					ctx.Assert(@case.Result.Bytes != null, $"{test}: Bytes != null");
					ctx.Assert(@case.Result.CacheHit == @case.ExpectedCacheHit, $"{test}: ExpectedCacheHit");
					ctx.Assert(Util.GetString(@case.Result.Bytes) == Util.GetString(@case.ExpectedContent), $"{test}: ExpectedContent");
				}
			}

			var content = Util.GetBytes("Content");
			VFileContent GetContent()
			{
				return new(content);
			}

			var requests = new List<CacheRequest>();
			var request = new CacheRequest(
				Util.GetBytes("1"),
				new VFilePath("cache-test", "value.txt"),
				GetContent);
			requests.Add(request);

			// first time through should be a cache miss and store the value via getContent
			var test = "Single, Cache Miss";
			var results = vfile.GetOrStore(requests);
			var @case = new CacheTestCase(results.Single(), false, content);
			AssertCacheResults(requests, [@case], test);

			// now we expect it to pull from cache
			test = "Single, Cache Hit";
			@case.Result = vfile.GetOrStore(requests).Single();
			@case.ExpectedCacheHit = true;
			AssertCacheResults(requests, [@case], test);

			// now change content and it should result in a cache miss
			test = "Single, Different Cache Key, Cache Miss";
			request.CacheKey = Util.GetBytes("new-key");
			@case.Result = vfile.GetOrStore(requests).Single();
			@case.ExpectedCacheHit = false;
			AssertCacheResults(requests, [@case], test);

			// dupe test
			test = "Dupe request";
			requests.Add(request with { });
			try
			{
				vfile.GetOrStore(requests);
				ctx.Assert(false, "GetOrStore did not throw exception for duplicate request");
			}
			catch (Exception ex)
			{
				WriteLine(ex.ToString());
			}

			// bulk tests
			test = "Bulk test";
			var dir = new VDirectory("cache-test");
			requests = [.. GenerateMetadataRequests(opts, true).Select(x =>
				new CacheRequest(
					Util.GetBytes(x.Path.FilePath),
					VFilePath.Combine(dir, x.Path),
					() => x.Content))];
			results = vfile.GetOrStore(requests);
			var cases = results.Select(x => new CacheTestCase(x, false, x.Request.ContentFn().GetContent())).ToList();
			AssertCacheResults(requests, cases, test);

			test = "Bulk test, Cache Hit";
			results = vfile.GetOrStore(requests);
			cases = [.. results.Select(x => new CacheTestCase(x, true, x.Request.ContentFn().GetContent()))];
			AssertCacheResults(requests, cases, test);
		}));

		return results;
	}

	private static List<VFileInfo> GetMetadataVFileInfos(VFile vfile, int expectedCount, TestContext ctx)
	{
		var vfiles = vfile.Get(new VDirectory(TestFileMetadataDir));

		ctx.Assert(vfiles.Count == expectedCount, $"GetVFileInfos by directory did not return expected file count. vfiles.Count={vfiles.Count}, expectedCount={expectedCount}");

		return vfiles;
	}

	private static List<StoreRequest> GenerateMetadataRequests(
		StoreOptions opts,
		bool update)
	{
		return [.. TestFiles.Select(x =>
		{
			var (name, _) = Util.FileNameAndExtension(x.FileName);
			return new StoreRequest(
				new VFilePath(TestFileMetadataDir, $"{name}.json"),
				new VFileContent(x.ToBytes(update)),
				opts);
		})];
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
		List<StoreRequest> requests,
		List<VFileInfo> infos,
		bool expectVersioned,
		TestContext ctx,
		string context)
	{
		ctx.Assert(requests.Count == infos.Count, $"AssertRequestsVFileInfos(): {context} - (requests.Count {requests.Count} == {infos.Count} infos.Count)");
		var infoPathMap = infos.ToDictionary(x => x.VFilePath.FilePath);

		foreach (var request in requests)
		{
			var info = infoPathMap.GetValueOrDefault(request.Path.FilePath);
			AssertRequestFileInfo(request, info, expectVersioned, ctx, context);
		}
	}

	public static void AssertRequestFileInfo(
		StoreRequest request,
		VFileInfo? info,
		bool expectVersioned,
		TestContext ctx,
		string context)
	{
		ctx.Assert(info != null, $"{context}: info is null");
		ctx.Assert(request.Path.FilePath == info!.VFilePath.FilePath, $"{context}: {request.Path.FilePath} == {info.VFilePath.FilePath}");
		ctx.Assert(expectVersioned ? info.Versioned != null : info.Versioned == null, $"{context}: incorrect Versioned");
	}

	public static T ChooseOne<T>(this List<T> list)
	{
		if (list.IsEmpty()) throw new Exception("empty list");
		return list[Rand.Next(0, list.Count)];
	}
}
