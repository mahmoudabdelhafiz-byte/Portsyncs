using System.Reflection;
using Npgsql;

namespace PortSyncs.Api.Database;

/// <summary>
/// Applies the embedded SQL files in Database/Migrations in name order, once each.
/// Plain SQL keeps the schema reviewable and portable (VPS, Azure or AWS PostgreSQL alike).
/// A PostgreSQL advisory lock stops two app instances migrating at the same time.
/// </summary>
public static class MigrationRunner
{
    private const long LockKey = 0x506F727453796E63; // "PortSync"

    public static async Task RunAsync(string connectionString, ILogger logger, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await Exec(conn, $"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            await Exec(conn, """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    name        text PRIMARY KEY,
                    applied_at  timestamptz NOT NULL DEFAULT now()
                )
                """, ct);

            var applied = new HashSet<string>();
            await using (var cmd = new NpgsqlCommand("SELECT name FROM schema_migrations", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) applied.Add(reader.GetString(0));

            foreach (var (name, sql) in LoadScripts())
            {
                if (applied.Contains(name)) continue;

                logger.LogInformation("Applying migration {Name}", name);
                await using var tx = await conn.BeginTransactionAsync(ct);
                await using (var cmd = new NpgsqlCommand(sql, conn, tx)) await cmd.ExecuteNonQueryAsync(ct);
                await using (var cmd = new NpgsqlCommand("INSERT INTO schema_migrations (name) VALUES (@n)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("n", name);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
            }
        }
        finally
        {
            await Exec(conn, $"SELECT pg_advisory_unlock({LockKey})", ct);
        }
    }

    private static IEnumerable<(string Name, string Sql)> LoadScripts()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string marker = ".Database.Migrations.";
        foreach (var resource in asm.GetManifestResourceNames()
                     .Where(r => r.Contains(marker) && r.EndsWith(".sql", StringComparison.Ordinal))
                     .OrderBy(r => r, StringComparer.Ordinal))
        {
            using var stream = asm.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var name = resource[(resource.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
            yield return (name, reader.ReadToEnd());
        }
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
