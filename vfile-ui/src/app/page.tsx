import { getDirectory } from "@/api";
import FileExplorer from "@/components/FileExplorer";
import VFileSelector from "@/components/VFileSelector";

export default async function Home() {
  const dir = await getDirectory("/");

  return (
    <div className="h-full w-full">
      <div className="m-auto mt-2 flex w-4xl flex-col gap-2">
        <VFileSelector />
        <FileExplorer initialDir={dir} />
      </div>
    </div>
  );
}
