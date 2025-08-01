export interface VDirectory {
  path: string;
  name: string;
}

export function getDirectories(dir: string) {
  // dummy data
  console.log(dir);
  const dirs = [
    {
      path: "/db-anime/",
      name: "db-anime",
    },
    {
      path: "/db-anime-release/",
      name: "db-anime-release",
    },
  ];

  return dirs.map((dir) => ({
    ...dir,
  })) as VDirectory[];
}
