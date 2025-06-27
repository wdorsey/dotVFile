using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole();

var path = Path.Combine(Environment.CurrentDirectory, "vfs");

VFS vfs = new(new(path, new TestHooks(), null));

var vfile = vfs.StoreFile(null, "file.json", Util.EmptyBytes());
Console.WriteLine($"Stored file: {vfile!.VFileId}");
vfile = vfs.StoreFile(new("dir1", "dir2"), "file2.json", Util.EmptyBytes());
Console.WriteLine($"Stored file: {vfile!.VFileId}");
var getVfile = vfs.GetFileInfo(vfile.VFileId);
Console.WriteLine($"GetFileInfo:\n{getVfile.ToJson(true)}");

Console.WriteLine($"Attempting to store with empty fileName:");
vfile = vfs.StoreFile(null, string.Empty, Util.EmptyBytes());
Console.WriteLine($"Returned vfile: {vfile}");


// vfileid testing
var ids = new List<string>
{
	@"/file.txt",
	@"/file.txt?v=1234",
	@"/folder/subfolder/file.txt",
	@"/folder/file.txt?v=1234"
};
foreach (var id in ids)
{
	var vfileId = VFS.ParseVFileId(id);
	Console.WriteLine($"{id}:{Environment.NewLine}{vfileId.ToJson(true)}");
}