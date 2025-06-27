namespace dotVFile;

internal static class DbUtil
{
	public static T Stamp<T>(this T entity) where T : Db.Entity
	{
		entity.CreateTimestamp = DateTimeOffset.Now;
		return entity;
	}

	public static object NullCoalesce(object? value)
	{
		return value ?? DBNull.Value;
	}

	public static DateTimeOffset ConvertDateTimeOffset(string value)
	{
		return DateTimeOffset.Parse(value);
	}

	public static DateTimeOffset? ConvertDateTimeOffsetNullable(object? value)
	{
		string? str = value?.ToString();
		return str.HasValue() ? ConvertDateTimeOffset(str) : null;
	}
}
