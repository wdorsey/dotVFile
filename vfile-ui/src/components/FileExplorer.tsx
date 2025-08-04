"use client";

import { getDirectory, getFileBytes, VFileDirectory } from "@/api";
import { Path, Record, RecordType } from "@/types";
import React from "react";
import FileExplorerIcon from "./FileExplorerIcon";
import { IoArrowBack } from "react-icons/io5";

export default function FileExplorer({
  initialDir,
}: {
  initialDir: VFileDirectory;
}) {
  function compileRecords(dir: VFileDirectory): Record[] {
    function sortRecordNameCompare(a: Record, b: Record): number {
      return a.name.localeCompare(b.name);
    }

    const dirRecords = dir.dirs
      .map((dir) => ({
        type: RecordType.Directory,
        path: dir.path,
        name: dir.name,
      }))
      .sort(sortRecordNameCompare) as Record[];

    const fileRecords = dir.files
      .map((file) => ({
        type: RecordType.File,
        path: file.path,
        name: file.name,
      }))
      .sort(sortRecordNameCompare) as Record[];

    return dirRecords.concat(fileRecords);
  }

  const [path, setPath] = React.useState({ path: initialDir.path } as Path);
  const [records, setRecords] = React.useState(compileRecords(initialDir));

  async function recordClick(record: Record, currPath: Path): Promise<void> {
    console.log(`recordClick: ${JSON.stringify(record)}`);
    if (record.type === RecordType.Directory) {
      await loadDir(record.path, currPath);
    } else {
      // download file
      console.log(record.path);
      const blob = await getFileBytes(record.name, record.path);
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = record.name;
      document.body.appendChild(link);
      link.click();
      window.URL.revokeObjectURL(url);
      link.remove();
    }
  }

  async function back(currPath: Path): Promise<void> {
    if (currPath.prevPath !== undefined) {
      loadDir(currPath.prevPath.path, currPath.prevPath.prevPath);
    }
  }

  async function loadDir(
    dirPath: string,
    prevPath: Path | undefined,
  ): Promise<void> {
    console.log(`load dir ${dirPath}`);
    const dir = await getDirectory(dirPath);
    const newRecords = compileRecords(dir);

    setPath({ path: dirPath, prevPath: prevPath } as Path);
    setRecords(newRecords);
  }

  return (
    <>
      <div className="m-auto mt-2 flex w-3xl flex-row gap-2">
        <button
          className="btn disabled rounded-full"
          disabled={path.prevPath === undefined}
          onClick={() => back(path)}
        >
          <IoArrowBack size={24} />
        </button>
        <div className="m-auto w-3xl px-1 text-2xl">{path.path}</div>
      </div>
      <div className="m-auto mt-2 flex w-3xl flex-col">
        {records.map((record) => (
          <button
            className="btn flex w-full flex-row items-center gap-2 text-left"
            key={record.path}
            type="button"
            onClick={() => recordClick(record, path)}
          >
            <FileExplorerIcon type={record.type} />
            <div className="grow">{record.name}</div>
          </button>
        ))}
      </div>
    </>
  );
}
