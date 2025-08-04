import { getDirectory } from "@/api";
import FileExplorer from "@/components/FileExplorer";

export default async function Home() {
  const dir = await getDirectory("/");

  return (
    <div className="h-full w-full">
      <FileExplorer initialDir={dir} />
    </div>
  );
}
