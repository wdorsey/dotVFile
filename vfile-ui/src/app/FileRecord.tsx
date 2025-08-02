import { VFileInfo } from "./api";
import { FaFile } from "react-icons/fa";
import Record from "./Record";

interface FileRecordPorps {
  fileInfo: VFileInfo;
}

export default function FileRecord({ fileInfo }: FileRecordPorps) {
  return (
    <Record>
      <FaFile className="fill-info-content" />
      <span>{fileInfo.name}</span>
    </Record>
  );
}
