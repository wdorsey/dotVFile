"use client";

import { FileExplorerItemType, Path, VFileDirectory } from "@/types";
import { download } from "@/utils";
import React from "react";
import { BackArrow, FileIcon, FolderIcon, SuccessCheckmarkIcon } from "./Icons";

function FileExplorerItem({
  path,
  name,
  cols,
  type,
  onClick,
}: {
  path: string;
  name: string;
  cols: React.ReactNode[];
  type: FileExplorerItemType;
  onClick: () => void;
}) {
  return (
    <button
      className="btn flex w-full flex-row items-center justify-start gap-2 text-left"
      key={path}
      type="button"
      onClick={(event) => {
        event.preventDefault();
        onClick();
      }}
    >
      <FileExplorerIcon type={type} />
      <div className="text-overflow-ellipsis">{name}</div>
      <div className="ml-auto flex flex-row gap-2">{cols}</div>
    </button>
  );
}

function FileExplorerIcon({ type }: { type: FileExplorerItemType }) {
  switch (type) {
    case FileExplorerItemType.Directory:
      return <FolderIcon size={20} />;
    case FileExplorerItemType.File:
      return <FileIcon size={20} />;
  }
}

function FileExplorerLoading({ text }: { text: string }) {
  return <div className="text-xl">{text}</div>;
}

function DownloadComplete() {
  return (
    <div className="flex flex-row gap-1">
      <SuccessCheckmarkIcon className="self-center" />
      <span>Download Complete, check your Downloads folder.</span>
    </div>
  );
}

export default function FileExplorerWindow({
  getVFileDirectory,
  getVFileBytes,
  exportDirectory,
}: {
  getVFileDirectory: (dir: string) => Promise<VFileDirectory>;
  getVFileBytes: (filePath: string) => Promise<Blob>;
  exportDirectory: (dirPath: string) => Promise<boolean>;
}) {
  const initialPath: Path = React.useMemo(() => {
    return { path: "/", prevPath: undefined };
  }, []);

  const [path, setPath] = React.useState(initialPath);
  const [vfileDirectory, setVFileDirectory] = React.useState<VFileDirectory>();
  const [isLoading, setIsLoading] = React.useState(false);
  const [isExporting, setIsExporting] = React.useState(false);
  const [showDownloadMessage, setShowDownloadMessage] = React.useState(false);

  const loadDirectory = React.useCallback(
    async (dir: string, currPath: Path | undefined) => {
      setPath({ path: dir, prevPath: currPath });
      setIsLoading(true);
      const vfileDirectory = await getVFileDirectory(dir);
      setVFileDirectory(vfileDirectory);
      setIsLoading(false);
      setShowDownloadMessage(false);
    },
    [getVFileDirectory],
  );

  // load initial path when component loads
  React.useEffect(() => {
    loadDirectory(initialPath.path, undefined);
  }, [loadDirectory, initialPath]);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex w-full flex-row items-center gap-2">
        <button
          className="btn rounded-full"
          disabled={path.prevPath === undefined}
          onClick={async () => {
            if (path.prevPath !== undefined) {
              await loadDirectory(path.prevPath.path, path.prevPath.prevPath);
            }
          }}
        >
          <BackArrow />
        </button>
        <div className="text-overflow-ellipsis w-full px-1 text-xl">
          {path.path}
        </div>
      </div>
      <div className="flex w-full flex-col">
        {isLoading || isExporting ? (
          <FileExplorerLoading
            text={
              isLoading
                ? "Loading..."
                : "Downloading... This may take a bit depending on the size of the directory."
            }
          />
        ) : (
          <div>
            <div className="mb-2 flex h-10 flex-row items-center gap-2 pl-4">
              <div>{vfileDirectory?.dirs?.length} directories</div>
              <div className="divider divider-horizontal mx-0" />
              <div>
                {vfileDirectory?.files?.length} files
                {vfileDirectory?.stats !== undefined &&
                vfileDirectory.stats.vFiles.count > 0 ? (
                  <>{` (${vfileDirectory?.stats.vFiles.sizeString})`}</>
                ) : (
                  <></>
                )}
              </div>
              <div className="divider divider-horizontal mx-0" />
              <button
                className="btn btn-primary rounded-full"
                disabled={showDownloadMessage}
                onClick={async () => {
                  setIsExporting(true);
                  await exportDirectory(path.path);
                  setIsExporting(false);
                  setShowDownloadMessage(true);
                  setTimeout(() => {
                    setShowDownloadMessage(false);
                  }, 5000);
                }}
              >
                Download Directory
              </button>
              {showDownloadMessage ? <DownloadComplete /> : <></>}
            </div>
            {vfileDirectory?.dirs?.map((dir) => (
              <FileExplorerItem
                key={dir.directory.path}
                path={dir.directory.path}
                name={dir.directory.name}
                cols={[
                  <div
                    key={`${dir.directory.path}-sizeString`}
                    className="text-overflow-ellipsis w-20 text-right"
                  >
                    {dir.stats.totalVFiles.sizeString}
                  </div>,
                  <div
                    key={`${dir.directory.path}-vd1`}
                    className="divider divider-horizontal mx-0"
                  />,
                  <div
                    key={`${dir.directory.path}-directoryCount`}
                    className="text-overflow-ellipsis w-24 text-right"
                  >
                    {dir.stats.directoryCount.toLocaleString()} dirs
                  </div>,
                  <div
                    key={`${dir.directory.path}-vd2`}
                    className="divider divider-horizontal mx-0"
                  />,
                  <div
                    key={`${dir.directory.path}-vFiles-count`}
                    className="text-overflow-ellipsis w-24 text-right"
                  >
                    {dir.stats.vFiles.count.toLocaleString()} files
                  </div>,
                ]}
                type={FileExplorerItemType.Directory}
                onClick={async () =>
                  await loadDirectory(dir.directory.path, path)
                }
              />
            ))}
            {vfileDirectory?.files?.map((file) => (
              <FileExplorerItem
                key={file.filePath}
                path={file.filePath}
                name={file.fileName}
                cols={[
                  <div key={`${file.filePath}-sizeStoredString`}>
                    {file.sizeStoredString}
                  </div>,
                ]}
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
