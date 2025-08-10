import "server-only";

export function sampleVFileDbPath() {
  return `${process.cwd()}\\sample-vfile-db\\${process.env.SAMPLE_VFILE_NAME}`;
}
