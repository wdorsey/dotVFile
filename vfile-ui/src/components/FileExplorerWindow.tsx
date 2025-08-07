"use client";

import { FileExplorerItemType, Path, VFileDirectory } from "@/types";
import { download } from "@/utils";
import React from "react";
import { IoArrowBack } from "react-icons/io5";
import { FaFile, FaFolderClosed } from "react-icons/fa6";

function FileExplorerItem({
  path,
  name,
  type,
  onClick,
}: {
  path: string;
  name: string;
  type: FileExplorerItemType;
  onClick: () => void;
}) {
  return (
    <button
      className="btn flex w-full flex-row items-center gap-2 text-left"
      key={path}
      type="button"
      onClick={(event) => {
        event.preventDefault();
        onClick();
      }}
    >
      <FileExplorerIcon type={type} />
      <div className="grow">{name}</div>
    </button>
  );
}

function FileExplorerIcon({ type }: { type: FileExplorerItemType }) {
  switch (type) {
    case FileExplorerItemType.Directory:
      return <FaFolderClosed className="fill-primary" />;
    case FileExplorerItemType.File:
      return <FaFile className="fill-secondary" />;
  }
}

function FileExplorerLoading() {
  return <div className="text-xl">Loading...</div>;
}

export default function FileExplorerWindow({
  getVFileDirectory,
  getVFileBytes,
}: {
  getVFileDirectory: (dir: string) => Promise<VFileDirectory>;
  getVFileBytes: (filePath: string) => Promise<Blob>;
}) {
  const initialPath: Path = React.useMemo(() => {
    return { path: "/", prevPath: undefined };
  }, []);

  const [path, setPath] = React.useState(initialPath);
  const [vfileDirectory, setVFileDirectory] = React.useState<VFileDirectory>();
  const [isLoading, setIsLoading] = React.useState(false);

  const loadDirectory = React.useCallback(
    async (dir: string, currPath: Path | undefined) => {
      setPath({ path: dir, prevPath: currPath });
      setIsLoading(true);
      const vfileDirectory = await getVFileDirectory(dir);
      setVFileDirectory(vfileDirectory);
      setIsLoading(false);
    },
    [getVFileDirectory],
  );

  // load initial path when component loads
  React.useEffect(() => {
    loadDirectory(initialPath.path, undefined);
  }, [loadDirectory, initialPath]);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex w-full flex-row gap-2">
        <button
          className="btn disabled rounded-full"
          disabled={path.prevPath === undefined}
          onClick={async () => {
            if (path.prevPath !== undefined) {
              await loadDirectory(path.prevPath.path, path.prevPath.prevPath);
            }
          }}
        >
          <IoArrowBack size={24} />
        </button>
        <div className="w-full px-1 text-2xl">{path.path}</div>
      </div>
      <div className="flex w-full flex-col">
        {isLoading ? (
          <FileExplorerLoading />
        ) : (
          <div>
            {vfileDirectory?.dirs?.map((dir) => (
              <FileExplorerItem
                key={dir.path}
                path={dir.path}
                name={dir.name}
                type={FileExplorerItemType.Directory}
                onClick={async () => await loadDirectory(dir.path, path)}
              />
            ))}
            {vfileDirectory?.files?.map((file) => (
              <FileExplorerItem
                key={file.filePath}
                path={file.filePath}
                name={file.fileName}
                type={FileExplorerItemType.File}
                onClick={async () =>
                  download(file.fileName, await getVFileBytes(file.filePath))
                }
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
