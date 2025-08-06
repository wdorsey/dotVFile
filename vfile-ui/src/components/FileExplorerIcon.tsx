import { FileExplorerItemType } from "@/types";
import { FaFile, FaFolderClosed } from "react-icons/fa6";

export default function FileExplorerIcon({
  type,
}: {
  type: FileExplorerItemType;
}) {
  switch (type) {
    case FileExplorerItemType.Directory:
      return <FaFolderClosed className="fill-primary" />;
    case FileExplorerItemType.File:
      return <FaFile className="fill-secondary" />;
  }
}
