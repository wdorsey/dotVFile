using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole(height: 1000);

var debug = true; // for local dev

var path = Path.Combine(Environment.CurrentDirectory, "vfile");

var versionOpts = new VFileVersionOptions(
	// ExistsBehavior:
	//   Determines what happens when a vfile is requested to be Stored but already exists.
	//   Options are Overwrite, Error, Version
	//   Default is Overwrite
	VFileExistsBehavior.Overwrite,
	null,  // MaxVersionsRetained: max number of versions to keep. default is null (unlimited)
	null); // TTL: time-to-live for versioned vfiles. default is null (no TTL)


// VFileStoreOptions can be passed-in for each individual file that is Stored, if desired.
// But usually the vast majority of Store operations can use the same standard set of options,
// so a default set of options is given to the VFile instance at startup.
var storeOpts = new VFileStoreOptions(
	// Compression:
	//   Compress the file or not before storing.
	//   No compression is much faster, but compressing saves disk space.
	//   Default is None
	VFileCompression.None,
	null,         // TTL: time-to-live for vfiles. default is null (no TTL)
	versionOpts); // VFileVersionOptions


var opts = new VFileOptions(
	"dotVFile.Test", // Name of the VFile instance
	path,            // Directory to store VFile's single-file
	new TestHooks(), // IVFileHooks implementation, pass null to ignore
	storeOpts,       // Default Store options, null will use VFile.GetDefaultStoreOptions()
					 // Read/Write permissions
	new(VFilePermission.MultiApplication, VFilePermission.SingleApplication),
	debug);          // Debug flag enables Hooks.DebugLog


var vfile = new VFile(opts);

/*
// VFilePermissions example, assuming Multi Read, Single Write:
// opts specifies the exact VFile file location.
// So creating a different instance for that same file would cause 
// vfile2 to now be the recognized instance that owns that VFile.
var vfile2 = new VFile(opts);

// Reading through the first vfile is fine.
vfile.GetVFile(new VFilePath("file.txt"));
	
// But trying to write would cause an exception.
vfile.StoreVFiles(new VFilePath("file.txt"), new VFileContent([])); // error
*/

// wipe data from test VFile at the start of each run
vfile.DANGER_WipeData();

TestUtil.LoadTestFiles();
var file = TestUtil.TestFiles.First();

// use a stream to store file
VFileInfo? info = null;
using (FileStream fs = File.OpenRead(file.FilePath))
{
	info = vfile.StoreVFile(
		new VFilePath("stream_directory", file.FileName),
		new VFileContent(fs));

	vfile.Hooks.DebugLog(info!.ToJson(true)!);
	vfile.GetBytes(info!);
}
TestUtil.AssertFileContent(vfile, file, info);

TestUtil.RunTests(vfile);