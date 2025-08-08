# dotVFile UI

A [Next.js](https://nextjs.org) app that provides read-only browsing of a dotVFile system. Uses the C# dotVFile.WebAPI (dotVFile library wrapper) to pull vfile info.

## Features

- File Explorer-like UI of a VFile system.
- Smooth, responsive experience expected of a next.js app.
- Frontend integrates with a C# WebAPI to fetch vfile information.
- Stats provided for directory and vfile counts and sizes.
- Download individual files or export entire directories.

@TODO: add image(s)

## Usage

- @TODO

## TODO

- exportDirectory - use result of api call
  - change showDownloadMessage to exportStatus (None | Exporting | Finished)
  - change component DownloadComplete to ExportMessage
- there is no reason anymore to wrap the api calls in FileExplorer, just pass vfilePath to FileExplorerWindow.
- add .env to source code
- remove console.logs
