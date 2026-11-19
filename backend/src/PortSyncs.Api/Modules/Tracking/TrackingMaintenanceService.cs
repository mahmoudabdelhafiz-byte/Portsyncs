using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;

namespace PortSyncs.Api.Modules.Tracking;

/// <summary>
/// Runs every hour: creates position partitions ahead of time, drops days older than the
/// retention window, and ends trips whose phone has gone silent (driver forgot to end it).
/// </summary>
public sealed class TrackingMaintenanceService(
    IServiceScopeFactory scopes, TrackingOptions options, ILogger<TrackingMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Tracking maintenance failed; will retry next hour.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortSyncsDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """
            SELECT ensure_positions_partition(d::date)
            FROM generate_series((now() AT TIME ZONE 'UTC')::date - {0}, (now() AT TIME ZONE 'UTC')::date + 2, interval '1 day') AS d
            """,
            [options.RetentionDays], ct);

        var dropped = await db.Database
            .SqlQueryRaw<int>(
                """SELECT drop_positions_partitions_before((now() AT TIME ZONE 'UTC')::date - {0}) AS "Value" """,
                options.RetentionDays)
            .SingleAsync(ct);

        var ended = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE trips
            SET status = 'ended', ended_at = now(), end_reason = 'stale'
            WHERE status = 'active' AND coalesce(last_ts, started_at) < now() - make_interval(hours => {0})
            """,
            [options.StaleTripHours], ct);

        if (dropped > 0 || ended > 0)
            logger.LogInformation("Tracking maintenance: dropped {Dropped} position days, ended {Ended} silent trips.", dropped, ended);
    }
}
