import { FaFile, FaFolderClosed } from "react-icons/fa6";
import { getDirectories, getFileInfos } from "./api";
import React from "react";

let path: string = "/";

export enum RecordType {
  Directory,
  File,
}

interface Record {
  type: RecordType;
  path: string;
  name: string;
}

function sortRecordNameCompare(a: Record, b: Record): number {
  return a.name.localeCompare(b.name);
}

async function directoryRecords(): Promise<Record[]> {
  const dirs = await getDirectories(path);

  return dirs
    .map((dir) => ({
      type: RecordType.Directory,
      path: dir.path,
      name: dir.name,
    }))
    .sort(sortRecordNameCompare) as Record[];
}

async function fileRecords(): Promise<Record[]> {
  const files = await getFileInfos(path);

  return files
    .map((file) => ({
      type: RecordType.File,
      path: file.path,
      name: file.name,
    }))
    .sort(sortRecordNameCompare) as Record[];
}

function onRecordClick(record: Record): void {
  if (record.type === RecordType.Directory) {
    path = record.path;
  } else {
    // download file
    console.log(record.path);
  }
}

export default async function FileExplorer() {
  const [path] = React.useState("/");
  const dirs = await directoryRecords();
  const files = await fileRecords();
  const records = dirs.concat(files);

  return (
    <>
      <div className="m-auto my-1 w-3xl px-1 text-2xl">Path: {path}</div>
      <div className="m-auto flex w-3xl flex-col">
        {records.map((record) => (
          <button
            className="btn flex w-full flex-row items-center gap-2 text-left"
            key={record.path}
            onClick={(e) => onRecordClick(record)}
          >
            <div>
              {record.type === RecordType.Directory ? (
                <FaFolderClosed className="fill-primary" />
              ) : (
                <FaFile className="fill-secondary" />
              )}
            </div>
            <div className="grow">{record.name}</div>
          </button>
        ))}
      </div>
    </>
  );
}
