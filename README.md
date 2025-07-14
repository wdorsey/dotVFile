# dotVFile
**_dotVFile_** (.NET Virtual File System) is a single-file virtual file system .NET library.

**!!! Currently in very active development, possible breaking changes may be made !!!**

### Major Features
- Single Sqlite database file is the **_entire_** virtual file system
- Supports multiple virtual paths per file, with file content stored on disk only once
- Optional versioning, TTL, and compression for every individual vfile
- Operations: Store, Get, Copy, Delete, Move, Export
- Caching functionality provided through GetOrStore
- Super easy to backup and restore, it's just a single file!

For detailed examples, take a look at the [test project](https://github.com/wdorsey/dotVFile/blob/master/dotVFile.Test/Program.cs). It not only runs tests, but serves as a usage guide.

### Core Concepts

- The basic idea of a virtual file system is to internally separate file paths and file content but maintain a link between them.
- This allows the file paths and content to be separately customized and optimized while still allowing the user to get the file bytes for a given file path, just like a normal file system.

## Documentation

- [Core Types](#core-types)
	- [VDirectory](#vdirectory)
    - [VFilePath](#vfilepath)
    - [VFileContent](#vfilecontent)
    - [VFileInfo](#vfileinfo)
    - [StoreOptions](#storeoptions)
- [API](#api)
    - [Error Handling](#error-handling)
    - [VFile](#vfile)
    - [Store](#store)

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
- `Path` is the internally standardized VFile representation of the directory path.
- `SystemPath` is the operating system representation of the `Path`. Handy for exporting files.
- `DirectoryNames` holds the names of each individual directory in the `Path`, in order. Useful for lots of various functionality.

Some notes on `VDirectory` paths:
- `Path` effectively acts as a unique directory identifier.
- A `VDirectory` path uses '/' to divide directories and always has both a leading and trailing divider. 
- The Root directory of every VFile system is '/'. `VDirectory.IsRoot` indicates that you've reached the root.
- `VDirectory.ParentDirectory()` will let you traverse up the directory path.

But you don't need to worry about fiddling with precisely formatted paths, `VDirectory` will accept a wide variety of paths and handle converting them to VFile's standard path:
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

- `FilePath` is the unique identifier of each vfile. The path is standardized through [VDirectory](#vdirectory).
- `SystemFilePath` is the operating system representation of the `FilePath`. Handy for exporting files.

As with [VDirectory](#vdirectory), for the most part you don't need to worry about fiddling with filepath formatting, you can create a `VFilePath` in a few different ways:
```C#
// all of these result in a VFilePath of "/a/b/c/file.txt"
vfilePath = new VFilePath(new VDirectory("/a/b/c"), "file.txt");
vfilePath = new VFilePath("/a/b/c/file.txt");   // expects vfilepath, a system path will not work.
vfilePath = new VFilePath("a/b/c", "file.txt"); // directory is processed through VDirectory

// VFilePath accepts a FileInfo, but as with VDirectory, 
// remember that if you give it a relative path it will 
// attach a drive root: "/C:/.../a/b/c/file.txt"
vfilePath = new VFilePath(new FileInfo("a\\b\\c\\file.txt"));
```

### VFileContent
`VFileContent` holds the file bytes the user requests to store.
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
`VFileInfo` contains all information about a vfile.

- `VFileInfo` is the return value for most VFile API operations.
- `VFileInfo` are created internally by VFile. Users do not directly create or modify `VFileInfo`.
- Many API operations have functions that take `VFileInfo` as input for convenience. These functions expect that another API function, like `Get`, was called to get the `VFileInfo`. The user is not expected to create the `VFileInfo`.

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

- `Versioned` holds the versioned datetime. null if the vfile is not versioned.
- `DeleteAt` is the datetime at which the vfile will be deleted. This is set via `StoreOptions.TTL` or `VersionOptions.TTL`. null if no TLL set.
- `Hash` of the [VFileContent](#vfilecontent) bytes. Unique identifier for the file content.
- `Size` of the [VFileContent](#vfilecontent) bytes.
- `SizeStored` is the size of bytes stored in the database. Only different from `Size` when compression is used.
- `Compression` indicates if compression was used or not.

### StoreOptions
`StoreOptions` specify how to store each individual vfile.

- A default `StoreOptions` is defined during VFile initialization as the majority of use-cases will use the same settings.
- Each individual vfile `StoreRequest` can have it's own custom `StoreOptions`, but if none are provided the default is used.
- VFile recommended default is available via `StoreOptions.Default()`

```JSON
{
  "StoreOptions": {
    "Compression": "None",
    "TTL": null,
    "VersionOpts": {
      "ExistsBehavior": "Overwrite",
      "MaxVersionsRetained": null,
      "TTL": null
    }
  }
}
```

- `Compression` specifies if the file contents should be compressed or not.
- `TTL` is a `TimeSpan?` that specifies the time-to-live for the vfile. null means no TTL.
- `VersionOpts` specifies how to handle situations where a store operation is requested for a vfile that already exists.
- `VersionOpts.ExistsBehavior` defines the overall behavior. Values are `Overwrite`, `Version`, and `Error`.
- `VersionOpts.MaxVersionsRetained` is the maximum number of versions retained. Only applies when `VersionOpts.ExistsBehavior` is `Version`.
- `VersionOpts.TTL` is the time-to-live for versions. Only applies when `VersionOpts.ExistsBehavior` is `Version`.

## API

### Error Handling
- Any error that occurs will result in an exception being thrown. This includes both known error states and unhandled exceptions.
- VFile API function documentation specifies all exceptions they will throw for known error states.

### VFile
A `VFile` instance is created with user-specified `VFileOptions`. Every operation is carried out through the `VFile` API.

```JSON
{
  "VFileOptions": {
    "Name": "dotVFile.Test",
    "Directory": "C:\\vfile",
    "DefaultStoreOptions": {
      "Compression": "None",
      "TTL": null,
      "VersionOpts": {
        "ExistsBehavior": "Overwrite",
        "MaxVersionsRetained": null,
        "TTL": null
      }
    }
  }
}
```

- `Name` is the name of the `VFile` instance and will be used to name the database file. It can be left null and the default name "dotVFile" will be used.
- `Directory` is the directory where the `VFile` database file will be created.
- `DefaultStoreOptions` is the default `StoreOptions`. See [StoreOptions](#storeoptions) for details.

Example:
```C#
var opts = new VFileOptions(
	"dotVFile.Test", // Name of the VFile instance. null to use default name.
	"C:\\vfile",     // Directory to store VFile's single-file
	storeOpts);      // Default Store options, null will use StoreOptions.Default()

var vfile = new VFile(opts);

// Configuring via a func is also available. Passed-in opts is StoreOptions.Default()
var vfile = new VFile(opts =>
{
	opts.Name = "dotVFile.Test";
	opts.Directory = "C:\\vfile";
	return opts;
});
```

### Store
Stores a file and it's contents.

```C#
var vfileInfo = vfile.Store(
	new VFilePath("a/b/c", "file.txt"),
	new VFileContent(filePath));

// or via a request
vfileInfo = vfile.Store(
	new StoreRequest(
		new VFilePath("a/b/c", "file.txt"),
		new VFileContent(filePath)));
```

Store also works in bulk, as do most operations.

Here are all Store method signatures:
```C#
VFileInfo Store(VFilePath path, VFileContent content, StoreOptions? opts = null) { }
VFileInfo Store(StoreRequest request) { }
List<VFileInfo> Store(List<StoreRequest> requests) { }
```