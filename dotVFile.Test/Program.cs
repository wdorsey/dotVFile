using dotVFile;
using dotVFile.Test;

var path = Path.Combine(Environment.CurrentDirectory, "vfs");

VFS vfs = new(new(path, new TestHooks()));

vfs.StoreFile(null, "file.json", Util.EmptyBytes(), VFS.DefaultVFileStorageOptions());
vfs.StoreFile(new("dir1", "dir2"), "file2.json", Util.EmptyBytes(), VFS.DefaultVFileStorageOptions());
