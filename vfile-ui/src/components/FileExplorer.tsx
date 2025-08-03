"use client";

import { getDirectory, VFileDirectory } from "@/api";
import { Path, Record, RecordType } from "@/types";
import React from "react";
import FileExplorerIcon from "./FileExplorerIcon";

export default function FileExplorer({
  initialPath,
  initialDir,
}: {
  initialPath: string;
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

  const [path, setPath] = React.useState({ path: initialPath } as Path);
  const [records, setRecords] = React.useState(compileRecords(initialDir));

  async function recordClick(record: Record): Promise<void> {
    console.log(record.path);
    if (
      record.type === RecordType.Directory ||
      record.type === RecordType.MoveBack
    ) {
      const newPath =
        record.type === RecordType.Directory
          ? ({ path: record.path, prevPath: path } as Path)
          : ({ path: record.path, prevPath: path.prevPath } as Path);

      console.log(JSON.stringify(newPath));
      const dir = await getDirectory(record.path);
      const newRecords = compileRecords(dir);

      if (record.path !== "/") {
        newRecords.unshift({
          type: RecordType.MoveBack,
          path: newPath.prevPath || "/",
          name: "",
        } as Record);
      }

      setPath(newPath);
      setRecords(newRecords);
    } else {
      // download file
      console.log(record.path);
    }
  }

  return (
    <>
      <div className="m-auto my-1 w-3xl px-1 text-2xl">{path.path}</div>
      <div className="m-auto flex w-3xl flex-col">
        {records.map((record) => (
          <button
            className="btn flex w-full flex-row items-center gap-2 text-left"
            key={record.path}
            onClick={() => recordClick(record)}
          >
            <FileExplorerIcon type={record.type} />
            <div className="grow">{record.name}</div>
          </button>
        ))}
      </div>
    </>
  );
}
