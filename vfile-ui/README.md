# dotVFile UI

A [Next.js](https://nextjs.org) app that provides read-only browsing of a dotVFile system.

## Features

- File Explorer-like UI of a VFile system.
- Smooth, responsive experience expected of a next.js app.
- Frontend integrates with the [dotVFile c# WebAPI](https://github.com/wdorsey/dotVFile/tree/master/dotVFile.WebAPI) to fetch vfile information.
- Stats provided for directory and vfile counts and sizes.
- Download individual files or export entire directories.
- This is purely read-only functionality. In order to add/update/delete vfiles you'll have to directly use the vfile library.

## Example

![vfile-ui-ss](https://github.com/user-attachments/assets/a1a79ebc-e025-410e-ab8b-51b80629a031)

## Usage

### UI Setup

In the vfile-ui directory (same directory as this file), do the following:

- Create a file named `env.local`.
- Add the following line to the `env.local` file. Replace the value with the path to the vfile database you want to browse.

```env
NEXT_PUBLIC_VFILE_PATH=C:\path\to\file\my-vfile.vfile.db
```

The UI does not currently support choosing a vfile path in the browser. There are several reasons for this, but tl;dr: it would ultimately have to be entered manually, so might as well just save it in a config file since that's WAY easier.

### Run it

- Run the [WebAPI](https://github.com/wdorsey/dotVFile/tree/master/dotVFile.WebAPI) project. It should work as-is, no setup required.
- From the vfile-ui directory, run the command `npm run dev`
- Navigate to [http://localhost:3000/](http://localhost:3000/)
