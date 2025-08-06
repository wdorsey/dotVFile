export interface Path {
  path: string;
  prevPath: Path | undefined;
}

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

export enum FileExplorerItemType {
  Directory,
  File,
}
