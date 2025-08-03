export enum RecordType {
  Directory,
  File,
  MoveBack,
}

export interface Record {
  type: RecordType;
  path: string;
  name: string;
}

export interface Path {
  path: string;
  prevPath: Path | undefined;
}
