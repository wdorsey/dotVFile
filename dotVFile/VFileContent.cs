namespace dotVFile;

public class VFileContent
{
	public static VFileContent Default() => new(Util.EmptyBytes());

	public VFileContent(byte[] bytes)
	{
		Bytes = bytes;
	}

	public VFileContent(string filePath)
	{
		FilePath = filePath;
	}

	public VFileContent(Stream stream)
	{
		Stream = stream;
	}

	internal byte[]? Bytes { get; set; }
	internal string? FilePath { get; }
	internal Stream? Stream { get; }

	public byte[] GetContent()
	{
		if (Bytes != null)
			return Bytes;

		if (FilePath.HasValue())
		{
			Bytes = Util.GetFileBytes(FilePath);
			return Bytes;
		}

		if (Stream != null)
		{
			Span<byte> buffer = new byte[1024];
			var result = new List<byte>();

			int bytesRead;
			while ((bytesRead = Stream.Read(buffer)) > 0)
			{
				result.AddRange(buffer[..bytesRead]);
			}

			Bytes = [.. result];
			return Bytes;
		}

		throw new Exception("VFileContent.GetContent() - unable to get bytes.");
	}
}
