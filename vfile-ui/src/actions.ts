import "server-only";

import { getDirectories, getFileInfos } from "./api";
import { VFileDirectory } from "./types";

export async function getVFileDirectory(
  vfilePath: string,
  dir: string,
): Promise<VFileDirectory> {
  const dirs = await getDirectories(vfilePath, dir);
  const files = await getFileInfos(vfilePath, dir);

  return {
    path: dir,
    dirs: dirs.result?.sort((a, b) => a.name.localeCompare(b.name)),
    files: files.result?.sort((a, b) => a.fileName.localeCompare(b.fileName)),
    error: dirs.error,
  };
}
