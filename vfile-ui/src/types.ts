export interface Path {
  path: string;
  prevPath: Path | undefined;
}

export enum FileExplorerItemType {
  Directory,
  File,
}

export interface VDirectory {
  path: string;
  name: string;
}

export interface VFileInfo {
  id: string;
  fileName: string;
  filePath: string;
  fileExtension: string;
  directoryName: string;
  directoryPath: string;
  versioned?: string;
  isVersion: boolean;
  deleteAt?: string;
  creationTime: string;

  // Content fields
  contentId: string;
  hash: string;
  size: number;
  sizeString: string;
  sizeStored: number;
  sizeStoredString: string;
  compression: number;
  contentCreationTime: string;
}

export interface ApiVDirectory {
  directory: VDirectory;
  stats: VDirectoryStats;
}

export interface VFileDirectory {
  path: string;
  dirs?: ApiVDirectory[];
  files?: VFileInfo[];
  stats?: VDirectoryStats;
  error?: ApiError;
}

export interface VFileStats {
  databaseFileSize: number;
  databaseFileSizeString: string;
  directoryCount: number;
  vFiles: VFileTotals;
  versions: VFileTotals;
  content: ContentTotals;
}

export interface VDirectoryStats {
  directory: VDirectory;
  directoryCount: number;
  vFiles: VFileTotals;
  versions: VFileTotals;
  totalVFiles: VFileTotals;
  totalVersions: VFileTotals;
  directories: VDirectory[];
}

export interface VFileTotals {
  count: number;
  size: number;
  sizeString: string;
}

export interface ContentTotals {
  count: number;
  size: number;
  sizeString: string;
  sizeStored: number;
  sizeStoredString: string;
}

export interface ApiRequest {
  vfilePath: string;
}

export interface ApiResponse<T> {
  result?: T;
  error?: ApiError;
}

export interface ApiError {
  type: string;
  message: string;
}

export type DirectoryApiRequest = {
  directory: string;
} & ApiRequest;

export type FileApiRequest = {
  filePath: string;
} & ApiRequest;
