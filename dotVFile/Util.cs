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

	public const string DefaultDateTimeFormat = "yyyy-MM-ddTHH:mm:ss.ffffffzzz";

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

	public static List<T> AddSafe<T>(this List<T> list, T item)
	{
		if (!list.Contains(item))
		{
			list.Add(item);
		}
		return list;
	}

	public static Dictionary<TKey, TValue> AddSafe<TKey, TValue>(
		this Dictionary<TKey, TValue> dict,
		TKey key,
		TValue value)
		where TKey : notnull
	{
		if (!dict.TryAdd(key, value))
			dict[key] = value;

		return dict;
	}

	public static Dictionary<TKey, List<TValue>> AddSafe<TKey, TValue>(
		this Dictionary<TKey, List<TValue>> dict,
		TKey key,
		TValue value)
		where TKey : notnull
	{
		if (dict.TryGetValue(key, out var found))
			found.Add(value);
		else
			dict.Add(key, [value]);

		return dict;
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

	public static string PluralChar(this int count, string one = "", string plural = "s")
	{
		return count == 1 ? one : plural;
	}

	/// <summary>
	/// Always rounds up.
	/// </summary>
	public static int DivideInt(int numerator, int denominator)
	{
		return denominator == 0 ? 0
			: numerator / denominator
			+ (numerator % denominator == 0 ? 0 : 1);
	}

	/// <summary>
	/// Always rounds up.
	/// </summary>
	public static long DivideLong(long numerator, long denominator)
	{
		return denominator == 0 ? 0
			: numerator / denominator
			+ (numerator % denominator == 0 ? 0 : 1);
	}

	public static T MinSafe<T>(this IEnumerable<T> list, T @default)
	{
		return list.AnySafe() ? list.Min() ?? @default : @default;
	}

	public static T MaxSafe<T>(this IEnumerable<T> list, T @default)
	{
		return list.AnySafe() ? list.Max() ?? @default : @default;
	}

	public static string FileExtension(string? fileName)
	{
		return fileName.IsEmpty()
			? string.Empty
			: new FileInfo(fileName).Extension;
	}

	public static (string Name, string Ext) FileNameAndExtension(string fileName)
	{
		var ext = FileExtension(fileName);
		var name = fileName[..fileName.LastIndexOf(ext)];
		return (name, ext);
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

	public static string? GetString(byte[]? bytes)
	{
		return Decode(bytes);
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

	/* Timespan string utils */
	public static string ToStringNumber(this short value) => value.ToString("N0");
	public static string ToStringNumber(this int value) => value.ToString("N0");
	public static string ToStringNumber(this long value) => value.ToString("N0");
	public static string ToStringNumber(this double value) => value.ToString("N2");
	public static string ToStringNumber(this decimal value) => value.ToString("N2");

	public static string NanosecondsString(this TimeSpan ts)
	{
		return $"0.{((int)ts.TotalNanoseconds).ToString().PadLeft(6, '0')} ms";
	}

	public static string MillisecondsString(this TimeSpan ts)
	{
		return $"{((int)ts.TotalMilliseconds).ToStringNumber()} ms";
	}

	public static string SecondsString(this TimeSpan ts)
	{
		return $"{ts.Seconds.ToStringNumber()},{ts.Milliseconds.ToStringNumber().PadLeft(3, '0')} ms";
	}

	public static string MinutesString(this TimeSpan ts)
	{
		return $"{ts.TotalMinutes.ToStringNumber()} min";
	}

	public static string TimeString(this TimeSpan ts)
	{
		// only works if less than 60 seconds
		return ts.TotalMinutes >= 1
			? ts.MinutesString()
			: ts.TotalSeconds >= 1
			? ts.SecondsString()
			: ts.TotalMilliseconds >= 1
			? ts.MillisecondsString()
			: ts.NanosecondsString();
	}

	/* SizeString */
	public static string SizeString(long sizeBytes)
	{
		var (size, units) = SizeStringParts(sizeBytes);

		return $"{size} {units}";
	}

	public static (string Size, string Units) SizeStringParts(long sizeBytes)
	{
		static string GetSizeString(decimal size, int decimalPlaces) =>
			string.Format("{0:n" + decimalPlaces + "}", size);

		(string, int)[] sizeSuffixesDecimalPlaces =
		{
				("bytes", 0),
				("KB", 2),
				("MB", 2),
				("GB", 2),
				("TB", 2),
				("PB", 3),
				("EB", 3),
				("ZB", 3),
				("YB", 3)
			};

		if (sizeBytes == 0)
			return (GetSizeString(0, 0), "bytes");

		if (sizeBytes < 0)
		{
			var (size, units) = SizeStringParts(-sizeBytes);
			return ("-" + size, units);
		}

		// mag is 0 for bytes, 1 for KB, 2 for MB, etc.
		// 1024 = 1 KB
		var mag = (int)Math.Log(sizeBytes, 1024);

		// shift size according to the magnitude
		// 1L << (mag * 10) == 2 ^ (mag * 10) 
		// the number of bytes in the unit corresponding to mag
		var adjustedSize = (decimal)sizeBytes / (1L << mag * 10);

		// make adjustment when the value is large enough that
		// it would round up to 1000 or more
		if (Math.Round(adjustedSize, 1) >= 1000)
		{
			mag += 1;
			adjustedSize /= 1024;
		}

		var (sizeSuffix, decimalPlaces) = sizeSuffixesDecimalPlaces[mag];

		return (GetSizeString(adjustedSize, decimalPlaces), sizeSuffix);
	}
}
