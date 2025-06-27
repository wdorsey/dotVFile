using System.Diagnostics.CodeAnalysis;

namespace dotVFile;

internal static class Util
{
	public static byte[] EmptyBytes() => [];

	public static List<T> AsList<T>(this T obj)
	{
		if (obj == null) return [];

		return [obj];
	}

	public static bool HasValue([NotNullWhen(true)] this string? value)
	{
		return !string.IsNullOrEmpty(value);
	}

	public static bool IsEmpty([NotNullWhen(false)] this string? value)
	{
		return !value.HasValue();
	}

	public static bool AnySafe<T>([NotNullWhen(true)] this IEnumerable<T>? list)
	{
		return list != null && list.Any();
	}

	public static bool IsEmpty<T>([NotNullWhen(false)] this IEnumerable<T>? list)
	{
		return !list.AnySafe();
	}

	public static string FileExtension(string fileName)
	{
		var fi = new FileInfo(fileName);
		return fi.Extension;
	}

	public static (string Name, string Ext) FileNameAndExtension(string fileName)
	{
		var ext = FileExtension(fileName);
		var name = fileName[..fileName.LastIndexOf(ext)];
		return (name, ext);
	}

	public static List<string> GetPathParts(string? path, char dirSeparator)
	{
		if (path.IsEmpty())
			return [];

		return [.. path.Split(dirSeparator, StringSplitOptions.RemoveEmptyEntries)];
	}
}
