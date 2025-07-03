namespace dotVFile;

public static class Extensions
{
	/// <summary>
	/// Converts FilePath to a path standardized for the current system via Path.Combine.<br/>
	/// e.g. "/a/b/c/file.txt" converts to "a\b\c\file.txt" on Windows
	/// </summary>
	public static string GetSystemFilePath(this VFilePath path)
	{
		return Path.Combine(Path.Combine([.. path.DirectoryParts]), path.FileName);
	}
}
