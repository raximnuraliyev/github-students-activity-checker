namespace GithubActivityChecker.Services;

/// <summary>
/// Service responsible for generating chart images using ScottPlot.
/// All methods return PNG byte arrays suitable for Telegram photo messages.
/// </summary>
public interface IPlotService
{
    /// <summary>Activity bar chart — total contributions per day.</summary>
    byte[] GenerateActivityChart(DateOnly[] dates, int[] totals, int days);

    /// <summary>Histogram — distribution of per-student contribution counts.</summary>
    byte[] GenerateDistributionHistogram(int[] studentTotals, int days);

    /// <summary>Line graph — total contributions + active students trend.</summary>
    byte[] GenerateTrendLineChart(DateOnly[] dates, int[] totals, int[] activeStudents, int days);

    /// <summary>Pie chart — Active vs Inactive vs Pending Removal.</summary>
    byte[] GenerateProPieChart(int active, int inactive, int pendingRemoval, int days);

    /// <summary>Generate and cache all snapshot images for the given day count.</summary>
    Task GenerateSnapshotsAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>Retrieve a cached snapshot image, or null if not found.</summary>
    byte[]? GetSnapshot(string key);
}
