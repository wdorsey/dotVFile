import "server-only";

import { getDirectories, getDirectoryStats, getFileInfos } from "./api";
import { VFileDirectory } from "./types";

export async function getVFileDirectory(
  vfilePath: string,
  dir: string,
): Promise<VFileDirectory> {
  const stats = await getDirectoryStats(vfilePath, dir);
  const dirs = await getDirectories(vfilePath, dir);
  const files = await getFileInfos(vfilePath, dir);

  return {
    path: dir,
    name: stats.result?.directory.name || "_unknown_dir_name_",
    dirs: dirs.result?.sort((a, b) =>
      a.directory.name.localeCompare(b.directory.name),
    ),
    files: files.result?.sort((a, b) => a.fileName.localeCompare(b.fileName)),
    stats: stats.result,
    error: dirs.error,
  };
}
