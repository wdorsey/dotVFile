import { getFileBytes, getStats, verifyVFilePath } from "@/api";
import FileExplorerWindow from "./FileExplorerWindow";
import { IoIosCheckmarkCircle } from "react-icons/io";
import { BiError } from "react-icons/bi";
import { getVFileDirectory } from "@/actions";

export default async function FileExplorer() {
  // vfilePath fetched from env.local
  const vfilePath = process.env.NEXT_PUBLIC_VFILE_PATH || "";

  const verified = await verifyVFilePath(vfilePath);
  const stats = await getStats(vfilePath).then((res) => res.result);

  async function getDirectoryCallback(dir: string) {
    "use server";
    return await getVFileDirectory(vfilePath, dir);
  }

  async function getFileBytesCallback(filePath: string) {
    "use server";
    const response = await getFileBytes(vfilePath, filePath);
    return response.result || new Blob([]);
  }

  return (
    <div className="m-auto mt-2 flex w-6xl flex-col gap-2">
      <h1 className="text-2xl underline">dotVFile</h1>
      <dl className="grid grid-cols-[auto_1fr] gap-2 text-xl">
        <dt>Status:</dt>
        <dd>
          {verified.result ? (
            <div className="flex flex-row gap-1">
              <IoIosCheckmarkCircle
                className="fill-success self-center"
                size={24}
              />
              <span>Success!</span>
            </div>
          ) : (
            <div className="flex flex-row gap-1">
              <BiError className="fill-error self-center" size={24} />
              <span>Error. {verified.error?.message}</span>
            </div>
          )}
        </dd>
        <dt>Path:</dt>
        <dd>{vfilePath}</dd>
        {stats ? (
          <>
            <dt>Size:</dt>
            <dd>{stats?.databaseFileSizeString}</dd>
            <dt>VFiles:</dt>
            <dd>
              {stats?.vFiles?.count} vfiles totaling {stats?.vFiles?.sizeString}
            </dd>
            <dt>Versions:</dt>
            <dd>
              {stats?.versions?.count} versioned vfiles totaling{" "}
              {stats?.versions?.sizeString}
            </dd>
            <dt>Content:</dt>
            <dd>
              {stats?.content?.count} stored file contents totaling{" "}
              {stats?.content?.sizeStoredString}
            </dd>
          </>
        ) : (
          <></>
        )}
      </dl>
      {verified.result ? (
        <div className="my-8">
          <FileExplorerWindow
            getVFileDirectory={getDirectoryCallback}
            getVFileBytes={getFileBytesCallback}
          />
        </div>
      ) : (
        <></>
      )}
    </div>
  );
}
