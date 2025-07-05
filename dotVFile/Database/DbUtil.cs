using System.Data;
using System.Text;
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

	public const string SelectInsertedRowId = "SELECT last_insert_rowid() AS RowId;";

	public static string Alias(string? alias) => alias.HasValue() ? $"{alias}." : string.Empty;
	public static string AliasColumn(string? alias, string column) => $"{Alias(alias)}{column}";

	public static string ParameterName(string name, int? index = null)
	{
		var prefixedName = name.StartsWith('@') ? name : $"@{name}";
		return index.HasValue ? $"{prefixedName}_{index}" : $"{prefixedName}";
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
		return cmd.AddParameter(name, null, type, value);
	}

	public static SqliteCommand AddParameter(
		this SqliteCommand cmd,
		string name,
		int? index,
		SqliteType type,
		object value)
	{
		cmd.Parameters.Add(NewParameter(ParameterName(name, index), type, value));

		return cmd;
	}

	public static SqliteCommand AddEntityParameters<T>(
		this SqliteCommand cmd,
		T entity,
		int? index = null)
		where T : Db.Entity
	{
		cmd.AddParameter("Id", index, SqliteType.Text, entity.Id.ToString());
		cmd.AddParameter("CreateTimestamp", index, SqliteType.Text, entity.CreateTimestamp.ToDefaultString());

		return cmd;
	}

	public static SqliteCommand BuildDeleteByRowId(
		this SqliteCommand cmd,
		string tableName,
		string columnName,
		List<long> rowIds)
	{
		if (rowIds.IsEmpty()) return cmd;

		var clause = BuildInClause(rowIds, columnName, null);

		cmd.CommandText += $"DELETE FROM {tableName} WHERE {clause.Sql};";
		cmd.Parameters.AddRange(clause.Parameters);

		return cmd;
	}

	// global index to prevent any parameter name collisions.
	private static int _BuildInClauseIndex = 0;
	public static Db.SqlExpr BuildInClause<T>(
		IEnumerable<T> values,
		string columnName,
		string? tableAlias)
	{
		if (values.IsEmpty()) return new("1=1", []);

		if (_BuildInClauseIndex >= 1000)
			_BuildInClauseIndex = 0;

		// use json_each that only requires one paramter
		// rather than building a parameter for each value, which is much
		// more complicated programatically, and slower.
		var key = $"@IN_{columnName}_{_BuildInClauseIndex}";
		var @in = $"{AliasColumn(tableAlias, columnName)} IN (SELECT e.value FROM json_each({key}) e)";
		var parameter = NewParameter(key, SqliteType.Text, values.ToJson());
		return new(@in, [parameter]);
	}

	public static T GetEntityValues<T>(this T entity, SqliteDataReader reader, string prefix = "")
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

	/// <summary>
	/// Expected to be used solely after inserts. Calls NextResult after each Read.
	/// </summary>
	public static List<T> ReadInsertedRowIds<T>(this List<T> entities, SqliteDataReader reader)
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

	public static List<TEntity> ExecuteGetById<TId, TEntity>(
		string connectionString,
		List<TId> ids,
		string tableName,
		string columnName,
		string select,
		Func<SqliteDataReader, List<TEntity>> read)
	{
		if (ids.IsEmpty()) return [];

		var inClause = BuildInClause(ids, columnName, tableName);

		var sql = $@"
SELECT
	{select}
FROM
	{tableName}
WHERE
	{inClause.Sql}
";
		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(sql, connection);
		cmd.Parameters.AddRange(inClause.Parameters);
		connection.Open();
		var reader = cmd.ExecuteReader();
		return read(reader);
	}

	public static string DebugString(this SqliteCommand cmd)
	{
		return cmd.CommandText;
	}
}
