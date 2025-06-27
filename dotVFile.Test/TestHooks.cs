namespace dotVFile.Test;

public class TestHooks : IVFileHooks
{
	public void Error(VFileError err)
	{
		Console.WriteLine(err.ToString());
	}

	public void Log(string msg)
	{
		Console.WriteLine(msg);
	}
}
