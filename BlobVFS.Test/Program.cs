using BlobVFS;
using BlobVFS.Test;

var path = Path.Combine(Environment.CurrentDirectory, "vfs");

VFS vfs = new(new(path, new Callbacks()));

vfs.Go();


