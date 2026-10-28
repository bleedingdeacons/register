using Microsoft.Data.Sqlite;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// A backup holds everything committed, including what is still in the WAL,
/// and leaves the live database as it was.
/// </summary>
public sealed class DatabaseSnapshotTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("register-snapshot-").FullName;

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		Directory.Delete(_directory, recursive: true);
	}

	private string PathTo(string name) => Path.Combine(_directory, name);

	private static async Task<long> CountAsync(string path)
	{
		await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM Members";
		return (long)(await command.ExecuteScalarAsync())!;
	}

	private static async Task ExecuteAsync(SqliteConnection connection, string sql)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync();
	}

	[Fact]
	public async Task HoldsWhatIsStillInTheWal()
	{
		var live = PathTo("register.db");
		await using var app = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = live, Pooling = false }.ToString());
		await app.OpenAsync();
		await ExecuteAsync(app, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE Members (Id INTEGER PRIMARY KEY, Name TEXT)");
		await ExecuteAsync(app, "INSERT INTO Members (Name) VALUES ('A'), ('B'), ('C')");

		// The app is still open and nothing has been checkpointed, so these
		// rows are in register.db-wal, not register.db.
		Assert.True(new FileInfo(live + "-wal").Length > 0);

		await DatabaseSnapshot.WriteAsync(live, PathTo("backup.db"));

		Assert.Equal(3, await CountAsync(PathTo("backup.db")));
		Assert.False(File.Exists(PathTo("backup.db-wal")) && new FileInfo(PathTo("backup.db-wal")).Length > 0);

		// The app carries on with the original.
		await ExecuteAsync(app, "INSERT INTO Members (Name) VALUES ('D')");
		Assert.Equal(4, await CountAsync(live));
	}

	[Fact]
	public async Task NoDatabaseIsAnError() =>
		await Assert.ThrowsAsync<FileNotFoundException>(() => DatabaseSnapshot.WriteAsync(PathTo("missing.db"), PathTo("backup.db")));

	[Fact]
	public async Task WillNotWriteOverAnExistingFile()
	{
		var live = PathTo("register.db");
		await using (var app = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = live, Pooling = false }.ToString()))
		{
			await app.OpenAsync();
			await ExecuteAsync(app, "CREATE TABLE Members (Id INTEGER PRIMARY KEY)");
		}

		await File.WriteAllTextAsync(PathTo("backup.db"), "already here");

		await Assert.ThrowsAsync<SqliteException>(() => DatabaseSnapshot.WriteAsync(live, PathTo("backup.db")));
		Assert.Equal("already here", await File.ReadAllTextAsync(PathTo("backup.db")));
	}
}
