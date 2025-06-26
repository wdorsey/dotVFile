using Microsoft.Data.Sqlite;

namespace BlobVFS;

internal class SqliteRepository
{
	public void Go(string databaseFile)
	{
		var connectionString = $"Data Source={databaseFile};";
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
	}
}
