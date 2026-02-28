namespace GithubActivityChecker.Services;

/// <summary>
/// Service responsible for generating premium chart images using ScottPlot 5.
/// All chart methods return PNG byte arrays suitable for Telegram photo messages.
/// </summary>
public interface IPlotService
{
    // ── Core Charts ──
    /// <summary>Activity bar chart — total contributions per day.</summary>
    byte[] GenerateActivityChart(DateOnly[] dates, int[] totals, int days);

    /// <summary>Histogram — distribution of per-student contribution counts.</summary>
    byte[] GenerateDistributionHistogram(int[] studentTotals, int days);

    /// <summary>Dual-axis line — total contributions + active students trend.</summary>
    byte[] GenerateTrendLineChart(DateOnly[] dates, int[] totals, int[] activeStudents, int days);

    /// <summary>Donut chart — Active vs Inactive vs Pending Removal.</summary>
    byte[] GenerateProPieChart(int active, int inactive, int pendingRemoval, int days);

    // ── New Charts ──
    /// <summary>Clustered bar — contributions split by student status per day.</summary>
    byte[] GenerateStackedBarChart(DateOnly[] dates, int[] activeTotals, int[] inactiveTotals, int[] pendingTotals, int days);

    /// <summary>Area chart — cumulative contributions over time.</summary>
    byte[] GenerateAreaChart(DateOnly[] dates, int[] totals, int days);

    /// <summary>GitHub-style heatmap grid of daily activity.</summary>
    byte[] GenerateHeatmap(DateOnly[] dates, int[] contributions, int days);

    /// <summary>Scatter plot — students by contributions vs active days.</summary>
    byte[] GenerateScatterBubble(string[] usernames, int[] totalContributions, int[] activeDays, int days);

    /// <summary>Gauge / KPI — license utilization donut with percentage.</summary>
    byte[] GenerateGaugeChart(double utilizationPct, int active, int total, int days);

    /// <summary>Waterfall — period-over-period contribution changes.</summary>
    byte[] GenerateWaterfallChart(string[] periodLabels, int[] values, int days);

    /// <summary>Funnel — student engagement pipeline.</summary>
    byte[] GenerateFunnelChart(int totalStudents, int activeStudents, int weeklyActive, int dailyActive, int topContributors, int days);

    /// <summary>Horizontal bar — top N contributors leaderboard.</summary>
    byte[] GenerateTopChart(string[] usernames, int[] contributions, int days);

    /// <summary>Weekly comparison — contributions and active students per week.</summary>
    byte[] GenerateWeeklyComparison(string[] weekLabels, int[] weekTotals, int[] weekActiveStudents, int days);

    /// <summary>Day-of-week analysis — contribution patterns by weekday.</summary>
    byte[] GenerateDayOfWeekChart(int[] dayOfWeekTotals, int[] dayOfWeekCounts, int days);

    // ── Snapshot Cache ──
    /// <summary>Generate and cache all snapshot images for 1d/7d/30d.</summary>
    Task GenerateSnapshotsAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>Retrieve a cached snapshot image, or null if not found.</summary>
    byte[]? GetSnapshot(string key);
}
