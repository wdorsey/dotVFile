import { getStats, verifyVFilePath } from "@/api";
import FileExplorerWindow from "./FileExplorerWindow";
import { IoIosCheckmarkCircle } from "react-icons/io";
import { BiError } from "react-icons/bi";

export default async function FileExplorer() {
  // vfilePath fetched from env.local
  const vfilePath = process.env.NEXT_PUBLIC_VFILE_PATH || "";

  const verified = await verifyVFilePath(vfilePath);
  const stats = await getStats(vfilePath).then((res) => res.result);

  return (
    <div className="m-auto mt-2 flex w-6xl flex-col gap-2">
      <h1 className="text-2xl underline">VFile</h1>
      <dl className="mb-8 grid grid-cols-[auto_1fr] gap-2 text-xl">
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
        <FileExplorerWindow vfilePath={vfilePath} />
      ) : (
        <div></div>
      )}
    </div>
  );
}
