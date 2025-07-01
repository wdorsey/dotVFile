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

	public static bool IsDbNull(this object? value)
	{
		return value == null || value == DBNull.Value;
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
		return IsDbNull(value) ? null : Convert.ToInt64(value);
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

	public static byte[] GetBytes(this SqliteDataReader reader, string name)
	{
		return (byte[])reader[name];
	}

	public static SqliteParameter NewParameter(string name, SqliteType type, object? value)
	{
		return new SqliteParameter(name, type)
		{
			Value = NullCoalesce(value)
		};
	}

	public static SqliteCommand AddParameter(
		this SqliteCommand cmd,
		string name,
		SqliteType type,
		object value)
	{
		cmd.Parameters.Add(NewParameter(name, type, value));

		return cmd;
	}

	public static T ReadEntityValues<T>(this T entity, SqliteDataReader reader, string prefix = "")
		where T : Db.Entity
	{
		entity.RowId = reader.GetInt64(prefix + "RowId");
		entity.Id = reader.GetGuid(prefix + "Id");
		entity.CreateTimestamp = reader.GetDateTimeOffset(prefix + "CreateTimestamp");
		return entity;
	}

	public static T ReadRowId<T>(this T entity, SqliteDataReader reader)
		where T : Db.Entity
	{
		reader.Read();
		entity.RowId = Convert.ToInt64(reader["RowId"]);
		return entity;
	}

	public static List<T> ReadRowId<T>(this List<T> entities, SqliteDataReader reader)
		where T : Db.Entity
	{
		foreach (var entity in entities)
		{
			entity.ReadRowId(reader);
			reader.NextResult();
		}
		return entities;
	}

	public static void ExecuteNonQuery(string connectionString, string sql)
	{
		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		cmd.ExecuteNonQuery();
	}

	public static Db.InParams BuildInParams<T>(
		IEnumerable<T> values,
		string paramName,
		SqliteType type)
	{
		var parameters = new List<SqliteParameter>();
		var paramKeys = new List<string>();
		var idx = 0;
		foreach (var value in values)
		{
			var key = $"{paramName}_{idx++}";
			paramKeys.Add(key);
			var @param = NewParameter(key, type, value);
			parameters.Add(@param);
		}

		var sql = string.Join(',', paramKeys);

		return new Db.InParams(sql, parameters);
	}

	public static SqliteCommand BuildDeleteByRowId(
		this SqliteCommand cmd,
		string tableName,
		List<long> rowIds)
	{
		if (rowIds.IsEmpty()) return cmd;

		int idx = 0;
		foreach (var list in rowIds.Partition(50))
		{
			var inParams = BuildInParams(list, $"@D_{tableName}_{idx}_RowId", SqliteType.Integer);
			cmd.CommandText += $@" DELETE FROM {tableName} WHERE RowId IN ({inParams.Sql}); ";
			cmd.Parameters.AddRange(inParams.Parameters);
			idx++;
		}

		return cmd;
	}

	public static void ExecuteDeleteByRowId(
		string connectionString,
		string tableName,
		List<long> rowIds)
	{
		if (rowIds.IsEmpty()) return;

		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildDeleteByRowId(tableName, rowIds);
		connection.Open();
		cmd.ExecuteNonQuery();
	}
}
