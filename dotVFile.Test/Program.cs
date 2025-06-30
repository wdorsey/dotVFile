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
TestUtil.LoadTestFiles();
var storageOpts = VFS.GetDefaultStorageOptions();

TestUtil.RunStandardTest(vfs, storageOpts, "default");

storageOpts.Compression = VFileCompression.Compress;
TestUtil.RunStandardTest(vfs, storageOpts, "compress");

storageOpts.Compression = VFileCompression.None;
storageOpts.TTL = TimeSpan.FromSeconds(1);
TestUtil.RunStandardTest(vfs, storageOpts, "TTL");
// @TODO: clean-up operation to check it deletes TLL

storageOpts.TTL = null;
storageOpts.VersionOpts.Behavior = VFileVersionBehavior.Version;
TestUtil.RunStandardTest(vfs, storageOpts, "Version");

storageOpts.VersionOpts.TTL = TimeSpan.FromSeconds(1);
TestUtil.RunStandardTest(vfs, storageOpts, "Version-TTL");

storageOpts.VersionOpts.MaxVersionsRetained = 1;
TestUtil.RunStandardTest(vfs, storageOpts, "Version-TTL-MaxVersionsRetained_1");
TestUtil.RunStandardTest(vfs, storageOpts, "Version-TTL-MaxVersionsRetained_1");