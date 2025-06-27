namespace dotVFile;

internal static class DatabaseExtensions
{
	public static T Stamp<T>(this T entity) where T : Db.Entity
	{
		entity.CreateTimestamp = DateTimeOffset.Now;
		return entity;
	}
}
