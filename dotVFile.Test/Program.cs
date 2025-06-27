using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole();

var path = Path.Combine(Environment.CurrentDirectory, "vfs");
var opts = new VFSOptions(path, new TestHooks(), null);

VFS vfs = new(opts);
vfs.Destroy();
vfs = new(opts);

var testFileDir = Path.Combine(Environment.CurrentDirectory, "TestFiles");

foreach (var fi in Util.GetFiles(testFileDir, [], false))
{
	vfs.StoreFile(new("test-file"), fi.Name, fi.FullName);
}