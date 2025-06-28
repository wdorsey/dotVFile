using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace dotVFile;

public record VFileDatabaseOptions(
	string Name,
	string VFileDirectory,
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
		int Size,
		int SizeOnDisk,
		byte Compression,
		DateTimeOffset CreationTime)
		: Entity;

	public record VFile(
		byte[] File)
		: Entity
	{
		public long VFileDataInfoId { get; set; }
	}

	public record VFileData(VFileDataInfo DataInfo, VFile File);

	[JsonConverter(typeof(StringEnumConverter))]
	public enum VFileInfoVersionQuery
	{
		Latest,
		Versions,
		Both
	}
}