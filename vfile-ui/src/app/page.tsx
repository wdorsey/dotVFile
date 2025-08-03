import { getDirectory } from "@/api";
import FileExplorer from "@/components/FileExplorer";

export default async function Home() {
  const path = "/";
  const dir = await getDirectory(path);

  return (
    <div className="h-full w-full">
      <FileExplorer initialPath={path} initialDir={dir} />
    </div>
  );
}
