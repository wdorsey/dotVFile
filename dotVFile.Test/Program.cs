using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole(height: 1000);

var versionOpts = new VersionOptions(
	// ExistsBehavior:
	//   Determines what happens when a vfile is requested to be Stored, but already exists at VFilePath.
	//   Options are Overwrite, Error, Version
	//   Default is Overwrite
	VFileExistsBehavior.Overwrite,
	null,  // MaxVersionsRetained: max number of versions to keep. default is null (unlimited)
	null); // TTL: time-to-live for versioned vfiles. default is null (no TTL)

// VFileStoreOptions can be passed in for each individual file that is Stored, if desired.
// But usually the vast majority of Store operations can use the same standard set of options,
// so a default set of options is given to the VFile instance at startup.
// Can also pass in null StoreOptions at startup to use
// the recommended defaults from StoreOptions.Default().
var storeOpts = new StoreOptions(
	// Compression:
	//   Optionally compress the file bytes before storing.
	//   No compression is much faster, but compressing saves disk space.
	//   Default is None
	VFileCompression.None,
	null,         // TTL: time-to-live for vfiles. default is null (no TTL)
	versionOpts); // VFileVersionOptions, see above

var vfilePath = Path.Combine(Environment.CurrentDirectory, "vfile");

var opts = new VFileOptions(
	"dotVFile.Test",       // Name of the VFile instance. null to use default name.
	vfilePath,             // Directory to store VFile's single-file
	TestUtil.ErrorHandler, // Error handler function, pass null to ignore
	storeOpts);            // Default Store options, null will use StoreOptions.Default()

var vfile = new VFile(opts);

// Enables recording of metrics for the currently running process.
// Get via VFile.GetMetrics()
vfile.SetMetricsMode(true);

// Enables debug mode. This is for local development purposes.
vfile.SetDebugMode(true, TestUtil.WriteLine);

// Deletes entire file system. Use cautiously.
vfile.DANGER_WipeData();

TestUtil.LoadTestFiles();
var file = TestUtil.TestFiles.First();

// use a stream to store file
using (FileStream fs = File.OpenRead(file.FilePath))
{
	var path = new VFilePath("stream_directory", file.FileName);

	var result = vfile.Store(new StoreRequest(path, new VFileContent(fs)));

	var bytes = vfile.GetBytes(path);
	bytes = vfile.GetBytes(result.VFiles.Single());
}

TestUtil.RunTests();