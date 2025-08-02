import { getDirectories, getFileInfos } from "./api";
import DirectoryRecord from "./DirectoryRecord";
import FileRecord from "./FileRecord";

export default async function Home() {
  const dirs = await getDirectories("/");
  const files = await getFileInfos("/");

  return (
    <div>
      <div className="m-auto flex w-3xl flex-col">
        {dirs.map((dir) => (
          <DirectoryRecord directory={dir} key={dir.name} />
        ))}
        {files.map((file) => (
          <FileRecord fileInfo={file} key={file.name} />
        ))}
      </div>
    </div>
  );
}
