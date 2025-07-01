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

	public static string Alias(string alias, string column) => $"{alias}.{column}";

	public static object NullCoalesce(this object? value)
	{
		return value ?? DBNull.Value;
	}

	public static bool IsDbNull(this object? value)
	{
		return value == null || value == DBNull.Value;
	}

	public static Db.VFileQuery Latest(this Db.VFileQuery query)
	{
		query.VersionQuery = VFileInfoVersionQuery.Latest;
		return query;
	}

	public static Db.VFileQuery Versions(this Db.VFileQuery query)
	{
		query.VersionQuery = VFileInfoVersionQuery.Versions;
		return query;
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

	public static T GetEntityValues<T>(this T entity, SqliteDataReader reader, string tableAlias)
		where T : Db.Entity
	{
		entity.RowId = reader.GetInt64(tableAlias + "RowId");
		entity.Id = reader.GetGuid(tableAlias + "Id");
		entity.CreateTimestamp = reader.GetDateTimeOffset(tableAlias + "CreateTimestamp");
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

	public static SqliteCommand BuildSelect(
		this SqliteCommand cmd,
		Db.Select select)
	{
		cmd.CommandText += @$"
SELECT 
	{select.Columns.Sql} 
FROM 
	{select.From.Sql} 
WHERE 
	{select.Where.Sql};
";
		cmd.Parameters.AddRange(select.Parameters);
		return cmd;
	}

	public static SqliteCommand BuildDelete(
		this SqliteCommand cmd,
		Db.Delete delete)
	{
		cmd.CommandText += $" DELETE FROM {delete.From.Sql} WHERE {delete.Where.Sql}; ";
		cmd.Parameters.AddRange(delete.Parameters);
		return cmd;
	}

	public static SqliteCommand BuildDeleteByRowId(
		this SqliteCommand cmd,
		Db.SqlExpr from,
		List<long> rowIds)
	{
		var clauses = BuildInClause(rowIds, "RowId", SqliteType.Integer);

		foreach (var clause in clauses)
		{
			cmd.BuildDelete(new(from, clause));
		}

		return cmd;
	}

	/// <summary>
	/// Returns multiple InClauses because it needs to partition the values to
	/// limit how many values are in each IN statement.
	/// returns Sql: " {columnName} IN ({parameters}) "
	/// </summary>
	public static List<Db.SqlExpr> BuildInClause<T>(
		IEnumerable<T> values,
		string columnName,
		SqliteType type)
	{
		var result = new List<Db.SqlExpr>();

		var pIdx = 0;
		foreach (var value in values.Partition(50))
		{
			var parameters = new List<SqliteParameter>();
			var paramKeys = new List<string>();
			var itemIdx = 0;
			foreach (var item in value)
			{
				var key = $"@IN__{columnName}_{pIdx}_{itemIdx}";
				paramKeys.Add(key);
				var @param = NewParameter(key, type, item);
				parameters.Add(@param);
				itemIdx++;
			}
			result.Add(new($" {columnName} IN ({string.Join(',', paramKeys)}) ", parameters));
			pIdx++;
		}

		return result;
	}

	public static void ExecuteNonQuery(string connectionString, string sql)
	{
		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(sql, connection);
		connection.Open();
		cmd.ExecuteNonQuery();
	}

	public static void ExecuteDeleteByRowId(
		string connectionString,
		string tableName,
		List<long> rowIds)
	{
		if (rowIds.IsEmpty()) return;

		using var connection = new SqliteConnection(connectionString);
		var cmd = new SqliteCommand(string.Empty, connection);
		cmd.BuildDeleteByRowId(new(tableName), rowIds);
		connection.Open();
		cmd.ExecuteNonQuery();
	}

	public static Db.SqlExpr Merge(this List<Db.SqlExpr> exprs, string join)
	{
		var sb = new StringBuilder();
		var parameters = new List<SqliteParameter>();

		foreach (var expr in exprs)
		{
			if (expr.Sql.HasValue())
			{
				sb.Append(expr.Sql);
				sb.AppendLine(join);
				parameters.AddRange(expr.Parameters);
			}
		}

		return new(sb.ToString().TrimEnd(join.ToCharArray()), parameters);
	}

	public static Db.SqlExpr Wrap(this Db.SqlExpr expr, string start, string end)
	{
		expr.Sql = start + expr.Sql + end;
		return expr;
	}

	public static Db.SqlExpr Append(this Db.SqlExpr expr, Db.SqlExpr append)
	{
		return new List<Db.SqlExpr> { expr, append }.Merge("");
	}
}
