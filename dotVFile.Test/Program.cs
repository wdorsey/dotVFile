using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole();

var path = Path.Combine(Environment.CurrentDirectory, "vfs");
var opts = new VFSOptions(path, new TestHooks(), null);

new VFS(opts).Destroy();
VFS vfs = new(opts);

var testFileDir = Path.Combine(Environment.CurrentDirectory, "TestFiles");

foreach (var fi in Util.GetFiles(testFileDir, [], false))
{
	var filePath = new VFilePath("test-file", fi.Name);
	var bytes = Util.GetFileBytes(fi.FullName);
	var vfile = vfs.StoreVFile(filePath, bytes);
	if (vfile != null)
		Console.WriteLine(vfile.VFileId.ToString());
}

var storeOpts = VFS.GetDefaultStorageOptions();
storeOpts.TTL = TimeSpan.FromHours(1);
vfs.StoreVFile(new VFilePath("ttl", "ttl.txt"), Util.EmptyBytes(), storeOpts);