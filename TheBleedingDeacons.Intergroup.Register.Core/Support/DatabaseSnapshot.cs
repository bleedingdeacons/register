using Microsoft.Data.Sqlite;

namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// A consistent copy of a live SQLite database, for a backup.
///
/// <para><b>Why not copy the file.</b> The database runs in WAL mode, so
/// recent changes sit in the <c>-wal</c> file beside it until a checkpoint
/// folds them in. A copy of the <c>.db</c> alone misses them, and one taken
/// mid-checkpoint can be torn. <c>VACUUM INTO</c> writes a fresh database
/// from a single read transaction: everything committed, the WAL included,
/// as of one moment, while the app goes on using the original.</para>
/// </summary>
public static class DatabaseSnapshot
{
	/// <summary>
	/// Writes a snapshot of <paramref name="databasePath"/> to
	/// <paramref name="snapshotPath"/>, which must not exist yet.
	/// </summary>
	public static async Task WriteAsync(string databasePath, string snapshotPath, CancellationToken ct = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(databasePath);
		ArgumentException.ThrowIfNullOrEmpty(snapshotPath);

		if (!File.Exists(databasePath))
		{
			throw new FileNotFoundException("No database to back up.", databasePath);
		}

		// Read-only and unpooled: the backup must neither change the original
		// nor keep a handle on it afterwards.
		var connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadOnly,
			Pooling = false,
		}.ToString();

		await using var connection = new SqliteConnection(connectionString);
		await connection.OpenAsync(ct);

		await using var command = connection.CreateCommand();
		command.CommandText = "VACUUM INTO $path";
		command.Parameters.AddWithValue("$path", snapshotPath);
		await command.ExecuteNonQueryAsync(ct);
	}
}
