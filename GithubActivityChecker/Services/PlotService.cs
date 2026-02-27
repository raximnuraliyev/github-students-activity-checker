using System.Collections.Concurrent;
using GithubActivityChecker.Data;
using GithubActivityChecker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScottPlot;

namespace GithubActivityChecker.Services;

/// <summary>
/// Generates high-quality chart images (1200×800 PNG) using ScottPlot 5.
/// Every chart includes: title, axis labels, data-value annotations, a summary
/// stats box, and a branded watermark so the image is self-explanatory.
/// </summary>
public class PlotService : IPlotService
{
    private const int Width = 1200;
    private const int Height = 800;

    private readonly ILogger<PlotService> _logger;
    private readonly ConcurrentDictionary<string, byte[]> _snapshotCache = new();

    // University branding colors
    private static readonly ScottPlot.Color BrandBlue = new(41, 98, 255);
    private static readonly ScottPlot.Color BrandGreen = new(0, 200, 83);
    private static readonly ScottPlot.Color BrandOrange = new(255, 145, 0);
    private static readonly ScottPlot.Color BrandRed = new(213, 0, 0);
    private static readonly ScottPlot.Color BrandGray = new(158, 158, 158);
    private static readonly ScottPlot.Color BgLight = new(245, 245, 250);

    public PlotService(ILogger<PlotService> logger)
    {
        _logger = logger;
    }

    // ==================== Helpers ====================

    /// <summary>Add a stats annotation box to the top-right of the plot.</summary>
    private static void AddStatsBox(Plot plt, string text)
    {
        var ann = plt.Add.Annotation(text);
        ann.LabelFontSize = 14;
        ann.LabelFontColor = Colors.Black;
        ann.LabelBackgroundColor = new ScottPlot.Color(255, 255, 255, 220);
        ann.LabelBorderColor = BrandBlue;
        ann.LabelBorderWidth = 1.5f;
        ann.Alignment = Alignment.UpperRight;
    }

    /// <summary>Add a watermark/timestamp annotation at the bottom-right.</summary>
    private static void AddWatermark(Plot plt, int days)
    {
        var ts = plt.Add.Annotation($"GitHub Activity Monitor · {PeriodLabel(days)} · Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        ts.LabelFontSize = 10;
        ts.LabelFontColor = new ScottPlot.Color(120, 120, 120);
        ts.LabelBackgroundColor = ScottPlot.Color.FromHex("#00000000");
        ts.LabelBorderWidth = 0;
        ts.Alignment = Alignment.LowerRight;
    }

    /// <summary>Apply common styling to a plot.</summary>
    private static void StylePlot(Plot plt)
    {
        plt.FigureBackground.Color = Colors.White;
        plt.DataBackground.Color = BgLight;
    }

    // ==================== Chart Generators ====================

    public byte[] GenerateActivityChart(DateOnly[] dates, int[] totals, int days)
    {
        var plt = new Plot();
        StylePlot(plt);
        plt.Title($"📊 Daily Student Contributions — Last {PeriodLabel(days)}");
        plt.YLabel("Total Contributions");
        plt.XLabel("Date");

        if (dates.Length == 0)
        {
            AddStatsBox(plt, "No contribution data\navailable for this period.");
            AddWatermark(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] positions = new double[dates.Length];
        string[] labels = new string[dates.Length];
        double[] values = new double[totals.Length];

        for (int i = 0; i < dates.Length; i++)
        {
            positions[i] = i;
            labels[i] = dates[i].ToString("MM/dd");
            values[i] = totals[i];
        }

        var bars = plt.Add.Bars(positions, values);
        bars.Color = BrandBlue;

        // Value labels on top of each bar
        for (int i = 0; i < positions.Length; i++)
        {
            if (values[i] > 0)
            {
                var txt = plt.Add.Text(totals[i].ToString("N0"), positions[i], values[i]);
                txt.LabelFontSize = 10;
                txt.LabelFontColor = BrandBlue;
                txt.LabelAlignment = Alignment.LowerCenter;
            }
        }

        // Summary stats
        int total = totals.Sum();
        double avg = totals.Average();
        int peak = totals.Max();
        int peakIdx = Array.IndexOf(totals, peak);
        string peakDate = peakIdx >= 0 ? dates[peakIdx].ToString("MMM dd") : "N/A";
        int zeroDays = totals.Count(t => t == 0);

        AddStatsBox(plt,
            $"Total: {total:N0}\n" +
            $"Daily Avg: {avg:F1}\n" +
            $"Peak: {peak:N0} ({peakDate})\n" +
            $"Zero-activity days: {zeroDays}/{dates.Length}");

        // X ticks
        int step = Math.Max(1, dates.Length / 15);
        var ticks = new List<Tick>();
        for (int i = 0; i < dates.Length; i += step)
            ticks.Add(new Tick(positions[i], labels[i]));
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
        plt.Axes.Bottom.TickLabelStyle.Rotation = 45;

        AddWatermark(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    public byte[] GenerateDistributionHistogram(int[] studentTotals, int days)
    {
        var plt = new Plot();
        StylePlot(plt);
        plt.Title($"📊 Student Contribution Distribution — Last {PeriodLabel(days)}");
        plt.YLabel("Number of Students");
        plt.XLabel("Contribution Range");

        if (studentTotals.Length == 0)
        {
            AddStatsBox(plt, "No students found.");
            AddWatermark(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        // Bins: 0, 1-5, 6-10, 11-20, 21-50, 51-100, 100+
        var binLabels = new[] { "0\n(Inactive)", "1 - 5\n(Very Low)", "6 - 10\n(Low)", "11 - 20\n(Moderate)", "21 - 50\n(Good)", "51 - 100\n(High)", "100+\n(Very High)" };
        var binCounts = new double[7];

        foreach (var total in studentTotals)
        {
            if (total == 0) binCounts[0]++;
            else if (total <= 5) binCounts[1]++;
            else if (total <= 10) binCounts[2]++;
            else if (total <= 20) binCounts[3]++;
            else if (total <= 50) binCounts[4]++;
            else if (total <= 100) binCounts[5]++;
            else binCounts[6]++;
        }

        double[] positions = Enumerable.Range(0, 7).Select(i => (double)i).ToArray();
        var bars = plt.Add.Bars(positions, binCounts);

        ScottPlot.Color[] barColors = [BrandRed, BrandOrange, BrandOrange, BrandGray, BrandBlue, BrandGreen, BrandGreen];
        for (int i = 0; i < bars.Bars.Count && i < barColors.Length; i++)
            bars.Bars[i].FillColor = barColors[i];

        // Count + percentage label on each bar
        int totalStudents = studentTotals.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            if (binCounts[i] > 0)
            {
                double pct = binCounts[i] / totalStudents * 100;
                var txt = plt.Add.Text($"{(int)binCounts[i]} ({pct:F1}%)", positions[i], binCounts[i]);
                txt.LabelFontSize = 11;
                txt.LabelFontColor = barColors[i];
                txt.LabelAlignment = Alignment.LowerCenter;
                txt.LabelBold = true;
            }
        }

        var ticks = positions.Select((p, i) => new Tick(p, binLabels[i])).ToArray();
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);

        // Summary stats box
        double avg = studentTotals.Average();
        double median = GetMedian(studentTotals);
        int maxContrib = studentTotals.Max();
        int zeroCount = studentTotals.Count(s => s == 0);
        double inactiveRate = (double)zeroCount / totalStudents * 100;

        AddStatsBox(plt,
            $"Students: {totalStudents:N0}\n" +
            $"Average: {avg:F1} contributions\n" +
            $"Median: {median:F0} contributions\n" +
            $"Max: {maxContrib:N0}\n" +
            $"Inactive (0): {zeroCount} ({inactiveRate:F1}%)");

        AddWatermark(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    public byte[] GenerateTrendLineChart(DateOnly[] dates, int[] totals, int[] activeStudents, int days)
    {
        var plt = new Plot();
        StylePlot(plt);
        plt.Title($"📈 GitHub Usage Trend — Last {PeriodLabel(days)}");
        plt.YLabel("Total Contributions");
        plt.XLabel("Date");

        if (dates.Length == 0)
        {
            AddStatsBox(plt, "No trend data\navailable for this period.");
            AddWatermark(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] xs = new double[dates.Length];
        string[] labels = new string[dates.Length];
        double[] yTotals = new double[totals.Length];
        double[] yActive = new double[activeStudents.Length];

        for (int i = 0; i < dates.Length; i++)
        {
            xs[i] = i;
            labels[i] = dates[i].ToString("MM/dd");
            yTotals[i] = totals[i];
            yActive[i] = activeStudents[i];
        }

        var line1 = plt.Add.Scatter(xs, yTotals);
        line1.Color = BrandBlue;
        line1.LineWidth = 2.5f;
        line1.MarkerSize = 6;
        line1.LegendText = "Contributions";

        var line2 = plt.Add.Scatter(xs, yActive);
        line2.Color = BrandGreen;
        line2.LineWidth = 2.5f;
        line2.MarkerSize = 6;
        line2.LinePattern = LinePattern.Dashed;
        line2.LegendText = "Active Students";
        line2.Axes.YAxis = plt.Axes.Right;

        plt.Axes.Right.Label.Text = "Active Students";
        plt.ShowLegend();

        // Mark peak contribution day
        if (totals.Length > 0)
        {
            int peakIdx = Array.IndexOf(totals, totals.Max());
            var peakMark = plt.Add.Text($"▲ Peak: {totals[peakIdx]:N0}\n({dates[peakIdx]:MMM dd})", xs[peakIdx], yTotals[peakIdx]);
            peakMark.LabelFontSize = 11;
            peakMark.LabelFontColor = BrandBlue;
            peakMark.LabelBold = true;
            peakMark.LabelAlignment = Alignment.LowerCenter;
        }

        // Summary stats
        string trend = totals.Length >= 2
            ? (totals[^1] > totals[0] ? "↑ Upward" : totals[^1] < totals[0] ? "↓ Downward" : "→ Flat")
            : "N/A";
        int totalSum = totals.Sum();
        double avgContrib = totals.Average();
        double avgActive = activeStudents.Average();

        AddStatsBox(plt,
            $"Trend: {trend}\n" +
            $"Total Contributions: {totalSum:N0}\n" +
            $"Avg Contributions/Day: {avgContrib:F1}\n" +
            $"Avg Active Students/Day: {avgActive:F1}\n" +
            $"Data Points: {dates.Length} days");

        int step = Math.Max(1, dates.Length / 15);
        var ticks = new List<Tick>();
        for (int i = 0; i < dates.Length; i += step)
            ticks.Add(new Tick(xs[i], labels[i]));
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
        plt.Axes.Bottom.TickLabelStyle.Rotation = 45;

        AddWatermark(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    public byte[] GenerateProPieChart(int active, int inactive, int pendingRemoval, int days)
    {
        var plt = new Plot();
        StylePlot(plt);
        int total = active + inactive + pendingRemoval;
        plt.Title($"🥧 Pro License Status — {total:N0} Students");

        var slices = new List<PieSlice>();

        double activePct = total > 0 ? (double)active / total * 100 : 0;
        double inactivePct = total > 0 ? (double)inactive / total * 100 : 0;
        double pendingPct = total > 0 ? (double)pendingRemoval / total * 100 : 0;

        if (active > 0)
            slices.Add(new PieSlice { Value = active, Label = $"Active: {active} ({activePct:F1}%)", FillColor = BrandGreen });
        if (inactive > 0)
            slices.Add(new PieSlice { Value = inactive, Label = $"Inactive: {inactive} ({inactivePct:F1}%)", FillColor = BrandOrange });
        if (pendingRemoval > 0)
            slices.Add(new PieSlice { Value = pendingRemoval, Label = $"Pending Removal: {pendingRemoval} ({pendingPct:F1}%)", FillColor = BrandRed });

        if (slices.Count == 0)
            slices.Add(new PieSlice { Value = 1, Label = "No Data", FillColor = BrandGray });

        var pie = plt.Add.Pie(slices);
        pie.ExplodeFraction = 0.05;

        plt.ShowLegend();
        plt.Axes.Frameless();

        // Add summary info as annotation
        double utilizationRate = total > 0 ? activePct : 0;
        string riskLevel = utilizationRate >= 80 ? "🟢 Low Risk" :
                           utilizationRate >= 60 ? "🟡 Medium Risk" :
                           utilizationRate >= 40 ? "🟠 High Risk" : "🔴 Critical";

        var ann = plt.Add.Annotation(
            $"License Utilization: {utilizationRate:F1}%\n" +
            $"Risk Level: {riskLevel}\n" +
            $"Candidates for removal: {pendingRemoval}\n" +
            $"Total students: {total:N0}");
        ann.LabelFontSize = 13;
        ann.LabelFontColor = Colors.Black;
        ann.LabelBackgroundColor = new ScottPlot.Color(255, 255, 255, 220);
        ann.LabelBorderColor = BrandBlue;
        ann.LabelBorderWidth = 1.5f;
        ann.Alignment = Alignment.LowerLeft;

        AddWatermark(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ==================== Snapshot Cache ====================

    public byte[]? GetSnapshot(string key)
    {
        return _snapshotCache.TryGetValue(key, out var bytes) ? bytes : null;
    }

    public async Task GenerateSnapshotsAsync(IServiceProvider services, CancellationToken ct)
    {
        _logger.LogInformation("Generating visualization snapshots...");

        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (int days in new[] { 1, 7, 30 })
            {
                ct.ThrowIfCancellationRequested();
                var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

                // Activity chart data
                var activityData = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.Date)
                    .Select(g => new { Date = g.Key, Total = g.Sum(x => x.Count) })
                    .OrderBy(x => x.Date)
                    .ToListAsync(ct);

                var actDates = activityData.Select(d => d.Date).ToArray();
                var actTotals = activityData.Select(d => d.Total).ToArray();
                _snapshotCache[$"activity_{days}d"] = GenerateActivityChart(actDates, actTotals, days);

                // Distribution data
                var studentSums = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.StudentId)
                    .Select(g => g.Sum(x => x.Count))
                    .ToListAsync(ct);

                var activeStudentIds = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .Select(dc => dc.StudentId)
                    .Distinct()
                    .CountAsync(ct);

                var totalStudents = await db.Students.CountAsync(ct);
                var zeroCount = totalStudents - activeStudentIds;
                for (int i = 0; i < zeroCount; i++)
                    studentSums.Add(0);

                _snapshotCache[$"dist_{days}d"] = GenerateDistributionHistogram(studentSums.ToArray(), days);

                // Trend data
                var trendData = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Total = g.Sum(x => x.Count),
                        ActiveStudents = g.Select(x => x.StudentId).Distinct().Count()
                    })
                    .OrderBy(x => x.Date)
                    .ToListAsync(ct);

                var trendDates = trendData.Select(d => d.Date).ToArray();
                var trendTotals = trendData.Select(d => d.Total).ToArray();
                var trendActive = trendData.Select(d => d.ActiveStudents).ToArray();
                _snapshotCache[$"trend_{days}d"] = GenerateTrendLineChart(trendDates, trendTotals, trendActive, days);

                // Pro pie data
                var activeCount = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
                var inactiveCount = await db.Students.CountAsync(s => s.Status == StudentStatus.Inactive, ct);
                var pendingCount = await db.Students.CountAsync(s => s.Status == StudentStatus.Pending_Removal, ct);
                _snapshotCache[$"pro_{days}d"] = GenerateProPieChart(activeCount, inactiveCount, pendingCount, days);
            }

            _logger.LogInformation("Visualization snapshots generated: {Count} charts cached", _snapshotCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate visualization snapshots");
        }
    }

    // ==================== Utilities ====================

    private static double GetMedian(int[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        if (n == 0) return 0;
        if (n % 2 == 1) return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static string PeriodLabel(int days) => days switch
    {
        1 => "24h",
        7 => "7 Days",
        30 => "30 Days",
        _ => $"{days} Days"
    };
}
