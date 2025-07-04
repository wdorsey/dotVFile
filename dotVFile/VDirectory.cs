namespace dotVFile;

public class VDirectory
{
	public const char DirectorySeparator = '/';
	public static VDirectory Default() => new(null);

	public VDirectory(string? directory)
	{
		Path = StandardizeDirectory(directory);
		DirectoryNames = [.. Path.Split(DirectorySeparator, StringSplitOptions.RemoveEmptyEntries)];
	}

	public string Name => DirectoryNames.LastOrDefault() ?? string.Empty;

	public string Path { get; }

	/// <summary>
	/// Names of each separate directory, in order
	/// </summary>
	public List<string> DirectoryNames { get; }

	public VDirectory? ParentDirectory()
	{
		return DirectoryNames.Count > 1
			? new(string.Join(DirectorySeparator, DirectoryNames[..^1])) // second-to-last
			: null;
	}

	public override string ToString()
	{
		return Path;
	}

	/// <summary>
	/// Standardizes all directories to use DirectorySeparator '/'
	/// and the full path always starts and ends with '/'.
	/// e.g. /x/y/z/
	/// </summary>
	internal static string StandardizeDirectory(string? directory)
	{
		char[] dividers = ['/', '\\'];
		var parts = directory?.Split(dividers, StringSplitOptions.RemoveEmptyEntries);
		var result = DirectorySeparator.ToString();
		if (parts.AnySafe())
		{
			result += string.Join(DirectorySeparator, parts);
			result += DirectorySeparator;
		}
		return result;
	}
}
