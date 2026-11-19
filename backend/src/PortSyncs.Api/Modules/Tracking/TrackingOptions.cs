namespace PortSyncs.Api.Modules.Tracking;

/// <summary>Bound from the "Tracking" section (Tracking__RetentionDays, ...).</summary>
public sealed class TrackingOptions
{
    /// <summary>Days of GPS history kept (free tier: 7). Whole days are dropped after this.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Active trips with no position for this long are ended automatically.</summary>
    public int StaleTripHours { get; set; } = 12;

    /// <summary>Most points accepted in one upload; phones batch points while offline.</summary>
    public int MaxPointsPerBatch { get; set; } = 500;

    /// <summary>A truck shown on the live map is "stale" after this many minutes without a position.</summary>
    public int LiveStaleMinutes { get; set; } = 10;
}
