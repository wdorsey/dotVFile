"use server";

export declare interface VDirectory {
  id: string;
  path: string;
  name: string;
}

export declare interface VFileInfo {
  id: string;
  path: string;
  name: string;
}

export declare interface VFileDirectory {
  path: string;
  dirs: VDirectory[];
  files: VFileInfo[];
}

export async function verifyVFilePath(path: string): Promise<boolean> {
  console.log(`verifyVFilePath: ${path}`);
  return true;
}

export async function getDirectories(dir: string): Promise<VDirectory[]> {
  // dummy data
  console.log(`getDirectories: ${dir}`);
  const dirs = [
    {
      id: "bd8138f2-508e-48da-b8f5-b556061e75b2",
      path: dir.concat("db-anime/"),
      name: "db-anime",
    },
    {
      id: "0b3cf345-26cf-4cdc-9839-93d38bb3aefd",
      path: dir.concat("db-anime-release/"),
      name: "db-anime-release",
    },
    {
      id: "9ed9fba7-0306-48b1-8ae5-71a0e9270f60",
      path: dir.concat("image/"),
      name: "image",
    },
    {
      id: "9ed9fba7-0306-48b1-8ae5-71a0e9270f60",
      path: dir.concat("abc/"),
      name: "abc",
    },
  ];

  return dirs.map((dir) => ({
    ...dir,
  })) as VDirectory[];
}

export async function getFileInfos(dir: string): Promise<VFileInfo[]> {
  // dummy data
  console.log(`getFileInfos: ${dir}`);
  const files = [
    {
      id: "fe87f839-3678-4cba-9cda-5738f95dffab",
      name: "file3.txt",
      path: dir.concat("/file3.txt"),
    },
    {
      id: "fe87f839-3678-4cba-9cda-5738f95dffab",
      name: "file4.json",
      path: dir.concat("/file4.json"),
    },
    {
      id: "fe87f839-3678-4cba-9cda-5738f95dffab",
      name: "file5.json",
      path: dir.concat("/file5.json"),
    },
    {
      id: "fe87f839-3678-4cba-9cda-5738f95dffab",
      name: "file.txt",
      path: dir.concat("/file.txt"),
    },
    {
      id: "fe87f839-3678-4cba-9cda-5738f95dffab",
      name: "file1.txt",
      path: dir.concat("/file1.txt"),
    },
    {
      id: "fe87f839-3678-4cba-9cda-5738f95dffab",
      name: "file2.txt",
      path: dir.concat("/file2.txt"),
    },
  ];

  return files.map((file) => ({
    ...file,
  })) as VFileInfo[];
}

export async function getFileBytes(
  fileName: string,
  filePath: string,
): Promise<Blob> {
  console.log(filePath);
  const blob = new Blob([]);
  return blob;
}

export async function getDirectory(dir: string): Promise<VFileDirectory> {
  return {
    path: dir,
    dirs: await getDirectories(dir),
    files: await getFileInfos(dir),
  } as VFileDirectory;
}
