import { RecordType } from "@/types";
import { FaFile, FaFolderClosed } from "react-icons/fa6";

export default function FileExplorerIcon({ type }: { type: RecordType }) {
  switch (type) {
    case RecordType.Directory:
      return <FaFolderClosed className="fill-primary" />;
    case RecordType.File:
      return <FaFile className="fill-secondary" />;
  }
}
