using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Data;

/// <summary>
/// EF Core connection interceptor that applies durability-critical SQLite
/// PRAGMAs to every connection when it opens.
///
/// Why this matters on Android:
///   • The default rollback journal mode leaves a window where a power loss
///     or process kill mid-write can corrupt the main DB file.
///   • WAL (Write-Ahead Logging) confines writes to a separate log file that
///     is atomically appended to, then checkpointed back into the main DB.
///     A crash in the middle of a write only ever loses the last transaction;
///     the main DB cannot be corrupted by a torn write.
///   • <c>synchronous=NORMAL</c> is the recommended pairing with WAL. It
///     fsyncs at each checkpoint rather than at every commit, which is dramatically
///     faster while still giving crash durability for committed transactions.
///   • <c>foreign_keys=ON</c> is off by default in SQLite for legacy reasons.
///     The sync service relies on FK checks to catch dangling references.
///   • <c>busy_timeout</c> gives SQLite a window to retry when another
///     connection holds the write lock — otherwise concurrent writes from
///     the snapshot service and a ViewModel can surface as SQLITE_BUSY.
///
/// Why an interceptor and not <c>DbContext.OnConfiguring</c>:
/// EF opens connections lazily and may reuse them across multiple operations.
/// Setting PRAGMAs in OnConfiguring fires once per context instance, but not
/// necessarily once per underlying connection. An <see cref="IDbConnectionInterceptor"/>
/// fires exactly when a connection opens, which is the right hook for
/// connection-scoped settings.
///
/// WAL mode is a <i>persistent</i> setting — once applied, it's recorded in
/// the DB file header and survives process restarts. The re-application on
/// every open is cheap (SQLite returns the existing mode immediately) and
/// ensures a freshly-created DB file picks up WAL on its first use.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
	private static readonly ILogger Logger = AppLogger.ForContext<SqlitePragmaInterceptor>();

	// Applied in order. Keep synchronous=NORMAL AFTER journal_mode=WAL —
	// SQLite rejects synchronous changes on a connection that hasn't
	// settled its journal mode.
	private static readonly string[] Pragmas =
	[
		"PRAGMA journal_mode=WAL;",
		"PRAGMA synchronous=NORMAL;",
		"PRAGMA foreign_keys=ON;",
		"PRAGMA busy_timeout=5000;",
	];

	public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
	{
		ApplyPragmas(connection);
		base.ConnectionOpened(connection, eventData);
	}

	public override Task ConnectionOpenedAsync(
		DbConnection connection,
		ConnectionEndEventData eventData,
		CancellationToken cancellationToken = default)
	{
		ApplyPragmas(connection);
		return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
	}

	private static void ApplyPragmas(DbConnection connection)
	{
		// Only applies to SQLite connections. If EF is ever reconfigured to
		// use another provider this interceptor silently no-ops.
		if (connection is not SqliteConnection) return;

		foreach (var pragma in Pragmas)
		{
			try
			{
				using var cmd = connection.CreateCommand();
				cmd.CommandText = pragma;
				cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				// A failed PRAGMA shouldn't take the app down — log and carry
				// on. In the worst case we fall back to SQLite defaults, which
				// still work, just with less crash resilience.
				Logger.Warning(ex, "Failed to apply {Pragma}", pragma);
			}
		}
	}
}