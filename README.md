# dotVFile
**_dotVFile_** (short for .NET Virtual File System) is a single-file virtual file system .NET library.

### Major Features
- Single Sqlite database file is the **_entire_** file system
- Supports multiple virtual paths per file, with content stored on disk only once
- Optional versioning, compression, and TTL for every vfile
- Optimized for high-performance read/write operations
- Easy to backup and restore, it's just a single file!