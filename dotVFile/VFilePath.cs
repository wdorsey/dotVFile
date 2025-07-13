namespace dotVFile;

public class VFilePath : IEquatable<VFilePath>
{
	public static VFilePath Default() => new(string.Empty, string.Empty);

	public VFilePath(VDirectory directory, string fileName)
	{
		VDirectory = directory;
		FileName = fileName;
		FileExtension = Util.FileExtension(FileName);
		FilePath = $"{VDirectory.Path}{FileName}";
		SystemFilePath = GetSystemFilePath(VDirectory, FileName);
	}

	public VFilePath(string vfilePath)
	{
		var idx = vfilePath.LastIndexOf(VDirectory.DirectorySeparator);
		if (idx == -1)
			throw new ArgumentException($"invalid vfilePath: {vfilePath}");

		VDirectory = new VDirectory(vfilePath[..idx]);
		FileName = vfilePath[(idx + 1)..];
		FileExtension = Util.FileExtension(FileName);
		FilePath = $"{VDirectory.Path}{FileName}";
		SystemFilePath = GetSystemFilePath(VDirectory, FileName);
	}

	public VFilePath(string? directory, string fileName)
		: this(new VDirectory(directory), fileName) { }

	public VFilePath(FileInfo fi)
		: this(fi.DirectoryName, fi.Name) { }

	public VDirectory VDirectory { get; }
	public string FileName { get; }
	public string FileExtension { get; }
	public string FilePath { get; }

	/// <summary>
	/// Converts FilePath to a path standardized for the current system via Path.Combine.<br/>
	/// e.g. "/a/b/c/file.txt" converts to "a\b\c\file.txt" on Windows
	/// </summary>
	public string SystemFilePath { get; }

	public static string GetSystemFilePath(VDirectory directory, string fileName)
	{
		return Path.Combine(directory.SystemPath, fileName);
	}

	public override string ToString()
	{
		return FilePath;
	}

	public override int GetHashCode()
	{
		return FilePath.GetHashCode();
	}

	public override bool Equals(object? obj)
	{
		return obj != null &&
			obj is VFilePath path &&
			FilePath == path.FilePath;
	}

	public bool Equals(VFilePath? other)
	{
		return other?.FilePath == FilePath;
	}

	public static VFilePath Combine(VDirectory root, VFilePath path)
	{
		return new(VDirectory.Join(root, path.VDirectory), path.FileName);
	}
}
