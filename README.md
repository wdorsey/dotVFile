# dotVFile
**_dotVFile_** (.NET Virtual File System) is a single-file virtual file system .NET library.

### Major Features
- Single Sqlite database file is the **_entire_** file system
- Supports multiple virtual paths per file, with file content stored on disk only once
- Optional versioning, TTL, and compression for every individual vfile
- Caching functionality provided through GetOrStore functions.
- Optimized for high-performance read/write operations
- Super easy to backup and restore, it's just a single file!