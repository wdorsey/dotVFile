namespace dotVFile.WebAPI;

public record VFileRequest(string VFilePath);

public record Response<T>(T Result)
{
	public Error? Error { get; set; }
}

public record Error(string Type, string Message);

public record GetDirectoriesRequest(string VFilePath, string Directory)
	: VFileRequest(VFilePath);