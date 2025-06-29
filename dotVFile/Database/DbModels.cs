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

	public record VFileInfo : Entity
	{
		public long VFileContentRowId;
		public string FileId = string.Empty;
		public string RelativePath = string.Empty;
		public string FileName = string.Empty;
		public string FileExtension = string.Empty;
		public DateTimeOffset? Versioned;
		public DateTimeOffset? DeleteAt;
		public DateTimeOffset CreationTime;
	}

	public record VFileContent : Entity
	{
		public string Hash = string.Empty;
		public long Size;
		public long SizeStored;
		public byte Compression;
		public byte[]? Content;
		public DateTimeOffset CreationTime;
	}

	public record VFile(
		VFileInfo VFileInfo,
		VFileContent VFileContent);

	[JsonConverter(typeof(StringEnumConverter))]
	public enum VFileInfoVersionQuery
	{
		Latest,
		Versions,
		Both
	}

	public record StoreVFilesResult
	{
		public List<VFileInfo> NewVFileInfos = [];
		public List<VFileInfo> UpdatedVFileInfos = [];
		public List<VFileContent> NewVFileContents = [];
	}
}