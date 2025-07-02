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
		int? idx = null)
		where T : Db.Entity
	{
		cmd.AddParameter("Id", idx, SqliteType.Text, entity.Id.ToString());
		cmd.AddParameter("CreateTimestamp", idx, SqliteType.Text, entity.CreateTimestamp.ToDefaultString());

		return cmd;
	}

	public static Db.SqlExpr Merge(this List<Db.SqlExpr> exprs, string join)
	{
		var sb = new StringBuilder();
		var parameters = new List<SqliteParameter>();

		var rest = false;
		foreach (var expr in exprs)
		{
			if (expr.Sql.HasValue())
			{
				if (rest)
					sb.Append(join);
				sb.AppendLine(expr.Sql);
				parameters.AddRange(expr.Parameters);
				rest = true;
			}
		}

		return new(sb.ToString(), parameters);
	}

	public static Db.SqlExpr Wrap(this Db.SqlExpr expr, string start, string end)
	{
		if (expr.Sql.IsEmpty()) return expr;
		expr.Sql = start + expr.Sql + end;
		return expr;
	}

	public static Db.SqlExpr And(this Db.SqlExpr expr, Db.SqlExpr append)
	{
		return new List<Db.SqlExpr> { expr, append }.Merge(" AND ");
	}

	public static string GetSql(this Db.SqlExpr expr)
	{
		return expr.Sql.Trim(Environment.NewLine.ToCharArray());
	}

	public static List<Db.SelectColumn> AddEntityColumns(
		this List<Db.SelectColumn> columns,
		string? tableAlias = null,
		bool prefixAlias = false)
	{
		columns.Add(new("RowId", tableAlias, prefixAlias));
		columns.Add(new("Id", tableAlias, prefixAlias));
		columns.Add(new("CreateTimestamp", tableAlias, prefixAlias));
		return columns;
	}

	public static string ToSql(this List<Db.SelectColumn> columns)
	{
		var result = new List<string>();

		foreach (var column in columns)
		{
			var @as = column.ColumnNamePrefixAlias
				? $" AS {column.TableAlias}{column.Name}"
				: string.Empty;

			result.Add($"\t{AliasColumn(column.TableAlias, column.Name)}{@as}");
		}

		return string.Join($",{Environment.NewLine}", result);
	}

	public static SqliteCommand BuildSelect(
		this SqliteCommand cmd,
		Db.Select select)
	{
		var where = select.Where.Sql.HasValue() ? "WHERE" : string.Empty;

		cmd.CommandText += @$"
SELECT 
{select.SelectColumns.ToSql()} 
FROM 
{select.From.GetSql()} 
{where}
{select.Where.GetSql()};
";
		cmd.Parameters.AddRange(select.Parameters);
		return cmd;
	}

	public static SqliteCommand BuildDelete(
		this SqliteCommand cmd,
		Db.Delete delete)
	{
		cmd.CommandText += $" DELETE FROM {delete.From.GetSql()} WHERE {delete.Where.GetSql()}; ";
		cmd.Parameters.AddRange(delete.Parameters);
		return cmd;
	}

	public static SqliteCommand BuildDeleteByRowId(
		this SqliteCommand cmd,
		Db.SqlExpr from,
		List<long> rowIds)
	{
		var clauses = BuildInClause(rowIds, "RowId", null, SqliteType.Integer);

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
		string? tableAlias,
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

			if (value.Count > 0)
			{
				result.Add(new($"\t{AliasColumn(tableAlias, columnName)} IN ({string.Join(',', paramKeys)}) ", parameters));
			}

			pIdx++;
		}

		return result;
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

	public static string DebugString(this SqliteCommand cmd)
	{
		return cmd.CommandText;
	}
}
