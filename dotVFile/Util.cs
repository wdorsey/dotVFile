using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace dotVFile;

internal static class Util
{
	private static Encoding Encoding => Encoding.UTF8;
	public static byte[] EmptyBytes() => [];

	public const string DefaultDateTimeFormat = "yyyy-MM-dd HH:mm:ss.ffffff zzz";

	public static string ToDefaultString(this DateTimeOffset dt)
	{
		return dt.ToString(DefaultDateTimeFormat);
	}

	public static string? ToDefaultString(this DateTimeOffset? dt)
	{
		return dt?.ToDefaultString();
	}

	public static List<T> AsList<T>(this T obj)
	{
		return obj == null ? [] : [obj];
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

	public static List<List<T>> Partition<T>(this IEnumerable<T> items, int itemsPerList)
	{
		var result = new List<List<T>>();
		var curr = new List<T>();
		foreach (var item in items)
		{
			if (curr.Count < itemsPerList)
			{
				curr.Add(item);
			}
			else
			{
				result.Add(curr);
				curr =
				[
					item
				];
			}
		}

		result.Add(curr);

		return result;
	}

	public static byte[] GetContent(this VFileContent content)
	{
		if (content.Bytes != null)
			return content.Bytes;

		if (content.FilePath.HasValue())
		{
			content.Bytes = GetFileBytes(content.FilePath);
			return content.Bytes;
		}

		if (content.Stream != null)
		{
			Span<byte> buffer = new byte[1024];
			var result = new List<byte>();

			int read;
			while ((read = content.Stream.Read(buffer)) > 0)
			{
				result.AddRange(buffer[..read]);
			}

			content.Bytes = [.. result];

			return content.Bytes;
		}

		throw new Exception("unable to get VFileContent bytes.");
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

	public static string GetString(this IEnumerable<char> values)
	{
		return new string([.. values]);
	}

	public static string GetString(this IEnumerable<string> values)
	{
		return values.SelectMany(x => x).GetString();
	}

	public static byte[] GetBytes(
		object? obj,
		bool format = false,
		bool ignoreNullValues = true)
	{
		if (obj == null) throw new NoNullAllowedException(nameof(obj));

		var settings = new JsonSerializerSettings
		{
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
			NullValueHandling = ignoreNullValues
				? NullValueHandling.Ignore
				: NullValueHandling.Include
		};

		return GetBytes(obj.ToJson(format, settings));
	}

	public static byte[] GetBytes(string? text)
	{
		return Encode(text);
	}

	public static byte[] Encode(string? text)
	{
		return text.HasValue()
			? Encoding.GetBytes(text)
			: EmptyBytes();
	}

	public static string? Decode(byte[]? bytes)
	{
		if (bytes.IsEmpty())
			return null;

		return Encoding.GetString(bytes);
	}

	public static string HashSHA256(byte[] bytes)
	{
		return Convert.ToHexString(SHA256.HashData(bytes));
	}

	public static string CreateDir(string dirPath)
	{
		if (!Directory.Exists(dirPath))
			Directory.CreateDirectory(dirPath);

		return dirPath;
	}

	public static void DeleteDir(string path, bool recursive = false)
	{
		if (!Directory.Exists(path))
			return;

		Directory.Delete(path, recursive);
	}

	/// <summary>
	/// Delete files and directories at a path but not the path dir itself.
	/// </summary>
	public static void DeleteDirectoryContent(string path, bool recursive = false)
	{
		if (!Directory.Exists(path))
			return;

		foreach (var file in GetFiles(path))
		{
			DeleteFile(file.FullName);
		}

		if (recursive)
		{
			foreach (var dir in GetDirectories(path))
			{
				DeleteDir(dir.FullName, recursive);
			}
		}
	}

	public static void DeleteFile(string path)
	{
		if (!File.Exists(path))
			return;

		File.Delete(path);
	}

	/// <param name="extensions">Must contain leading '.'</param>
	public static List<FileInfo> GetFiles(
		string path,
		bool recursive = false,
		params string[] extensions)
	{
		return GetFiles(path, [], recursive, extensions);
	}

	/// <param name="ignoredDirectories">Exact name of the directory to ignore, not a path.</param>
	/// <param name="extensions">Must contain leading '.'</param>
	public static List<FileInfo> GetFiles(
		string path,
		HashSet<string> ignoredDirectories,
		bool recursive = false,
		params string[] extensions)
	{
		var exts = extensions.ToHashSet();

		var files = Directory.GetFiles(CreateDir(path))
			.Select(x => new FileInfo(x))
			.Where(x => exts.IsEmpty() || exts.Contains(x.Extension))
			.ToList();

		if (recursive)
		{
			foreach (var dir in GetDirectories(path))
			{
				if (ignoredDirectories.Contains(dir.Name))
					continue;

				files.AddRange(GetFiles(dir.FullName, ignoredDirectories, recursive, extensions));
			}
		}

		return files;
	}

	public static List<DirectoryInfo> GetDirectories(string path)
	{
		return [.. Directory.GetDirectories(path).Select(x => new DirectoryInfo(x))];
	}

	public static byte[] GetFileBytes(string path)
	{
		AssertFileExists(path);

		return File.ReadAllBytes(path);
	}

	public static void WriteFile(string path, byte[] bytes)
	{
		CreateDir(new FileInfo(path).Directory!.FullName);

		File.WriteAllBytes(path, bytes);
	}

	public static byte[] Compress(byte[] bytes)
	{
		if (bytes == null) throw new NoNullAllowedException(nameof(bytes));

		using var input = new MemoryStream(bytes);
		using var output = new MemoryStream();
		using var compressor = new DeflateStream(output, CompressionMode.Compress);

		input.CopyTo(compressor);

		compressor.Close();

		return output.ToArray();
	}

	public static byte[] Decompress(byte[] bytes)
	{
		if (bytes == null) throw new NoNullAllowedException(nameof(bytes));

		using var input = new MemoryStream(bytes);
		using var output = new MemoryStream();
		using var decompressor = new DeflateStream(input, CompressionMode.Decompress);

		decompressor.CopyTo(output);

		return output.ToArray();
	}

	private static void AssertFileExists(string path)
	{
		if (!File.Exists(path))
			throw new FileNotFoundException(path);
	}

	/* json utils */
	public static string? ToJson(
	this object? obj,
	bool format = false,
	JsonSerializerSettings? settings = null)
	{
		if (obj == null)
			return null;

		settings ??= JsonSettingsDefault();

		if (obj is string)
			return obj.ToString();

		return JsonConvert.SerializeObject(
			obj,
			format ? Formatting.Indented : Formatting.None,
			settings);
	}

	public static T? As<T>(this string? json)
	{
		if (json.IsEmpty())
			return default;

		return JsonConvert.DeserializeObject<T>(json, JsonSettingsDefault());
	}

	public static T? As<T>(this object? obj)
	{
		if (obj == null)
			return default;

		if (typeof(T) == typeof(string))
			return (T)obj;

		var json = obj.ToJson();

		if (json == null)
			return default;

		return JsonConvert.DeserializeObject<T>(json, JsonSettingsDefault());
	}

	public static JsonSerializerSettings JsonSettingsDefault()
	{
		return new JsonSerializerSettings
		{
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
			DateParseHandling = DateParseHandling.DateTimeOffset,
			DateTimeZoneHandling = DateTimeZoneHandling.Local
		};
	}

	public static JsonSerializerSettings JsonSettingsIgnoreNulls()
	{
		return JsonSettingsDefault().JsonSettingsIgnoreNulls();
	}

	public static JsonSerializerSettings JsonSettingsIgnoreNulls(this JsonSerializerSettings settings)
	{
		settings.NullValueHandling = NullValueHandling.Ignore;

		return settings;
	}
}
