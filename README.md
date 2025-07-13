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
    - [VFilePath](#vfilepath)
    - [VFileContent](#vfilecontent)
    - [VFileInfo](vfileinfo)

## Core Types

### VDirectory
`VDirectory` represents a virtual directory path in the VFile system.

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

- `Name` is the directory name.
- `Path` is the internal VFile representation of the directory path
- `SystemPath` is the operating system representation of the `Path`. Handy for exporting files.
- `DirectoryNames` holds the names of each individual directory in the `Path`, in order. Useful for lots of different operations.

Some notes on VFile paths:
- `Path` effectively acts as a unique directory identifier.
- The standard VDirectory path uses '/' to divide directories and always has both a leading and trailing divider. 
- The Root directory of every VFile system is '/'. `VDirectory.IsRoot` indicates that you've reached the root.

But you don't need to worry about fiddling with precisely formatted paths, `VDirectory` will accept a wide variety of paths and handle converting them to VFile's standard path.
```C#
// all of these result in a VDirectory.Path of "/a/b/c/"
vdir = new VDirectory("/a/b/c/");
vdir = new VDirectory("a/b/c");
vdir = new VDirectory("a\\b\\c");
vdir = new VDirectory("a", "b", "c");
vdir = new VDirectory(Path.Combine("a", "b", "c"));

// DirectoryInfo.Fullname and FileInfo.DirectoryName can be used but remember 
// that they do not accept relative paths and will automatically attach a drive root.
// These Paths would be: "/C:/.../a/b/c/"
vdir = new VDirectory(new DirectoryInfo("a\\b\\c").FullName);
vdir = new VDirectory(new FileInfo("a\\b\\c\\file.txt").DirectoryName);
```

### VFilePath
`VFilePath` represents the full virtual file path of a vfile.

```JSON
{
  "VFilePath": {
    "VDirectory": { /* see above */ },
    "FileName": "file.txt",
    "FileExtension": ".txt",
    "FilePath": "/a/b/c/file.txt",
    "SystemFilePath": "a\\b\\c\\file.txt"
  }
}
```

- `FilePath` is the unique identifier of each vfile, standardized through [VDirectory](#vdirectory).
- `SystemFilePath` is the operating system representation of the `FilePath`. Handy for exporting files.

As with `VDirectory`, for the most part you don't need to worry about fiddling with filepath formatting, you can create a `VFilePath` in many different ways.

```C#
// all of these result in a VFilePath of "/a/b/c/file.txt"
vfilePath = new VFilePath(new VDirectory("/a/b/c"), "file.txt");
vfilePath = new VFilePath("/a/b/c/file.txt"); // expects exact vfilepath, a system path will not work.
vfilePath = new VFilePath("a/b/c", "file.txt"); // directory is processed through VDirectory

// VFilePath accepts a FileInfo, but as with VDirectory, 
// remember that if you give it a relative path it will 
// attach a drive root: "/C:/.../a/b/c/file.txt"
vfilePath = new VFilePath(new FileInfo("a\\b\\c\\file.txt"));
```

### VFileContent
`VFileContent` holds the file bytes the user needs to store.
```C#
// byte[]
vcontent = new VFileContent(File.ReadAllBytes(filePath));

// filePath
vcontent = new VFileContent(filePath);

// Stream
using (FileStream fs = File.OpenRead(filePath))
{
	vcontent = new VFileContent(fs);
}
```

### VFileInfo
`VFileInfo` contains all data about a vfile except for the content bytes.

- `VFileInfo` is the return value for most VFile API operations.
- `VFileInfo` are created internally by VFile.
- Users do not directly create or modify `VFileInfo`. This is restricted by access modifiers.
- Many API functions take a `VFileInfo` as input for convenience. These functions expect that another API function, like `Get`, was called to get the `VFileInfo`. The user is not expected to create the `VFileInfo`.

```JSON
{
  "VFileInfo": {
    "Id": "11733716-1623-4da8-8d4b-be41e95872fc",
    "VFilePath": { /* see above */ },
    "VDirectory": { /* see above */},
    "FileName": "file.txt",
    "FilePath": "/a/b/c/file.txt",
    "DirectoryName": "c",
    "DirectoryPath": "/a/b/c/",
    "Versioned": null,
    "IsVersion": false,
    "DeleteAt": null,
    "CreationTime": "2025-07-12T20:47:18.5930707-05:00",
    "ContentId": "ce3ce101-0c93-41f5-a992-5c7ae1f9b235",
    "Hash": "41E57BC093FE0249E8F51AE06A44AA3FB99998B609C028C85093F4992AD13291",
    "Size": 7294,
    "SizeStored": 7294,
    "Compression": "None",
    "ContentCreationTime": "2025-07-12T20:47:18.5930707-05:00"
  }
}
```

- `Versioned` holds the versioned datetime. null if the `VFileInfo` is not versioned.
- `DeleteAt` is the datetime at which the vfile will be deleted. This is set via `StoreOptions.TTL` or `VersionOptions.TTL`. null if no TLL set.
- `Hash` of the [VFileContent](#vfilecontent) bytes. Unique identifier for the content bytes.
- `Size` of the [VFileContent](#vfilecontent) bytes.
- `SizeStored` is the size of bytes stored in the database. Only different from `Size` when compression is used.
- `Compression` indicates if compression was used or not.