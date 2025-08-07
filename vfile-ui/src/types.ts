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
  sizeStored: number;
  compression: number;
  contentCreationTime: string;
}

export interface VFileDirectory {
  path: string;
  dirs: VDirectory[] | undefined;
  files: VFileInfo[] | undefined;
  error: ApiError | undefined;
}

export interface VFileStats {
  databaseFileSize: number;
  databaseFileSizeString: string;
  directoryCount: number;
  vFiles: VFileTotals;
  versions: VFileTotals;
  content: ContentTotals;
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
