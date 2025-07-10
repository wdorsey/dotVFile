namespace dotVFile;

public class VFilePath : IEquatable<VFilePath>
{
	public static VFilePath Default() => new(string.Empty, string.Empty);

	public VFilePath(VDirectory directory, string fileName)
	{
		Directory = directory;
		FileName = fileName;
		FileExtension = Util.FileExtension(fileName);
		FilePath = $"{Directory.Path}{FileName}";
		SystemFilePath = Path.Combine(
			Path.Combine([.. Directory.DirectoryNames]),
			FileName);
	}

	public VFilePath(string? directory, string fileName)
		: this(new VDirectory(directory), fileName) { }

	public VFilePath(string filePath)
		: this(new FileInfo(filePath)) { }

	public VFilePath(FileInfo fi)
		: this(fi.DirectoryName, fi.Name) { }

	public VDirectory Directory { get; }
	public string FileName { get; }
	public string FileExtension { get; }
	public string FilePath { get; }

	/// <summary>
	/// Converts FilePath to a path standardized for the current system via Path.Combine.
	/// e.g. "/a/b/c/file.txt" converts to "a\b\c\file.txt" on Windows
	/// </summary>
	public string SystemFilePath { get; }

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
		return new(VDirectory.Join(root, path.Directory), path.FileName);
	}
}
