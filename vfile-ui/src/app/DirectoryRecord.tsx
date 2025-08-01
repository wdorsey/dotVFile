import { VDirectory } from "./api";

interface DirectoryRecordPorps {
  directory: VDirectory;
}

export default function DirectoryRecord({ directory }: DirectoryRecordPorps) {
  return (
    <div className="w-full">
      <span>{directory.name}</span>
    </div>
  );
}
