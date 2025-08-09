"use client";

import { FileExplorerItemType, Path, VFileDirectory } from "@/types";
import { download } from "@/utils";
import React from "react";
import {
  BackArrow,
  ErrorIcon,
  FileIcon,
  FolderIcon,
  SuccessCheckmarkIcon,
} from "./Icons";

enum ExportStatus {
  None,
  Exporting,
  Success,
  Failed,
}

function FileExplorerItem({
  path,
  name,
  cols,
  itemType,
  onClick,
}: {
  path: string;
  name: string;
  cols: React.ReactNode[];
  itemType: FileExplorerItemType;
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
      <FileExplorerItemIcon itemType={itemType} />
      <div className="text-overflow-ellipsis">{name}</div>
      <div className="ml-auto flex flex-row gap-2">{cols}</div>
    </button>
  );
}

function FileExplorerItemIcon({
  itemType,
}: {
  itemType: FileExplorerItemType;
}) {
  switch (itemType) {
    case FileExplorerItemType.Directory:
      return <FolderIcon size={20} />;
    case FileExplorerItemType.File:
      return <FileIcon size={20} />;
  }
}

function FileExplorerExportDirectory({
  path,
  exportStatus,
  setExportStatus,
  totalSize,
  exportDirectory,
}: {
  path: string;
  exportStatus: ExportStatus;
  setExportStatus: React.Dispatch<React.SetStateAction<ExportStatus>>;
  totalSize?: string;
  exportDirectory: (dirPath: string) => Promise<boolean>;
}) {
  async function exportClick() {
    setExportStatus(ExportStatus.Exporting);
    const result = await exportDirectory(path);
    const status = result ? ExportStatus.Success : ExportStatus.Failed;
    console.log(`exportClick status: ${status}`);
    setExportStatus(status);
    setTimeout(() => {
      console.log("exportClick timeout - reset status to None.");
      setExportStatus(ExportStatus.None);
    }, 5000);
  }

  return (
    <>
      <button
        className="btn btn-primary rounded-full"
        disabled={exportStatus !== ExportStatus.None}
        onClick={async (event) => {
          event.preventDefault();
          await exportClick();
        }}
      >
        Export Directory{totalSize && ` (${totalSize})`}
      </button>
      <div className="flex flex-row gap-1">
        <FileExplorerExportStatusMessage status={exportStatus} />
      </div>
    </>
  );
}

function FileExplorerExportStatusMessage({ status }: { status: ExportStatus }) {
  console.log(`FileExplorerExportStatusMessage status: ${status}`);
  switch (status) {
    case ExportStatus.None:
    case ExportStatus.Exporting:
      return <></>;

    case ExportStatus.Success:
      return (
        <>
          <SuccessCheckmarkIcon className="self-center" />
          <span>Export Complete, check your Downloads folder.</span>
        </>
      );

    case ExportStatus.Failed:
      return (
        <>
          <ErrorIcon className="self-center" />
          <span>Export Failed.</span>
        </>
      );
  }
}

function FileExplorerLoading({ text }: { text: string }) {
  return <div className="text-xl">{text}</div>;
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
  const [exportStatus, setExportStatus] = React.useState(ExportStatus.None);

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

  // load initial path when component loads for the first time
  React.useEffect(() => {
    loadDirectory(initialPath.path, undefined);
  }, [loadDirectory, initialPath]);

  // compile stats that are used into variables that much easier to use in the jsx
  const dirCount = vfileDirectory?.dirs?.length || 0;
  const fileCount = vfileDirectory?.files?.length || 0;
  const dirSize = vfileDirectory?.stats?.totalVFiles.sizeString || "";
  const filesSize = vfileDirectory?.stats?.vFiles.sizeString || "";
  const totalSize = vfileDirectory?.stats?.totalSizeString || "";

  return (
    <div className="border-base-200 flex flex-col gap-2 border-2 p-2 shadow">
      <div className="flex w-full flex-row items-center gap-2">
        <button
          className="btn rounded-full"
          disabled={!!path.prevPath}
          onClick={async () => {
            if (path.prevPath) {
              await loadDirectory(path.prevPath.path, path.prevPath.prevPath);
            }
          }}
        >
          <BackArrow />
        </button>
        <div className="divider divider-horizontal mx-0" />
        <div className="text-overflow-ellipsis w-full px-1 text-xl">
          {path.path}
        </div>
      </div>
      <div className="flex w-full flex-col">
        {isLoading || exportStatus === ExportStatus.Exporting ? (
          <FileExplorerLoading
            text={
              isLoading
                ? "Loading..."
                : "Exporting... This may take a bit depending on the size of the directory."
            }
          />
        ) : (
          <div>
            <div className="mb-3 flex h-10 flex-row items-center gap-2 pl-4">
              <div>
                {dirCount} directories
                {dirCount > 0 && <span>{` (${dirSize})`}</span>}
              </div>
              <div className="divider divider-horizontal mx-0" />
              <div>
                {fileCount} files
                {fileCount > 0 && <span>{` (${filesSize})`}</span>}
              </div>
              <div className="divider divider-horizontal mx-0" />
              <FileExplorerExportDirectory
                path={path.path}
                exportStatus={exportStatus}
                setExportStatus={setExportStatus}
                totalSize={totalSize}
                exportDirectory={exportDirectory}
              />
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
                itemType={FileExplorerItemType.Directory}
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
                itemType={FileExplorerItemType.File}
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
