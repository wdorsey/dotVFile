# dotVFile
**_dotVFile_** (.NET Virtual File System) is a single-file virtual file system .NET library.
(Currently in active development, possible breaking changes may be made)

### Major Features
- Single Sqlite database file is the **_entire_** file system
- Supports multiple virtual paths per file, with file content stored on disk only once
- Optional versioning, TTL, and compression for every individual vfile
- Operations: Store, Get, Copy, Delete, Move, Export
- Caching functionality provided through GetOrStore
- Super easy to backup and restore, it's just a single file!

For detailed examples, take a look at the [test project](https://github.com/wdorsey/dotVFile/blob/master/dotVFile.Test/Program.cs). It not only runs tests, but serves as a usage guide.

## Documentation

- [Core Types](#core-types)
	- [VDirectory](#vdirectory)

## Core Types

### VDirectory
A VDirectory represents a virtual directory path in the VFile system.

```JSON
{
  "VDirectory": {
    "Name": "c",
    "Path": "/a/b/c/",
    "SystemPath": "a\\b\\c",
    "DirectoryNames": [
      "a",
      "b",
      "c"
    ]
  }
}
```

- Name - The directory name.
- Path - The internal VFile representation of the directory path
- SystemPath - Operating system representation of the path. Handy for exporting files.
- DirectoryNames - Names of each individual directory in the Path, in order. Useful for lots of different operations.

Some notes on VFile paths:
- Path effectively acts as a unique directory identifier.
- The standard VDirectory path uses '/' to divide directories and always has both a leading and trailing divider. 
- The Root directory of every VFile system is '/'. VDirectory.IsRoot indicates that you've reached the root.

But you don't need to worry about fiddling with precisely formatted paths, VDirectory will accept a wide variety of paths and handle converting them to VFile's standard path.
```C#
// all of these result in a VDirectory.Path of "/a/b/c/"
vdir = new VDirectory("/a/b/c/");
vdir = new VDirectory("a/b/c");
vdir = new VDirectory("a\\b\\c");
vdir = new VDirectory("a", "b", "c");
vdir = new VDirectory(Path.Combine("a", "b", "c"));

// DirectoryInfo.Fullname and FileInfo.DirectoryName can be used
// but remember that they do not accept relative paths and will
// automatically attach a drive root.
// These Paths would be: "/C:/.../a/b/c/"
vdir = new VDirectory(new DirectoryInfo("a\\b\\c").FullName);
vdir = new VDirectory(new FileInfo("a\\b\\c\\file.txt").DirectoryName);
```