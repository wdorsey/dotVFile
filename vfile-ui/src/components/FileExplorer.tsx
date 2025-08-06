"use client";

import { getVFileDirectory } from "@/actions";
import { FileExplorerItemType, Path, VFileDirectory } from "@/types";
import FileExplorerIcon from "./FileExplorerIcon";
import { download } from "@/utils";
import { getFileBytes } from "@/api";
import React from "react";
import { IoArrowBack } from "react-icons/io5";

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

function FileExplorerWindow({ vfilePath }: { vfilePath: string }) {
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
      const vfileDirectory = await getVFileDirectory(vfilePath, dir);
      setVFileDirectory(vfileDirectory);
      setIsLoading(false);
    },
    [vfilePath],
  );

  // load initial path when component loads
  React.useEffect(() => {
    loadDirectory(initialPath.path, undefined);
  }, [loadDirectory, initialPath]);

  return (
    <>
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
            {vfileDirectory?.dirs.map((dir) => (
              <FileExplorerItem
                key={dir.path}
                path={dir.path}
                name={dir.name}
                type={FileExplorerItemType.Directory}
                onClick={async () => await loadDirectory(dir.path, path)}
              />
            ))}
            {vfileDirectory?.files.map((file) => (
              <FileExplorerItem
                key={file.path}
                path={file.path}
                name={file.name}
                type={FileExplorerItemType.File}
                onClick={async () =>
                  download(
                    file.name,
                    await getFileBytes(vfilePath, file.name, file.path),
                  )
                }
              />
            ))}
          </div>
        )}
      </div>
    </>
  );
}

function FileExplorerLoading() {
  return <div className="text-xl">Loading...</div>;
}

export default function FileExplorer() {
  // vfilePath fetched from env.local
  const vfilePath = process.env.NEXT_PUBLIC_VFILE_PATH || "";

  return (
    <>
      <div className="text-xl">VFile Path: {vfilePath}</div>
      <FileExplorerWindow vfilePath={vfilePath} />
    </>
  );
}
