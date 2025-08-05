import FileExplorer from "@/components/FileExplorer";

export default async function Home() {
  return (
    <div className="h-full w-full">
      <div className="m-auto mt-2 flex w-4xl flex-col gap-2">
        <FileExplorer />
      </div>
    </div>
  );
}
