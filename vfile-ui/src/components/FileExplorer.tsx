import { verifyVFilePath } from "@/api";
import FileExplorerWindow from "./FileExplorerWindow";
import { IoIosCheckmarkCircle } from "react-icons/io";
import { BiError } from "react-icons/bi";

export default async function FileExplorer() {
  // vfilePath fetched from env.local
  const vfilePath = process.env.NEXT_PUBLIC_VFILE_PATH || "";

  const verified = await verifyVFilePath(vfilePath);

  return (
    <div className="m-auto mt-2 flex w-6xl flex-col gap-2">
      <div className="text-xl underline">VFile</div>
      <dl className="mb-8 grid grid-cols-[auto_1fr] gap-3 text-xl">
        <dt className="text-right">Path:</dt>
        <dd>{vfilePath}</dd>
        <dt className="text-right">Status:</dt>
        <dd>
          {verified ? (
            <div className="flex flex-row gap-1">
              <IoIosCheckmarkCircle
                className="fill-success self-center"
                size={24}
              />
              <span>Valid</span>
            </div>
          ) : (
            <div className="flex flex-row gap-1">
              <BiError className="fill-error self-center" size={24} />
              <span>Invalid path</span>
            </div>
          )}
        </dd>
      </dl>
      <FileExplorerWindow vfilePath={vfilePath} />
    </div>
  );
}
