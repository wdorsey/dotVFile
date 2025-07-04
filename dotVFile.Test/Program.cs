using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole(height: 1000);

var debug = false; // for local dev

var path = Path.Combine(Environment.CurrentDirectory, "vfs");

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
// so a default set of options is given to the VFS instance at startup.
var storeOpts = new VFileStoreOptions(
	// Compression:
	//   Compress the file or not before storing.
	//   No compression is much faster, but compressing saves disk space.
	//   Default is None
	VFileCompression.None,
	null,         // TTL: time-to-live for vfiles. default is null (no TTL)
	versionOpts); // VFileVersionOptions


var opts = new VFileSystemOptions(
	"dotVFile.Test", // Name of the VFS instance
	path,            // Directory to store VFS's single-file
	new TestHooks(), // IVFileHooks implementation, pass null to ignore
	storeOpts,       // Default Store options, null will use VFS.GetDefaultStoreOptions()
	true,            // Enforces that only a single application instance can access the VFS at a time
	debug);          // Debug flag enables Hooks.DebugLog


var vfs = new VFileSystem(opts);

/*
EnforceSingleInstance example:
  opts specifies the exact VFS file location.
  So creating a different instance would cause vfs2
  to now be the recognized instance that owns 
  the VFS at the location specified by opts.
VFS vfs2 = new(opts);
	
  Trying to access the VFS via the first vfs instance,
  which no longer is the owner, would cause an exception.
vfs.GetVFiles(); // error
*/

// wipe data from test vfs at the start of each run
vfs.DANGER_WipeData();

TestUtil.LoadTestFiles();
var file = TestUtil.TestFiles.First();

// use a stream to store file
VFileInfo? info = null;
using (FileStream fs = File.OpenRead(file.FilePath))
{
	info = vfs.StoreVFile(
		new VFilePath("stream_directory", file.FileName),
		new VFileContent(fs));

	vfs.Hooks.DebugLog(info!.ToJson(true)!);
	vfs.GetBytes(info!);
}
TestUtil.AssertFileContent(vfs, file, info);

TestUtil.RunTests(vfs);