import { getDirectories } from "./api";
import DirectoryRecord from "./DirectoryRecord";

export default function Home() {
  const dirs = getDirectories("/");

  return (
    <div>
      <div>
        {dirs.map((dir) => (
          <DirectoryRecord directory={dir} key={dir.name} />
        ))}
      </div>
    </div>
  );
}
