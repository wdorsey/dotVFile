using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole(height: 1000);

// initialize some variables for use later
var vdir = VDirectory.Default();
var vfilePath = VFilePath.Default();
var vcontent = VFileContent.Default();

var versionOpts = new VersionOptions(
	// ExistsBehavior:
	//   Determines what happens when a vfile is requested to be Stored, but already exists at VFilePath.
	//   Options are Overwrite, Error, Version
	//   Default is Overwrite
	VFileExistsBehavior.Overwrite,
	null,  // MaxVersionsRetained: max number of versions to keep. default is null (unlimited)
	null); // TTL: time-to-live for versioned vfiles. default is null (no TTL)

// StoreOptions can be passed in for each individual file that is Stored, if desired.
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
	versionOpts); // VersionOptions, see above

var path = Path.Combine(Environment.CurrentDirectory, "vfile");

var opts = new VFileOptions(
	"dotVFile.Test",       // Name of the VFile instance. null to use default name.
	path,                  // Directory to store VFile's single-file
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

/* Core Types */

// VDirectory
// all of these result in a VDirectory.Path of "/a/b/c/"
vdir = new VDirectory("/a/b/c/");
vdir = new VDirectory("a/b/c");
vdir = new VDirectory("a\\b\\c");
vdir = new VDirectory("a", "b", "c");
vdir = new VDirectory(Path.Combine("a", "b", "c"));

// DirectoryInfo.Fullname and FileInfo.DirectoryName can be used but remember 
// that they do not accept relative paths and will automatically attach a drive root.
// These Paths would be: "/C:/.../a/b/c/"
vdir = new VDirectory(new DirectoryInfo("a\\b\\c").FullName);
vdir = new VDirectory(new FileInfo("a\\b\\c\\file.txt").DirectoryName);

// VFilePath
// all of these result in a VFilePath of "/a/b/c/file.txt"
vfilePath = new VFilePath(new VDirectory("/a/b/c"), "file.txt");
vfilePath = new VFilePath("/a/b/c/file.txt"); // expects vfilepath, a system path will not work.
vfilePath = new VFilePath("a/b/c", "file.txt"); // directory is processed through VDirectory

// VFilePath accepts a FileInfo, but as with VDirectory, 
// remember that if you give it a relative path it will 
// attach a drive root: "/C:/.../a/b/c/file.txt"
vfilePath = new VFilePath(new FileInfo("a\\b\\c\\file.txt"));

// VContent
// accepts byte[], filePath, or Stream
var filePath = Path.Combine(TestUtil.TestFilesDir, "test-file-1.json");
vcontent = new VFileContent(File.ReadAllBytes(filePath));
vcontent = new VFileContent(filePath);
using (FileStream fs = File.OpenRead(filePath))
{
	vcontent = new VFileContent(fs);
}

//vfilePath = new VFilePath("/a/b/c/file.txt");
//vcontent = new VFileContent(filePath);
//var result = vfile.Store(vfilePath, vcontent);
//var vfileInfo = result.VFiles.Single();

TestUtil.RunTests();