import "server-only";

import {
  ApiRequest,
  ApiResponse,
  DirectoryApiRequest,
  VDirectory,
  VFileInfo,
  VFileStats,
} from "./types";

async function call<RequestType extends ApiRequest, ResultType>(
  route: string,
  request: RequestType,
): Promise<ApiResponse<ResultType>> {
  const url = `${process.env.WEB_API_URL}${route}`;

  console.log(`call api: ${url} ${JSON.stringify(request)}`);

  return await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
    cache: "no-store",
  })
    .then((response) => {
      return response.json();
    })
    .then((data) => {
      const response = data as ApiResponse<ResultType>;
      if (response.error) {
        console.log(`error returned from api: ${JSON.stringify(data.error)}`);
      }
      return response;
    })
    .catch((error) => {
      console.log(error);
      const err: Error = !(error instanceof Error) ? new Error(error) : error;

      return {
        error: {
          type: err.name,
          message: `Error calling api: ${err.message}. Make sure the WebAPI is running.`,
        },
      } as ApiResponse<ResultType>;
    });
}

export async function verifyVFilePath(
  vfilePath: string,
): Promise<ApiResponse<boolean>> {
  return await call("/VFile/VerifyVFile", { vfilePath });
}

export async function getStats(
  vfilePath: string,
): Promise<ApiResponse<VFileStats>> {
  return await call("/VFile/GetStats", { vfilePath });
}

export async function getDirectories(
  vfilePath: string,
  dir: string,
): Promise<ApiResponse<VDirectory[]>> {
  return await call("/VFile/GetDirectories", {
    vfilePath,
    directory: dir,
  } as DirectoryApiRequest);
}

export async function getFileInfos(
  vfilePath: string,
  dir: string,
): Promise<VFileInfo[]> {
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
  vfilePath: string,
  fileName: string,
  filePath: string,
): Promise<Blob> {
  console.log(filePath);
  const blob = new Blob([]);
  return blob;
}
