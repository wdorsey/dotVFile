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
		public long RowId;
		public Guid Id;
		public DateTimeOffset CreateTimestamp;
	}

	public record VFileInfo(
		string FileId,
		string RelativePath,
		string FileName,
		string Extension,
		DateTimeOffset? Versioned,
		DateTimeOffset? DeleteAt,
		DateTimeOffset CreationTime)
		: Entity
	{
		public long VFileContentInfoRowId { get; set; }
	}

	public record VFileContentInfo(
		string Hash,
		int Size,
		int SizeStored,
		byte Compression,
		DateTimeOffset CreationTime)
		: Entity;

	public record VFileContent(byte[] Content) : Entity
	{
		public long VFileContentInfoRowId { get; set; }
	}

	public record VFile(
		VFileInfo? VFileInfo,
		VFileContentInfo? VFileContentInfo,
		VFileContent? VFileContent);

	[JsonConverter(typeof(StringEnumConverter))]
	public enum VFileInfoVersionQuery
	{
		Latest,
		Versions,
		Both
	}
}