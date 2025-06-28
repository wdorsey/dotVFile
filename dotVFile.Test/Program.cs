using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole();

var path = Path.Combine(Environment.CurrentDirectory, "vfs");
var opts = new VFSOptions("dotVFile.Test", path, new TestHooks(), null);

VFS vfs = new(opts);
vfs.DANGER_WipeData();

var testFileDir = Path.Combine(Environment.CurrentDirectory, "TestFiles");

List<StoreVFileRequest> requests = [];
foreach (var fi in Util.GetFiles(testFileDir, [], false))
{
	var filePath = new VFilePath("test-file", fi.Name);
	var bytes = Util.GetFileBytes(fi.FullName);
	requests.Add(new(filePath, bytes));
}

var savePath = Path.Combine(testFileDir, "GetVFile");
foreach (var info in vfs.StoreVFiles(requests))
{
	Console.WriteLine(info.VFileId.ToString());

	var vfile = vfs.GetVFile(info.VFileId) ?? throw new Exception($"null vfile {info.VFileId}");
	var contentPath = Path.Combine(savePath, vfile.FileInfo.Name);
	var (name, _) = Util.FileNameAndExtension(vfile.FileInfo.Name);
	var metadataPath = Path.Combine(savePath, $"metadata_{name}.json");

	Util.WriteFile(contentPath, vfile.Content);
	Util.WriteFile(metadataPath, Util.GetBytes(new { vfile.FileInfo, vfile.DataInfo }, true, false));
}

var storeOpts = VFS.GetDefaultStorageOptions();
storeOpts.TTL = TimeSpan.FromHours(1);
vfs.StoreVFile(new VFilePath("ttl", "ttl.txt"), Util.EmptyBytes(), storeOpts);