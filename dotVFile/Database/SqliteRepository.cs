using Microsoft.Data.Sqlite;

namespace dotVFile;

internal class SqliteRepository
{
	public void Go(string databaseFile)
	{
		var connectionString = $"Data Source={databaseFile};";
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
	}
}
