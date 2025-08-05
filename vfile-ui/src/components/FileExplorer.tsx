import React from "react";
import { IoArrowBack } from "react-icons/io5";
import { IoIosCheckmarkCircle } from "react-icons/io";
import { BiError } from "react-icons/bi";
import { Path, Record, RecordType } from "@/types";
import FileExplorerIcon from "./FileExplorerIcon";
import {
  getDirectory,
  getFileBytes,
  verifyVFilePath,
  VFileDirectory,
} from "@/api";
import SelectVFile from "./SelectVFile";

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

export default function FileExplorer() {
  const initialPath = process.env.NEXT_PUBLIC_VFILE_PATH;
  const [vfilePathVerified, setVFilePathVerified] = React.useState(false);
  const [vfilePath, setVFilePath] = React.useState("");
  const [path, setPath] = React.useState({ path: "/" } as Path);
  const [records, setRecords] = React.useState([] as Record[]);

  const verifyVFile = React.useCallback(
    async (path: string) => {
      console.log(`verifyVFilePath: ${path}`);
      await verifyVFilePath(path).then((res) => {
        setVFilePathVerified(res);
        if (res) {
          console.log(`vfilePath verified: ${path}`);
          setVFilePath(path);
          loadDir({ path: "/" } as Path);
        }
      });
    },
    [loadDir],
  );

  React.useEffect(() => {
    verifyVFile(initialPath || "");
  }, [initialPath, verifyVFile]);

  async function loadVFile(newVFilePath: string) {
    console.log(`loadVFile: ${newVFilePath}`);
    setVFilePath(newVFilePath);
    await loadDir({ path: "/" } as Path);
  }

  async function recordClick(record: Record, currPath: Path): Promise<void> {
    console.log(`recordClick: ${JSON.stringify(record)}`);
    if (record.type === RecordType.Directory) {
      await loadDir({ path: record.path, prevPath: currPath });
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
      console.log("back => loadDir");
      await loadDir(currPath.prevPath);
    }
  }

  async function loadDir(path: Path): Promise<void> {
    console.log(`loadDir: ${path.path}`);
    const dirPath = path.path;
    const dir = await getDirectory({ vfilePath, dir: dirPath });
    const newRecords = compileRecords(dir);
    setPath(path);
    setRecords(newRecords);
  }

  return (
    <>
      <SelectVFile
        initialPath={initialPath}
        vfilePathVerified={vfilePathVerified}
        onVFilePathSubmitted={(newVFilePath) => loadVFile(newVFilePath)}
      />
      <div className="flex w-full flex-row gap-2">
        <button
          className="btn disabled rounded-full"
          disabled={path.prevPath === undefined}
          onClick={() => back(path)}
        >
          <IoArrowBack size={24} />
        </button>
        <div className="w-full px-1 text-2xl">{path.path}</div>
      </div>
      <div className="flex w-full flex-col">
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
