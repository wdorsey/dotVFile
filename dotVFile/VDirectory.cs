namespace dotVFile;

public class VDirectory : IEquatable<VDirectory>
{
	public const char DirectorySeparator = '/';
	public static VDirectory Default() => new(string.Empty);
	public static VDirectory RootDirectory() => new(DirectorySeparator.ToString());

	public VDirectory(string? directory)
	{
		Path = StandardizeDirectory(directory);
		DirectoryNames = [.. Path.Split(DirectorySeparator, StringSplitOptions.RemoveEmptyEntries)];
		SystemPath = System.IO.Path.Combine([.. DirectoryNames]);
	}

	public VDirectory(params string[] directories)
		: this(string.Join(string.Empty, StandardizeDirectories(directories))) { }

	public string Name => DirectoryNames.LastOrDefault() ?? string.Empty;
	public string Path { get; }
	public bool IsRoot => Equals(RootDirectory());

	/// <summary>
	/// Converts Path to a path standardized for the current system via Path.Combine.<br/>
	/// e.g. "/a/b/c/" converts to "a\b\c" on Windows
	/// </summary>
	public string SystemPath { get; }

	/// <summary>
	/// Names of each directory in Path, in order
	/// </summary>
	public List<string> DirectoryNames { get; }

	public VDirectory? ParentDirectory()
	{
		if (IsRoot) return null;

		return DirectoryNames.Count > 1
			? new(string.Join(DirectorySeparator, DirectoryNames[..^1])) // cut off last element
			: RootDirectory();
	}

	public List<VDirectory> AllDirectoriesInPath()
	{
		var results = new List<VDirectory>();

		var prev = string.Empty;
		foreach (var name in DirectoryNames)
		{
			var dir = prev + DirectorySeparator + name;
			results.Add(new(dir));
			prev = dir;
		}

		return results;
	}

	public override string ToString()
	{
		return Path;
	}

	public override int GetHashCode()
	{
		return Path.GetHashCode();
	}

	public override bool Equals(object? obj)
	{
		return obj != null &&
			obj is VDirectory directory &&
			Path == directory.Path;
	}

	public bool Equals(VDirectory? other)
	{
		return other?.Path == Path;
	}

	public static VDirectory RemoveRootPath(VDirectory dir, VDirectory root)
	{
		return new(dir.Path.Replace(root.Path, string.Empty));
	}

	public static VDirectory Join(VDirectory dir1, VDirectory dir2)
	{
		return new(dir1.Path.TrimEnd(DirectorySeparator) +
			DirectorySeparator +
			dir2.Path.TrimStart(DirectorySeparator));
	}

	/// <summary>
	/// Standardizes all directories to use DirectorySeparator '/'
	/// and the full path always starts and ends with '/'.
	/// e.g. /x/y/z/
	/// </summary>
	private static string StandardizeDirectory(string? directory)
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

	private static List<string> StandardizeDirectories(IEnumerable<string> directories)
	{
		return [.. directories.Select(StandardizeDirectory)];
	}
}
