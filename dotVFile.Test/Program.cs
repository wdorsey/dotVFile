using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole();

var path = Path.Combine(Environment.CurrentDirectory, "vfs");
var opts = new VFSOptions("dotVFile.Test", path, new TestHooks());
VFS vfs = new(opts);

// wipe data from test vfs at the start of each run
vfs.DANGER_WipeData();

/* TESTING */
Util.DeleteDirectoryContent(TestUtil.ResultsDir, true);
var storageOpts = VFS.GetDefaultStorageOptions();

TestUtil.RunStandardTest(vfs, storageOpts, "default");

storageOpts.Compression = VFileCompression.Compress;
TestUtil.RunStandardTest(vfs, storageOpts, "compress");