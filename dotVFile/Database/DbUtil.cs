using System.Data;
using Microsoft.Data.Sqlite;

namespace dotVFile;

internal static class DbUtil
{
	public static T Stamp<T>(this T entity) where T : Db.Entity
	{
		if (entity.Id == Guid.Empty)
			entity.Id = Guid.NewGuid();
		entity.CreateTimestamp = DateTimeOffset.Now;
		return entity;
	}

	public static object NullCoalesce(this object? value)
	{
		return value ?? DBNull.Value;
	}

	public static DateTimeOffset ConvertDateTimeOffset(this object? value)
	{
		var str = value?.ToString() ?? throw new NoNullAllowedException("value");
		return DateTimeOffset.Parse(str);
	}

	public static DateTimeOffset? ConvertDateTimeOffsetNullable(this object? value)
	{
		string? str = value?.ToString();
		return str.HasValue() ? DateTimeOffset.Parse(str) : null;
	}

	public static long? GetInt64Nullable(this SqliteDataReader reader, string name)
	{
		var value = reader[name];
		return value != null && value != DBNull.Value ? Convert.ToInt64(value) : null;
	}

	public static DateTimeOffset GetDateTimeOffset(this SqliteDataReader reader, string name)
	{
		return reader[name].ConvertDateTimeOffset();
	}

	public static DateTimeOffset? GetDateTimeOffsetNullable(this SqliteDataReader reader, string name)
	{
		return reader[name].ConvertDateTimeOffsetNullable();
	}

	public static Guid GetGuid(this SqliteDataReader reader, string name)
	{
		return Guid.Parse(reader.GetString(name));
	}

	public static T ReadEntityValues<T>(this T entity, SqliteDataReader reader, string prefix = "")
		where T : Db.Entity
	{
		entity.RowId = reader.GetInt64(prefix + "RowId");
		entity.Id = reader.GetGuid(prefix + "Id");
		entity.CreateTimestamp = reader.GetDateTimeOffset(prefix + "CreateTimestamp");
		return entity;
	}

	public static void ReadRowId<T>(this List<T> entities, SqliteDataReader reader)
		where T : Db.Entity
	{
		foreach (var entity in entities)
		{
			reader.Read();
			entity.RowId = Convert.ToInt64(reader["RowId"]);
			reader.NextResult();
		}
	}

	public static void ExecuteNonQuery(string connectionString, string sql)
	{
		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		cmd.ExecuteNonQuery();
	}
}
