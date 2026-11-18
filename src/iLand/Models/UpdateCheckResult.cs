namespace iLand.Models;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NotConfigured,
    NoReleases,
    RateLimited,
    Failed
}

/// <summary>Hasil satu pengecekan pembaruan manual.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, string? LatestVersion = null, string? ReleaseUrl = null);
