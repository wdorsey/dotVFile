namespace dotVFile.WebAPI;

public record VFileRequest(string VFilePath);

public record Response<T>
{
	public Response(T result) : this(result, null) { }
	public Response(Error error) : this(default, error) { }
	public Response(T? result, Error? error)
	{
		Result = result;
		Error = error;
	}

	public T? Result { get; set; }
	public Error? Error { get; set; }
}

public record Error(string Type, string Message);

public record DirectoryRequest(string VFilePath, string Directory)
	: VFileRequest(VFilePath);

public record GetFileBytesRequest(string VFilePath, string FilePath)
	: VFileRequest(VFilePath);

public record ApiVDirectory(VDirectory Directory, DirectoryStats Stats);

public record ExportRequest(
	string VFilePath,
	string DirectoryPath)
	: VFileRequest(VFilePath);

public record ExportResponse(List<string> ExportedFilePaths);