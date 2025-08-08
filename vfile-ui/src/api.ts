import "server-only";

import {
  ApiRequest,
  ApiResponse,
  ApiVDirectory,
  DirectoryApiRequest,
  FileApiRequest,
  VDirectoryStats,
  VFileInfo,
  VFileStats,
} from "./types";
import { b64toBlob } from "./utils";

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

export async function getDirectoryStats(
  vfilePath: string,
  dir: string,
): Promise<ApiResponse<VDirectoryStats>> {
  return await call("/VFile/GetDirectoryStats", {
    vfilePath,
    directory: dir,
  } as DirectoryApiRequest);
}

export async function getDirectories(
  vfilePath: string,
  dir: string,
): Promise<ApiResponse<ApiVDirectory[]>> {
  return await call("/VFile/GetDirectories", {
    vfilePath,
    directory: dir,
  } as DirectoryApiRequest);
}

export async function getFileInfos(
  vfilePath: string,
  dir: string,
): Promise<ApiResponse<VFileInfo[]>> {
  return await call("/VFile/GetFiles", {
    vfilePath,
    directory: dir,
  } as DirectoryApiRequest);
}

export async function getFileBytes(
  vfilePath: string,
  filePath: string,
): Promise<ApiResponse<Blob>> {
  const response = await call<FileApiRequest, string>("/VFile/GetFileBytes", {
    vfilePath,
    filePath: filePath,
  } as FileApiRequest);

  if (response.result) {
    return {
      result: b64toBlob(response.result),
      error: response.error,
    };
  }

  return { error: response.error };
}
