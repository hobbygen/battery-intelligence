using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Data.Sqlite;

/// <summary>One embedded migration script.</summary>
/// <param name="Version">Sequential schema version.</param>
/// <param name="Name">Human-readable name, from the resource file name.</param>
/// <param name="Sql">The script body.</param>
internal sealed partial record Migration(int Version, string Name, string Sql)
{
    [GeneratedRegex(@"V(\d+)__(.+)\.sql$", RegexOptions.IgnoreCase)]
    public static partial Regex FileNamePattern { get; }
}

/// <summary>
/// Applies embedded, versioned SQL scripts in order and records each in
/// <c>SchemaMigration</c> (docs/database.md section 7; specification section 64).
/// </summary>
/// <remarks>
/// Never drops or rewrites a user-data column destructively — every migration
/// from V002 onward must be additive, or must copy existing rows when a table
/// requires rebuilding. A failed migration rolls back and leaves the previous
/// schema version intact rather than attempting an automatic "recovery" that
/// could discard history (docs/database.md section 7).
/// </remarks>
public sealed class DatabaseMigrator
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(ISqliteConnectionFactory connectionFactory, ILogger<DatabaseMigrator> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Brings the database up to the latest embedded schema version.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the database is now at the latest version
    /// (including "already was"); <see langword="false"/> if a migration failed
    /// and was rolled back, leaving the previous version intact. This never
    /// throws for an expected failure — a database problem must degrade the app,
    /// not crash it (specification section 44).
    /// </returns>
    public async Task<bool> MigrateAsync(CancellationToken cancellationToken = default)
    {
        List<Migration> migrations = LoadEmbeddedMigrations();
        if (migrations.Count == 0)
        {
            _logger.LogWarning("No embedded migrations found.");
            return true;
        }

        try
        {
            await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (!await PassesIntegrityCheckAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                // A corrupt database must NOT be "repaired" by deleting it — months
                // of battery history is the most valuable thing the app holds
                // (docs/testing.md section 5). The app runs without persistence
                // this session; Diagnostics shows the write path Degraded.
                _logger.LogError(
                    "Database failed its integrity check. The file has NOT been modified. " +
                    "History recording is paused this session — see Diagnostics.");
                return false;
            }

            int currentVersion = await GetCurrentVersionAsync(connection, cancellationToken).ConfigureAwait(false);

            foreach (Migration migration in migrations.Where(m => m.Version > currentVersion).OrderBy(m => m.Version))
            {
                if (currentVersion > 0)
                {
                    // A fresh (version 0) database has no user data yet, so a
                    // backup before the very first migration would be an empty
                    // file. From the second migration onward, back up first.
                    BackupBeforeMigration(migration.Version);
                }

                await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Applied migration V{Version} ({Name}).", migration.Version, migration.Name);
                currentVersion = migration.Version;
            }

            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Database migration failed; the application will run without persistence this session.");
            return false;
        }
    }

    /// <summary>
    /// Runs <c>PRAGMA quick_check</c> — a fast, page-level structural check that
    /// catches a truncated or overwritten file without the full <c>integrity_check</c>
    /// scan. A brand-new (empty) database passes.
    /// </summary>
    private async Task<bool> PassesIntegrityCheckAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteCommand check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check(1);";
            object? result = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is "ok";
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Database integrity check could not run.");
            return false;
        }
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand checkTable = connection.CreateCommand();
        checkTable.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'SchemaMigration';";
        object? tableName = await checkTable.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (tableName is null)
        {
            // SchemaMigration itself does not exist yet: this database predates
            // any migration, including V001.
            return 0;
        }

        await using SqliteCommand maxVersion = connection.CreateCommand();
        maxVersion.CommandText = "SELECT MAX(Version) FROM SchemaMigration;";
        object? result = await maxVersion.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is long version ? (int)version : 0;
    }

    private async Task ApplyMigrationAsync(SqliteConnection connection, Migration migration, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using (SqliteCommand script = connection.CreateCommand())
            {
                script.Transaction = transaction;
                script.CommandText = migration.Sql;
                await script.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (SqliteCommand record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO SchemaMigration (Version, Name, AppliedUtc) VALUES ($version, $name, $appliedUtc);";
                record.Parameters.AddWithValue("$version", migration.Version);
                record.Parameters.AddWithValue("$name", migration.Name);
                record.Parameters.AddWithValue("$appliedUtc", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private void BackupBeforeMigration(int upcomingVersion)
    {
        try
        {
            string source = _connectionFactory.DatabasePath;
            if (!File.Exists(source))
            {
                return;
            }

            string backupPath = $"{source}.bak-v{upcomingVersion:000}";
            File.Copy(source, backupPath, overwrite: true);
            _logger.LogInformation("Backed up database to {Path} before applying V{Version}.", backupPath, upcomingVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed backup should not block the migration attempt itself;
            // it is a best-effort safety net, not a precondition.
            _logger.LogWarning(ex, "Could not back up the database before migration V{Version}.", upcomingVersion);
        }
    }

    private static List<Migration> LoadEmbeddedMigrations()
    {
        Assembly assembly = typeof(DatabaseMigrator).Assembly;
        List<Migration> migrations = [];

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            Match match = Migration.FileNamePattern.Match(resourceName);
            if (!match.Success)
            {
                continue;
            }

            int version = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            string name = match.Groups[2].Value;

            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using StreamReader reader = new(stream);
            string sql = reader.ReadToEnd();

            migrations.Add(new Migration(version, name, sql));
        }

        return migrations;
    }
}
