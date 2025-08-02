import { VDirectory } from "./api";
import { FaFolderClosed } from "react-icons/fa6";
import Record from "./Record";

interface DirectoryRecordPorps {
  directory: VDirectory;
}

export default function DirectoryRecord({ directory }: DirectoryRecordPorps) {
  return (
    <Record>
      <FaFolderClosed className="fill-info" />
      <span>{directory.name}</span>
    </Record>
  );
}
