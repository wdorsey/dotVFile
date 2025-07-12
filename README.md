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