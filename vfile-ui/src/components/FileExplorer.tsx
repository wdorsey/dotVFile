import {
  exportDirectory,
  getFileBytes,
  getStats,
  verifyVFilePath,
} from "@/api";
import FileExplorerWindow from "./FileExplorerWindow";
import { getVFileDirectory } from "@/actions";
import { ErrorIcon, SuccessCheckmarkIcon } from "./Icons";

export default async function FileExplorer() {
  // vfilePath fetched from env.local
  const vfilePath = process.env.NEXT_PUBLIC_VFILE_PATH || "";

  const verified = await verifyVFilePath(vfilePath);
  const stats = await getStats(vfilePath).then((res) => res.result);

  // wrap these api calls in "use server" functions in order to pass them to the client component.
  // we wrap these and pass them to the FileExplorer component because the api is marked "server-only"
  // because the api url is in a private environment variable that cannot be exposed on the client.
  // (simulates real-world enviroment variable handling).
  async function getDirectoryCallback(dir: string) {
    "use server";
    return await getVFileDirectory(vfilePath, dir);
  }

  async function getFileBytesCallback(filePath: string) {
    "use server";
    const response = await getFileBytes(vfilePath, filePath);
    return response.result || new Blob([]);
  }

  async function exportDirectoryCallback(dirPath: string) {
    "use server";
    const response = await exportDirectory(vfilePath, dirPath);
    return response.result !== undefined;
  }

  return (
    <div className="m-auto mt-2 flex w-5xl flex-col gap-2">
      <div className="border-base-200 m-auto overflow-x-auto border-2 p-2 shadow">
        <table className="table-zebra table w-fit text-xl">
          <thead>
            <tr>
              <th className="text-primary-content text-4xl font-bold">
                dotVFile
              </th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <th>Status</th>
              <td>
                {" "}
                {verified.result ? (
                  <div className="flex flex-row gap-1">
                    <SuccessCheckmarkIcon className="self-center" />
                    <span>Success!</span>
                  </div>
                ) : (
                  <div className="flex flex-row gap-1">
                    <ErrorIcon className="self-center" />
                    <span>Error. {verified.error?.message}</span>
                  </div>
                )}
              </td>
            </tr>
            <tr>
              <th>Path</th>
              <td>{vfilePath}</td>
            </tr>
            {stats ? (
              <>
                <tr>
                  <th>Size</th>
                  <td>{stats?.databaseFileSizeString}</td>
                </tr>
                <tr>
                  <th>VFiles</th>
                  <td>
                    {stats?.vFiles?.count.toLocaleString()} vfiles totaling{" "}
                    {stats?.vFiles?.sizeString}
                  </td>
                </tr>
                <tr>
                  <th>Versions</th>
                  <td>
                    {stats?.versions?.count.toLocaleString()} versioned vfiles
                    totaling {stats?.versions?.sizeString}
                  </td>
                </tr>
                <tr>
                  <th>Content</th>
                  <td>
                    {stats?.content?.count.toLocaleString()} stored file
                    contents totaling {stats?.content?.sizeStoredString}
                  </td>
                </tr>
              </>
            ) : (
              <></>
            )}
          </tbody>
        </table>
      </div>
      <div className="divider" />
      {verified.result ? (
        <div className="mb-8">
          <FileExplorerWindow
            getVFileDirectory={getDirectoryCallback}
            getVFileBytes={getFileBytesCallback}
            exportDirectory={exportDirectoryCallback}
          />
        </div>
      ) : (
        <></>
      )}
    </div>
  );
}
