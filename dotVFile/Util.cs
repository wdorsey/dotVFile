using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace dotVFile;

internal static class Util
{
	public static byte[] EmptyBytes() => [];

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
