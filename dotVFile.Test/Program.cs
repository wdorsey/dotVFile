using dotVFile;
using dotVFile.Test;

ConsoleUtil.InitializeConsole(height: 1000);

var path = Path.Combine(Environment.CurrentDirectory, "vfs");
var opts = new VFSOptions("dotVFile.Test", path, new TestHooks());
VFS vfs = new(opts);

// wipe data from test vfs at the start of each run
vfs.DANGER_WipeData();

TestUtil.LoadTestFiles();
var file = TestUtil.TestFiles.First();

// use stream to store file
VFileInfo? info = null;
using (FileStream fs = File.OpenRead(file.FilePath))
{
	info = vfs.StoreVFile(
		new VFilePath("stream_directory", file.FileName),
		new VFileContent(fs));

	var vfile = vfs.GetVFile(info!);

	Console.WriteLine(vfile!.VFileInfo.ToJson(true));
}
TestUtil.AssertFileContent(vfs, file, info);

TestUtil.RunTests(vfs);