namespace GithubActivityChecker.Services;

/// <summary>
/// Orchestrates the full sync of all student GitHub activity.
/// </summary>
public interface ISyncService
{
    Task RunFullSyncAsync(CancellationToken ct = default);
}
