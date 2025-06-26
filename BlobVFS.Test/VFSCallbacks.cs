namespace BlobVFS.Test;

public class Callbacks : IVFSCallbacks
{
	public void HandleError(VFSError err)
	{
		Console.WriteLine($"error: {err.ErrorCode}");
		Console.WriteLine(err.Exception.ToString());
	}

	public void Log(string msg)
	{
		Console.WriteLine(msg);
	}
}
