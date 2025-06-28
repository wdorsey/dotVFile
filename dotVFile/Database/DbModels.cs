using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace dotVFile;

public record VFileDatabaseOptions(
	string RootPath,
	IVFileHooks Hooks);

public static class Db
{
	public record Entity
	{
		public long Id;
		public DateTimeOffset CreateTimestamp;
	}

	public record VFileInfo(
		string FileId,
		string Hash,
		string RelativePath,
		string FileName,
		string Extension,
		int Size,
		string? Version,
		DateTimeOffset? DeleteAt,
		DateTimeOffset CreationTime)
		: Entity;

	public record VFileDataInfo(
		string Hash,
		string Directory,
		string FileName,
		int Size,
		int SizeOnDisk,
		byte Compression,
		DateTimeOffset CreationTime)
		: Entity;

	[JsonConverter(typeof(StringEnumConverter))]
	public enum VFileInfoVersionQuery
	{
		Latest,
		Versions,
		Both
	}
}