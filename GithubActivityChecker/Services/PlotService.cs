using System.Collections.Concurrent;
using GithubActivityChecker.Data;
using GithubActivityChecker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScottPlot;

namespace GithubActivityChecker.Services;

/// <summary>
/// Generates premium-quality chart images (1400×900 PNG) using ScottPlot 5.
/// Features dark theme, gradient fills, data annotations, branded layouts,
/// and extensive chart variety — bar, line, area, donut, heatmap, scatter,
/// gauge, waterfall, funnel, stacked bar, and more.
/// </summary>
public class PlotService : IPlotService
{
    private const int Width = 1400;
    private const int Height = 900;
    private const int WideWidth = 1600;
    private const int TallHeight = 1000;

    private readonly ILogger<PlotService> _logger;
    private readonly ConcurrentDictionary<string, byte[]> _snapshotCache = new();

    // ── Premium Color Palette ──
    private static readonly ScottPlot.Color Cyan = new(0, 188, 212);
    private static readonly ScottPlot.Color ElectricBlue = new(59, 130, 246);
    private static readonly ScottPlot.Color NeonGreen = new(16, 185, 129);
    private static readonly ScottPlot.Color Amber = new(245, 158, 11);
    private static readonly ScottPlot.Color Rose = new(244, 63, 94);
    private static readonly ScottPlot.Color Violet = new(139, 92, 246);
    private static readonly ScottPlot.Color Coral = new(251, 113, 133);
    private static readonly ScottPlot.Color Teal = new(20, 184, 166);
    private static readonly ScottPlot.Color SkyBlue = new(56, 189, 248);
    private static readonly ScottPlot.Color Lime = new(132, 204, 22);
    private static readonly ScottPlot.Color Fuchsia = new(217, 70, 239);
    private static readonly ScottPlot.Color SlateGray = new(100, 116, 139);

    // Dark theme
    private static readonly ScottPlot.Color DarkBg = new(15, 23, 42);
    private static readonly ScottPlot.Color DarkSurface = new(30, 41, 59);
    private static readonly ScottPlot.Color DarkText = new(226, 232, 240);
    private static readonly ScottPlot.Color DarkMuted = new(100, 116, 139);
    private static readonly ScottPlot.Color DarkGrid = new(51, 65, 85);

    // Palette array for multi-series
    private static readonly ScottPlot.Color[] ChartPalette =
        [ElectricBlue, NeonGreen, Amber, Rose, Violet, Cyan, Coral, Teal, SkyBlue, Lime, Fuchsia];

    public PlotService(ILogger<PlotService> logger)
    {
        _logger = logger;
    }

    // ==================== Premium Style Helpers ====================

    private static void ApplyDarkTheme(Plot plt)
    {
        plt.FigureBackground.Color = DarkBg;
        plt.DataBackground.Color = DarkSurface;
        plt.Axes.Bottom.TickLabelStyle.ForeColor = DarkText;
        plt.Axes.Left.TickLabelStyle.ForeColor = DarkText;
        plt.Axes.Bottom.MajorTickStyle.Color = DarkGrid;
        plt.Axes.Left.MajorTickStyle.Color = DarkGrid;
        plt.Axes.Bottom.MinorTickStyle.Color = DarkGrid;
        plt.Axes.Left.MinorTickStyle.Color = DarkGrid;
        plt.Axes.Bottom.FrameLineStyle.Color = DarkGrid;
        plt.Axes.Left.FrameLineStyle.Color = DarkGrid;
        plt.Axes.Top.FrameLineStyle.Color = DarkGrid;
        plt.Axes.Right.FrameLineStyle.Color = DarkGrid;
        plt.Grid.MajorLineColor = DarkGrid;
    }

    private static void SetTitle(Plot plt, string title)
    {
        plt.Title(title);
        plt.Axes.Title.Label.ForeColor = DarkText;
        plt.Axes.Title.Label.FontSize = 22;
        plt.Axes.Title.Label.Bold = true;
    }

    private static void SetAxisLabels(Plot plt, string xLabel, string yLabel)
    {
        plt.XLabel(xLabel);
        plt.YLabel(yLabel);
        plt.Axes.Bottom.Label.ForeColor = DarkMuted;
        plt.Axes.Left.Label.ForeColor = DarkMuted;
        plt.Axes.Bottom.Label.FontSize = 14;
        plt.Axes.Left.Label.FontSize = 14;
    }

    private static void AddInfoBox(Plot plt, string text, Alignment position = Alignment.UpperRight)
    {
        var ann = plt.Add.Annotation(text);
        ann.LabelFontSize = 13;
        ann.LabelFontColor = DarkText;
        ann.LabelBackgroundColor = new ScottPlot.Color(30, 41, 59, 230);
        ann.LabelBorderColor = ElectricBlue;
        ann.LabelBorderWidth = 1.5f;
        ann.Alignment = position;
    }

    private static void AddFooter(Plot plt, int days, string? extra = null)
    {
        var now = DateTime.UtcNow;
        var footer = $"GitHub Activity Monitor  ·  {PeriodLabel(days)}  ·  Generated {now:yyyy-MM-dd HH:mm} UTC" +
                     (extra is not null ? $"  ·  {extra}" : "");
        var ts = plt.Add.Annotation(footer);
        ts.LabelFontSize = 10;
        ts.LabelFontColor = DarkMuted;
        ts.LabelBackgroundColor = ScottPlot.Color.FromHex("#00000000");
        ts.LabelBorderWidth = 0;
        ts.Alignment = Alignment.LowerRight;
    }

    private static void SetXTicks(Plot plt, double[] positions, string[] labels, int maxTicks = 18)
    {
        int step = Math.Max(1, positions.Length / maxTicks);
        var ticks = new List<Tick>();
        for (int i = 0; i < positions.Length; i += step)
            ticks.Add(new Tick(positions[i], labels[i]));
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
        plt.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plt.Axes.Bottom.TickLabelStyle.ForeColor = DarkMuted;
    }

    // ====================================================================
    //  1. ACTIVITY BAR CHART
    // ====================================================================

    public byte[] GenerateActivityChart(DateOnly[] dates, int[] totals, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4ca  Daily Contributions  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Date", "Total Contributions");

        if (dates.Length == 0)
        {
            AddInfoBox(plt, "No contribution data\navailable for this period.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] positions = new double[dates.Length];
        string[] labels = new string[dates.Length];
        double[] values = new double[totals.Length];

        double max = totals.Max();
        for (int i = 0; i < dates.Length; i++)
        {
            positions[i] = i;
            labels[i] = dates[i].ToString("MM/dd");
            values[i] = totals[i];
        }

        var bars = plt.Add.Bars(positions, values);
        for (int i = 0; i < bars.Bars.Count; i++)
        {
            double ratio = max > 0 ? values[i] / max : 0;
            bars.Bars[i].FillColor = ratio switch
            {
                >= 0.8 => NeonGreen,
                >= 0.5 => ElectricBlue,
                >= 0.2 => SkyBlue,
                > 0 => SlateGray,
                _ => new ScottPlot.Color(51, 65, 85)
            };
            bars.Bars[i].LineWidth = 0;
        }

        double threshold = max * 0.5;
        for (int i = 0; i < positions.Length; i++)
        {
            if (values[i] >= threshold && values[i] > 0)
            {
                var txt = plt.Add.Text(totals[i].ToString("N0"), positions[i], values[i]);
                txt.LabelFontSize = 10;
                txt.LabelFontColor = NeonGreen;
                txt.LabelAlignment = Alignment.LowerCenter;
                txt.LabelBold = true;
            }
        }

        int total = totals.Sum();
        double avg = totals.Average();
        int peak = totals.Max();
        int peakIdx = Array.IndexOf(totals, peak);
        string peakDate = peakIdx >= 0 ? dates[peakIdx].ToString("MMM dd") : "N/A";
        int zeroDays = totals.Count(t => t == 0);
        double stdDev = Math.Sqrt(totals.Average(v => Math.Pow(v - avg, 2)));

        AddInfoBox(plt,
            $"  \u25cf Total: {total:N0}\n" +
            $"  \u25cf Daily Avg: {avg:F1}\n" +
            $"  \u25cf Peak: {peak:N0} ({peakDate})\n" +
            $"  \u25cf Std Dev: {stdDev:F1}\n" +
            $"  \u25cf Zero Days: {zeroDays}/{dates.Length}");

        SetXTicks(plt, positions, labels);
        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  2. DISTRIBUTION HISTOGRAM
    // ====================================================================

    public byte[] GenerateDistributionHistogram(int[] studentTotals, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4ca  Student Contribution Distribution  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Contribution Range", "Number of Students");

        if (studentTotals.Length == 0)
        {
            AddInfoBox(plt, "No students to display.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        var binLabels = new[] { "0\n(Inactive)", "1-5\n(Minimal)", "6-10\n(Low)", "11-20\n(Moderate)", "21-50\n(Good)", "51-100\n(Strong)", "100+\n(Excellent)" };
        var binCounts = new double[7];
        ScottPlot.Color[] binColors = [Rose, Amber, Amber, SlateGray, ElectricBlue, NeonGreen, Cyan];

        foreach (var t in studentTotals)
        {
            if (t == 0) binCounts[0]++;
            else if (t <= 5) binCounts[1]++;
            else if (t <= 10) binCounts[2]++;
            else if (t <= 20) binCounts[3]++;
            else if (t <= 50) binCounts[4]++;
            else if (t <= 100) binCounts[5]++;
            else binCounts[6]++;
        }

        double[] positions = Enumerable.Range(0, 7).Select(i => (double)i).ToArray();
        var bars = plt.Add.Bars(positions, binCounts);

        for (int i = 0; i < bars.Bars.Count && i < binColors.Length; i++)
        {
            bars.Bars[i].FillColor = binColors[i];
            bars.Bars[i].LineWidth = 0;
        }

        int totalStudents = studentTotals.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            if (binCounts[i] > 0)
            {
                double pct = binCounts[i] / totalStudents * 100;
                var txt = plt.Add.Text($"{(int)binCounts[i]} ({pct:F0}%)", positions[i], binCounts[i]);
                txt.LabelFontSize = 11;
                txt.LabelFontColor = binColors[i];
                txt.LabelAlignment = Alignment.LowerCenter;
                txt.LabelBold = true;
            }
        }

        var ticks = positions.Select((p, i) => new Tick(p, binLabels[i])).ToArray();
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);

        double avg = studentTotals.Average();
        double median = GetMedian(studentTotals);
        int maxC = studentTotals.Max();
        int zeroCount = studentTotals.Count(s => s == 0);
        double inactiveRate = (double)zeroCount / totalStudents * 100;

        AddInfoBox(plt,
            $"  \u25cf Students: {totalStudents:N0}\n" +
            $"  \u25cf Average: {avg:F1}\n" +
            $"  \u25cf Median: {median:F0}\n" +
            $"  \u25cf Max: {maxC:N0}\n" +
            $"  \u25cf Inactive: {zeroCount} ({inactiveRate:F1}%)");

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  3. TREND LINE CHART
    // ====================================================================

    public byte[] GenerateTrendLineChart(DateOnly[] dates, int[] totals, int[] activeStudents, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4c8  Usage Trend  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Date", "Total Contributions");

        if (dates.Length == 0)
        {
            AddInfoBox(plt, "No trend data available.");
            AddFooter(plt, days);
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
        line1.Color = ElectricBlue;
        line1.LineWidth = 3f;
        line1.MarkerSize = 5;
        line1.MarkerColor = ElectricBlue;
        line1.LegendText = "Contributions";

        var line2 = plt.Add.Scatter(xs, yActive);
        line2.Color = NeonGreen;
        line2.LineWidth = 3f;
        line2.MarkerSize = 5;
        line2.MarkerColor = NeonGreen;
        line2.LinePattern = LinePattern.Dashed;
        line2.LegendText = "Active Students";
        line2.Axes.YAxis = plt.Axes.Right;

        plt.Axes.Right.Label.Text = "Active Students";
        plt.Axes.Right.Label.ForeColor = NeonGreen;
        plt.Axes.Right.TickLabelStyle.ForeColor = NeonGreen;
        plt.ShowLegend();
        plt.Legend.FontColor = DarkText;

        if (totals.Length > 0)
        {
            int peakIdx = Array.IndexOf(totals, totals.Max());
            var peakMark = plt.Add.Text($"\u25b2 {totals[peakIdx]:N0}\n{dates[peakIdx]:MMM dd}", xs[peakIdx], yTotals[peakIdx]);
            peakMark.LabelFontSize = 11;
            peakMark.LabelFontColor = Cyan;
            peakMark.LabelBold = true;
            peakMark.LabelAlignment = Alignment.LowerCenter;
        }

        string trend = totals.Length >= 2
            ? (totals[^1] > totals[0] ? "\u2191 Upward" : totals[^1] < totals[0] ? "\u2193 Downward" : "\u2192 Flat")
            : "N/A";

        AddInfoBox(plt,
            $"  \u25cf Trend: {trend}\n" +
            $"  \u25cf Total: {totals.Sum():N0}\n" +
            $"  \u25cf Avg/Day: {totals.Average():F1}\n" +
            $"  \u25cf Avg Active: {activeStudents.Average():F1}\n" +
            $"  \u25cf Data Points: {dates.Length}");

        SetXTicks(plt, xs, labels);
        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  4. PRO DONUT CHART
    // ====================================================================

    public byte[] GenerateProPieChart(int active, int inactive, int pendingRemoval, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        int total = active + inactive + pendingRemoval;
        SetTitle(plt, $"\U0001f369  License Status  \u00b7  {total:N0} Students");

        var slices = new List<PieSlice>();
        double activePct = total > 0 ? (double)active / total * 100 : 0;
        double inactivePct = total > 0 ? (double)inactive / total * 100 : 0;
        double pendingPct = total > 0 ? (double)pendingRemoval / total * 100 : 0;

        if (active > 0) slices.Add(new PieSlice { Value = active, Label = $"Active: {active} ({activePct:F1}%)", FillColor = NeonGreen });
        if (inactive > 0) slices.Add(new PieSlice { Value = inactive, Label = $"Inactive: {inactive} ({inactivePct:F1}%)", FillColor = Amber });
        if (pendingRemoval > 0) slices.Add(new PieSlice { Value = pendingRemoval, Label = $"Pending: {pendingRemoval} ({pendingPct:F1}%)", FillColor = Rose });
        if (slices.Count == 0) slices.Add(new PieSlice { Value = 1, Label = "No Data", FillColor = SlateGray });

        var pie = plt.Add.Pie(slices);
        pie.DonutFraction = 0.55;
        pie.ExplodeFraction = 0.03;

        plt.ShowLegend();
        plt.Legend.FontColor = DarkText;
        plt.Axes.Frameless();

        double utilization = total > 0 ? activePct : 0;
        string riskLevel = utilization >= 80 ? "\U0001f7e2 Healthy" :
                           utilization >= 60 ? "\U0001f7e1 Moderate" :
                           utilization >= 40 ? "\U0001f7e0 Concerning" : "\U0001f534 Critical";

        AddInfoBox(plt,
            $"  \u25cf Utilization: {utilization:F1}%\n" +
            $"  \u25cf Risk: {riskLevel}\n" +
            $"  \u25cf Removal Queue: {pendingRemoval}\n" +
            $"  \u25cf Total: {total:N0}", Alignment.LowerLeft);

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  5. STACKED BAR CHART — Clustered bars by status
    // ====================================================================

    public byte[] GenerateStackedBarChart(DateOnly[] dates, int[] activeTotals, int[] inactiveTotals, int[] pendingTotals, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4ca  Contributions by Status  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Date", "Contributions");

        if (dates.Length == 0)
        {
            AddInfoBox(plt, "No data available.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] positions = new double[dates.Length];
        string[] labels = new string[dates.Length];
        for (int i = 0; i < dates.Length; i++)
        {
            positions[i] = i;
            labels[i] = dates[i].ToString("MM/dd");
        }

        double barWidth = 0.25;

        var barsActive = plt.Add.Bars(positions.Select(p => p - barWidth).ToArray(), activeTotals.Select(v => (double)v).ToArray());
        barsActive.Color = NeonGreen;
        barsActive.LegendText = "Active";
        for (int i = 0; i < barsActive.Bars.Count; i++) { barsActive.Bars[i].LineWidth = 0; barsActive.Bars[i].Size = barWidth * 2; }

        var barsInactive = plt.Add.Bars(positions, inactiveTotals.Select(v => (double)v).ToArray());
        barsInactive.Color = Amber;
        barsInactive.LegendText = "Inactive";
        for (int i = 0; i < barsInactive.Bars.Count; i++) { barsInactive.Bars[i].LineWidth = 0; barsInactive.Bars[i].Size = barWidth * 2; }

        var barsPending = plt.Add.Bars(positions.Select(p => p + barWidth).ToArray(), pendingTotals.Select(v => (double)v).ToArray());
        barsPending.Color = Rose;
        barsPending.LegendText = "Pending Removal";
        for (int i = 0; i < barsPending.Bars.Count; i++) { barsPending.Bars[i].LineWidth = 0; barsPending.Bars[i].Size = barWidth * 2; }

        plt.ShowLegend();
        plt.Legend.FontColor = DarkText;

        int totalActive = activeTotals.Sum();
        int totalInactive = inactiveTotals.Sum();
        int totalPending = pendingTotals.Sum();
        int grandTotal = totalActive + totalInactive + totalPending;

        AddInfoBox(plt,
            $"  \u25cf Active: {totalActive:N0} ({(grandTotal > 0 ? (double)totalActive / grandTotal * 100 : 0):F1}%)\n" +
            $"  \u25cf Inactive: {totalInactive:N0} ({(grandTotal > 0 ? (double)totalInactive / grandTotal * 100 : 0):F1}%)\n" +
            $"  \u25cf Pending: {totalPending:N0} ({(grandTotal > 0 ? (double)totalPending / grandTotal * 100 : 0):F1}%)\n" +
            $"  \u25cf Grand Total: {grandTotal:N0}");

        SetXTicks(plt, positions, labels);
        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  6. AREA CHART — Cumulative contributions
    // ====================================================================

    public byte[] GenerateAreaChart(DateOnly[] dates, int[] totals, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4c8  Cumulative Activity  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Date", "Cumulative Contributions");

        if (dates.Length == 0)
        {
            AddInfoBox(plt, "No data available.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] xs = new double[dates.Length];
        string[] labels = new string[dates.Length];
        double[] cumulative = new double[totals.Length];
        double runningSum = 0;

        for (int i = 0; i < dates.Length; i++)
        {
            xs[i] = i;
            labels[i] = dates[i].ToString("MM/dd");
            runningSum += totals[i];
            cumulative[i] = runningSum;
        }

        var scatter = plt.Add.Scatter(xs, cumulative);
        scatter.Color = ElectricBlue;
        scatter.LineWidth = 3f;
        scatter.MarkerSize = 0;
        scatter.FillY = true;
        scatter.FillYColor = new ScottPlot.Color(59, 130, 246, 60);
        scatter.FillYValue = 0;
        scatter.LegendText = "Cumulative";

        // Milestone lines
        double finalTotal = cumulative.Length > 0 ? cumulative[^1] : 0;
        foreach (double pct in new[] { 0.25, 0.50, 0.75 })
        {
            double target = finalTotal * pct;
            var hl = plt.Add.HorizontalLine(target);
            hl.Color = new ScottPlot.Color(100, 116, 139, 80);
            hl.LinePattern = LinePattern.Dotted;
            hl.LineWidth = 1;
        }

        AddInfoBox(plt,
            $"  \u25cf Total: {finalTotal:N0}\n" +
            $"  \u25cf Daily Avg: {totals.Average():F1}\n" +
            $"  \u25cf Growth Rate: {(totals.Length > 1 ? (cumulative[^1] - cumulative[0]) / totals.Length : 0):F1}/day\n" +
            $"  \u25cf Peak Day: {totals.Max():N0}");

        SetXTicks(plt, xs, labels);
        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  7. HEATMAP — GitHub-style activity grid
    // ====================================================================

    public byte[] GenerateHeatmap(DateOnly[] dates, int[] contributions, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f525  Activity Heatmap  \u00b7  {PeriodLabel(days)}");

        if (dates.Length == 0)
        {
            AddInfoBox(plt, "No data for heatmap.");
            AddFooter(plt, days);
            return plt.GetImageBytes(WideWidth, Height, ImageFormat.Png);
        }

        var startDate = dates.Min();
        var endDate = dates.Max();
        var lookup = new Dictionary<DateOnly, int>();
        for (int i = 0; i < dates.Length; i++)
            lookup[dates[i]] = contributions[i];

        int totalDaySpan = endDate.DayNumber - startDate.DayNumber + 1;
        int numWeeks = (totalDaySpan + 6) / 7;
        if (numWeeks == 0) numWeeks = 1;

        double maxContrib = contributions.Max();
        if (maxContrib == 0) maxContrib = 1;

        var dayNames = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        for (int day = 0; day < totalDaySpan; day++)
        {
            var currentDate = startDate.AddDays(day);
            int dayOfWeek = ((int)currentDate.ToDateTime(TimeOnly.MinValue).DayOfWeek + 6) % 7;
            int weekIdx = day / 7;
            int count = lookup.GetValueOrDefault(currentDate, 0);
            double intensity = count / maxContrib;

            ScottPlot.Color cellColor = intensity switch
            {
                0 => new ScottPlot.Color(30, 41, 59),
                < 0.25 => new ScottPlot.Color(6, 78, 59),
                < 0.5 => new ScottPlot.Color(4, 120, 87),
                < 0.75 => new ScottPlot.Color(16, 185, 129),
                _ => new ScottPlot.Color(52, 211, 153)
            };

            double x1 = weekIdx;
            double x2 = weekIdx + 0.9;
            double y1 = 6 - dayOfWeek;
            double y2 = y1 + 0.9;

            var rect = plt.Add.Rectangle(x1, x2, y1, y2);
            rect.FillColor = cellColor;
            rect.LineColor = DarkBg;
            rect.LineWidth = 2;
        }

        var yTicks = Enumerable.Range(0, 7).Select(i => new Tick(6 - i + 0.45, dayNames[i])).ToArray();
        plt.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(yTicks);

        if (numWeeks > 0)
        {
            int xStep = Math.Max(1, numWeeks / 12);
            var xTicks = new List<Tick>();
            for (int w = 0; w < numWeeks; w += xStep)
            {
                var weekStart = startDate.AddDays(w * 7);
                xTicks.Add(new Tick(w + 0.45, weekStart.ToString("MM/dd")));
            }
            plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(xTicks.ToArray());
        }

        plt.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plt.Axes.Bottom.TickLabelStyle.ForeColor = DarkMuted;
        plt.Axes.Left.TickLabelStyle.ForeColor = DarkMuted;

        int totalContribs = contributions.Sum();
        int activeDays = contributions.Count(c => c > 0);
        int streakMax = GetMaxStreak(dates, contributions);

        AddInfoBox(plt,
            $"  \u25cf Total: {totalContribs:N0}\n" +
            $"  \u25cf Active Days: {activeDays}/{dates.Length}\n" +
            $"  \u25cf Max Streak: {streakMax} days\n" +
            $"  \u25cf Avg/Active Day: {(activeDays > 0 ? (double)totalContribs / activeDays : 0):F1}\n" +
            $"  \u25cf Peak: {contributions.Max():N0}");

        AddFooter(plt, days);
        return plt.GetImageBytes(WideWidth, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  8. SCATTER / BUBBLE — Students: contributions vs active days
    // ====================================================================

    public byte[] GenerateScatterBubble(string[] usernames, int[] totalContributions, int[] activeDays, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f535  Student Activity Map  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Active Days", "Total Contributions");

        if (usernames.Length == 0)
        {
            AddInfoBox(plt, "No student data available.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] xs = activeDays.Select(d => (double)d).ToArray();
        double[] ys = totalContributions.Select(c => (double)c).ToArray();

        var scatter = plt.Add.Scatter(xs, ys);
        scatter.Color = new ScottPlot.Color(59, 130, 246, 150);
        scatter.LineWidth = 0;
        scatter.MarkerSize = 10;
        scatter.MarkerColor = new ScottPlot.Color(59, 130, 246, 150);

        // Highlight top 3
        var ranked = Enumerable.Range(0, usernames.Length)
            .OrderByDescending(i => totalContributions[i])
            .Take(3).ToList();

        foreach (var idx in ranked)
        {
            var label = plt.Add.Text($" {usernames[idx]}", xs[idx], ys[idx]);
            label.LabelFontSize = 10;
            label.LabelFontColor = NeonGreen;
            label.LabelBold = true;
            label.LabelAlignment = Alignment.MiddleLeft;
        }

        double avgX = xs.Average();
        double avgY = ys.Average();
        var hLine = plt.Add.HorizontalLine(avgY);
        hLine.Color = new ScottPlot.Color(245, 158, 11, 100);
        hLine.LinePattern = LinePattern.Dashed;
        hLine.LineWidth = 1;
        var vLine = plt.Add.VerticalLine(avgX);
        vLine.Color = new ScottPlot.Color(245, 158, 11, 100);
        vLine.LinePattern = LinePattern.Dashed;
        vLine.LineWidth = 1;

        int q1 = 0, q3 = 0;
        for (int i = 0; i < xs.Length; i++)
        {
            if (xs[i] >= avgX && ys[i] >= avgY) q1++;
            else if (xs[i] < avgX && ys[i] < avgY) q3++;
        }

        AddInfoBox(plt,
            $"  \u25cf Students: {usernames.Length:N0}\n" +
            $"  \u25cf Avg Contributions: {avgY:F1}\n" +
            $"  \u25cf Avg Active Days: {avgX:F1}\n" +
            $"  \u25cf \u2b50 Stars (high/high): {q1}\n" +
            $"  \u25cf \u26a0\ufe0f At Risk (low/low): {q3}");

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  9. GAUGE / KPI — License utilization
    // ====================================================================

    public byte[] GenerateGaugeChart(double utilizationPct, int active, int total, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, "\u26a1  License Utilization KPI");

        double fillPct = Math.Clamp(utilizationPct, 0, 100);
        double emptyPct = 100 - fillPct;

        ScottPlot.Color gaugeColor = fillPct >= 80 ? NeonGreen :
                                      fillPct >= 60 ? ElectricBlue :
                                      fillPct >= 40 ? Amber : Rose;

        var slices = new List<PieSlice>
        {
            new() { Value = fillPct, Label = "", FillColor = gaugeColor },
            new() { Value = emptyPct, Label = "", FillColor = new ScottPlot.Color(51, 65, 85) }
        };

        var pie = plt.Add.Pie(slices);
        pie.DonutFraction = 0.65;
        plt.Axes.Frameless();

        var centerText = plt.Add.Text($"{fillPct:F1}%", 0, 0);
        centerText.LabelFontSize = 48;
        centerText.LabelFontColor = gaugeColor;
        centerText.LabelBold = true;
        centerText.LabelAlignment = Alignment.MiddleCenter;

        var subText = plt.Add.Text("Utilization Rate", 0, -0.3);
        subText.LabelFontSize = 16;
        subText.LabelFontColor = DarkMuted;
        subText.LabelAlignment = Alignment.MiddleCenter;

        string riskLevel = fillPct >= 80 ? "\U0001f7e2 Healthy" :
                           fillPct >= 60 ? "\U0001f7e1 Moderate Risk" :
                           fillPct >= 40 ? "\U0001f7e0 High Risk" : "\U0001f534 Critical";

        int inactive = total - active;

        AddInfoBox(plt,
            $"  \u25cf Active: {active:N0}\n" +
            $"  \u25cf Inactive: {inactive:N0}\n" +
            $"  \u25cf Total: {total:N0}\n" +
            $"  \u25cf Status: {riskLevel}\n" +
            $"  \u25cf Generated: {DateTime.UtcNow:MMM dd, yyyy}", Alignment.LowerLeft);

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  10. WATERFALL — Period-over-period changes
    // ====================================================================

    public byte[] GenerateWaterfallChart(string[] periodLabels, int[] values, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4a7  Period-over-Period Changes  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Period", "Contributions");

        if (periodLabels.Length == 0)
        {
            AddInfoBox(plt, "Insufficient data for waterfall.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] positions = Enumerable.Range(0, periodLabels.Length).Select(i => (double)i).ToArray();
        var isPositive = new bool[values.Length];
        isPositive[0] = values[0] >= 0;
        for (int i = 1; i < values.Length; i++)
            isPositive[i] = values[i] >= values[i - 1];

        double[] heights = values.Select(v => (double)v).ToArray();
        var bars = plt.Add.Bars(positions, heights);

        for (int i = 0; i < bars.Bars.Count; i++)
        {
            bars.Bars[i].FillColor = isPositive[i] ? NeonGreen : Rose;
            bars.Bars[i].LineWidth = 0;

            string label = i == 0 ? values[i].ToString("N0") :
                $"{(values[i] - values[i - 1] >= 0 ? "+" : "")}{values[i] - values[i - 1]:N0}";
            var txt = plt.Add.Text(label, positions[i], heights[i]);
            txt.LabelFontSize = 10;
            txt.LabelFontColor = isPositive[i] ? NeonGreen : Rose;
            txt.LabelAlignment = Alignment.LowerCenter;
            txt.LabelBold = true;
        }

        var ticks = positions.Select((p, i) => new Tick(p, periodLabels[i])).ToArray();
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);

        int netChange = values.Length >= 2 ? values[^1] - values[0] : 0;
        string direction = netChange > 0 ? "\U0001f4c8 Growing" : netChange < 0 ? "\U0001f4c9 Declining" : "\u27a1\ufe0f Stable";

        AddInfoBox(plt,
            $"  \u25cf Net Change: {(netChange >= 0 ? "+" : "")}{netChange:N0}\n" +
            $"  \u25cf Direction: {direction}\n" +
            $"  \u25cf Periods: {periodLabels.Length}\n" +
            $"  \u25cf Start: {values[0]:N0} \u2192 End: {values[^1]:N0}");

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  11. FUNNEL — Student engagement funnel
    // ====================================================================

    public byte[] GenerateFunnelChart(int totalStudents, int activeStudents, int weeklyActive, int dailyActive, int topContributors, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, "\U0001f53b  Student Engagement Funnel");

        var funnelLabels = new[] { "Total Students", "Active (30d)", "Weekly Active", "Daily Active", "Top Contributors" };
        var funnelValues = new[] { totalStudents, activeStudents, weeklyActive, dailyActive, topContributors };
        ScottPlot.Color[] funnelColors = [ElectricBlue, Cyan, NeonGreen, Amber, Rose];

        double maxVal = funnelValues.Max();
        if (maxVal == 0) maxVal = 1;

        for (int i = 0; i < funnelValues.Length; i++)
        {
            double barWidth = Math.Max(0.3, (double)funnelValues[i] / maxVal * 10);
            double y = funnelValues.Length - 1 - i;

            var rect = plt.Add.Rectangle(-barWidth / 2, barWidth / 2, y - 0.35, y + 0.35);
            rect.FillColor = funnelColors[i];
            rect.LineColor = DarkSurface;
            rect.LineWidth = 2;

            double pct = totalStudents > 0 ? (double)funnelValues[i] / totalStudents * 100 : 0;
            var label = plt.Add.Text($"{funnelLabels[i]}: {funnelValues[i]:N0} ({pct:F1}%)", 0, y);
            label.LabelFontSize = 14;
            label.LabelFontColor = DarkText;
            label.LabelBold = true;
            label.LabelAlignment = Alignment.MiddleCenter;
        }

        plt.Axes.Frameless();

        double convActive = totalStudents > 0 ? (double)activeStudents / totalStudents * 100 : 0;
        double convWeekly = activeStudents > 0 ? (double)weeklyActive / activeStudents * 100 : 0;
        double convDaily = weeklyActive > 0 ? (double)dailyActive / weeklyActive * 100 : 0;

        AddInfoBox(plt,
            $"  \u25cf Total \u2192 Active: {convActive:F1}%\n" +
            $"  \u25cf Active \u2192 Weekly: {convWeekly:F1}%\n" +
            $"  \u25cf Weekly \u2192 Daily: {convDaily:F1}%\n" +
            $"  \u25cf Drop-off: {totalStudents - activeStudents:N0}", Alignment.LowerRight);

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  12. TOP CONTRIBUTORS — Horizontal bar race
    // ====================================================================

    public byte[] GenerateTopChart(string[] usernames, int[] contributions, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f3c6  Top Contributors  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Contributions", "");

        if (usernames.Length == 0)
        {
            AddInfoBox(plt, "No contributor data.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, TallHeight, ImageFormat.Png);
        }

        int count = Math.Min(usernames.Length, 15);
        double[] positions = Enumerable.Range(0, count).Select(i => (double)i).ToArray();
        double[] values = contributions.Take(count).Select(c => (double)c).Reverse().ToArray();
        string[] names = usernames.Take(count).Reverse().ToArray();

        var bars = plt.Add.Bars(positions, values);
        bars.Horizontal = true;

        for (int i = 0; i < bars.Bars.Count; i++)
        {
            int rankFromTop = count - 1 - i;
            bars.Bars[i].FillColor = rankFromTop switch
            {
                0 => new ScottPlot.Color(255, 215, 0),
                1 => new ScottPlot.Color(192, 192, 192),
                2 => new ScottPlot.Color(205, 127, 50),
                _ => ChartPalette[rankFromTop % ChartPalette.Length]
            };
            bars.Bars[i].LineWidth = 0;
        }

        var yTicks = positions.Select((p, i) =>
        {
            string medal = (count - 1 - i) switch
            {
                0 => "\U0001f947 ",
                1 => "\U0001f948 ",
                2 => "\U0001f949 ",
                _ => $"#{count - i} "
            };
            return new Tick(p, $"{medal}{names[i]}");
        }).ToArray();
        plt.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(yTicks);
        plt.Axes.Left.TickLabelStyle.ForeColor = DarkText;

        for (int i = 0; i < count; i++)
        {
            var txt = plt.Add.Text($" {values[i]:N0}", values[i], positions[i]);
            txt.LabelFontSize = 11;
            txt.LabelFontColor = DarkText;
            txt.LabelBold = true;
            txt.LabelAlignment = Alignment.MiddleLeft;
        }

        AddInfoBox(plt,
            $"  \u25cf Showing Top {count}\n" +
            $"  \u25cf Combined: {contributions.Take(count).Sum():N0}\n" +
            $"  \u25cf Average: {contributions.Take(count).Average():F1}\n" +
            $"  \u25cf #1: {usernames[0]} ({contributions[0]:N0})");

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, TallHeight, ImageFormat.Png);
    }

    // ====================================================================
    //  13. WEEKLY COMPARISON — Clustered bars
    // ====================================================================

    public byte[] GenerateWeeklyComparison(string[] weekLabels, int[] weekTotals, int[] weekActiveStudents, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4ca  Weekly Comparison  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Week", "Value");

        if (weekLabels.Length == 0)
        {
            AddInfoBox(plt, "Not enough data.");
            AddFooter(plt, days);
            return plt.GetImageBytes(Width, Height, ImageFormat.Png);
        }

        double[] positions = Enumerable.Range(0, weekLabels.Length).Select(i => (double)i).ToArray();
        double barWidth = 0.35;

        var contribBars = plt.Add.Bars(
            positions.Select(p => p - barWidth / 2).ToArray(),
            weekTotals.Select(v => (double)v).ToArray());
        contribBars.Color = ElectricBlue;
        contribBars.LegendText = "Contributions";
        for (int i = 0; i < contribBars.Bars.Count; i++) { contribBars.Bars[i].Size = barWidth; contribBars.Bars[i].LineWidth = 0; }

        var activeBars = plt.Add.Bars(
            positions.Select(p => p + barWidth / 2).ToArray(),
            weekActiveStudents.Select(v => (double)v).ToArray());
        activeBars.Color = NeonGreen;
        activeBars.LegendText = "Active Students";
        for (int i = 0; i < activeBars.Bars.Count; i++) { activeBars.Bars[i].Size = barWidth; activeBars.Bars[i].LineWidth = 0; }

        plt.ShowLegend();
        plt.Legend.FontColor = DarkText;

        for (int i = 0; i < positions.Length; i++)
        {
            var t1 = plt.Add.Text(weekTotals[i].ToString("N0"), positions[i] - barWidth / 2, weekTotals[i]);
            t1.LabelFontSize = 10; t1.LabelFontColor = ElectricBlue; t1.LabelAlignment = Alignment.LowerCenter;
            var t2 = plt.Add.Text(weekActiveStudents[i].ToString("N0"), positions[i] + barWidth / 2, weekActiveStudents[i]);
            t2.LabelFontSize = 10; t2.LabelFontColor = NeonGreen; t2.LabelAlignment = Alignment.LowerCenter;
        }

        var ticks = positions.Select((p, i) => new Tick(p, weekLabels[i])).ToArray();
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);

        if (weekTotals.Length >= 2)
        {
            int change = weekTotals[^1] - weekTotals[^2];
            string dir = change > 0 ? "\U0001f4c8+" : change < 0 ? "\U0001f4c9" : "\u27a1\ufe0f";
            AddInfoBox(plt,
                $"  \u25cf Latest: {weekTotals[^1]:N0}\n" +
                $"  \u25cf WoW Change: {dir}{change:N0}\n" +
                $"  \u25cf Avg Active: {weekActiveStudents.Average():F1}\n" +
                $"  \u25cf Weeks: {weekLabels.Length}");
        }

        AddFooter(plt, days);
        return plt.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    // ====================================================================
    //  14. DAY-OF-WEEK ANALYSIS
    // ====================================================================

    public byte[] GenerateDayOfWeekChart(int[] dayOfWeekTotals, int[] dayOfWeekCounts, int days)
    {
        var plt = new Plot();
        ApplyDarkTheme(plt);
        SetTitle(plt, $"\U0001f4c5  Day-of-Week Patterns  \u00b7  {PeriodLabel(days)}");
        SetAxisLabels(plt, "Day", "Total Contributions");

        var dayNames = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        double[] positions = Enumerable.Range(0, 7).Select(i => (double)i).ToArray();
        double[] values = dayOfWeekTotals.Select(v => (double)v).ToArray();

        var bars = plt.Add.Bars(positions, values);
        double maxDay = values.Max() > 0 ? values.Max() : 1;
        for (int i = 0; i < bars.Bars.Count; i++)
        {
            double ratio = values[i] / maxDay;
            bars.Bars[i].FillColor = ratio >= 0.8 ? NeonGreen : ratio >= 0.5 ? ElectricBlue : ratio >= 0.2 ? SkyBlue : SlateGray;
            bars.Bars[i].LineWidth = 0;
        }

        for (int i = 0; i < 7; i++)
        {
            if (values[i] > 0)
            {
                var txt = plt.Add.Text(dayOfWeekTotals[i].ToString("N0"), positions[i], values[i]);
                txt.LabelFontSize = 12; txt.LabelFontColor = DarkText;
                txt.LabelAlignment = Alignment.LowerCenter; txt.LabelBold = true;
            }
        }

        var ticks = positions.Select((p, i) => new Tick(p, dayNames[i])).ToArray();
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);

        int bestDay = Array.IndexOf(dayOfWeekTotals, dayOfWeekTotals.Max());
        int worstDay = Array.IndexOf(dayOfWeekTotals, dayOfWeekTotals.Min());
        double weekdayAvg = dayOfWeekTotals.Take(5).Average();
        double weekendAvg = dayOfWeekTotals.Skip(5).Take(2).Average();

        AddInfoBox(plt,
            $"  \u25cf Best: {dayNames[bestDay]}\n" +
            $"  \u25cf Quietest: {dayNames[worstDay]}\n" +
            $"  \u25cf Weekday Avg: {weekdayAvg:F0}\n" +
            $"  \u25cf Weekend Avg: {weekendAvg:F0}\n" +
            $"  \u25cf Ratio: {(weekendAvg > 0 ? weekdayAvg / weekendAvg : 0):F1}x");

        AddFooter(plt, days);
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

                // ── Activity chart ──
                var activityData = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.Date)
                    .Select(g => new { Date = g.Key, Total = g.Sum(x => x.Count) })
                    .OrderBy(x => x.Date)
                    .ToListAsync(ct);

                var actDates = activityData.Select(d => d.Date).ToArray();
                var actTotals = activityData.Select(d => d.Total).ToArray();
                _snapshotCache[$"activity_{days}d"] = GenerateActivityChart(actDates, actTotals, days);

                // ── Distribution ──
                var studentSums = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.StudentId)
                    .Select(g => g.Sum(x => x.Count))
                    .ToListAsync(ct);

                var activeStudentCount = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .Select(dc => dc.StudentId).Distinct().CountAsync(ct);

                var totalStudentCount = await db.Students.CountAsync(ct);
                var zeroCount = totalStudentCount - activeStudentCount;
                for (int i = 0; i < zeroCount; i++) studentSums.Add(0);

                _snapshotCache[$"dist_{days}d"] = GenerateDistributionHistogram(studentSums.ToArray(), days);

                // ── Trend ──
                var trendData = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Total = g.Sum(x => x.Count),
                        ActiveStudents = g.Select(x => x.StudentId).Distinct().Count()
                    })
                    .OrderBy(x => x.Date).ToListAsync(ct);

                _snapshotCache[$"trend_{days}d"] = GenerateTrendLineChart(
                    trendData.Select(d => d.Date).ToArray(),
                    trendData.Select(d => d.Total).ToArray(),
                    trendData.Select(d => d.ActiveStudents).ToArray(), days);

                // ── Pro donut ──
                var activeCountDb = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
                var inactiveCountDb = await db.Students.CountAsync(s => s.Status == StudentStatus.Inactive, ct);
                var pendingCountDb = await db.Students.CountAsync(s => s.Status == StudentStatus.Pending_Removal, ct);
                _snapshotCache[$"pro_{days}d"] = GenerateProPieChart(activeCountDb, inactiveCountDb, pendingCountDb, days);

                // ── Area chart ──
                _snapshotCache[$"area_{days}d"] = GenerateAreaChart(actDates, actTotals, days);

                // ── Heatmap ──
                _snapshotCache[$"heatmap_{days}d"] = GenerateHeatmap(actDates, actTotals, days);

                // ── Gauge KPI ──
                var totalForGauge = await db.Students.CountAsync(ct);
                double utilPct = totalForGauge > 0 ? (double)activeCountDb / totalForGauge * 100 : 0;
                _snapshotCache[$"gauge_{days}d"] = GenerateGaugeChart(utilPct, activeCountDb, totalForGauge, days);

                // ── Stacked bar ──
                var activeIds = await db.Students.Where(s => s.Status == StudentStatus.Active).Select(s => s.Id).ToListAsync(ct);
                var inactiveIds = await db.Students.Where(s => s.Status == StudentStatus.Inactive).Select(s => s.Id).ToListAsync(ct);
                var pendingIds = await db.Students.Where(s => s.Status == StudentStatus.Pending_Removal).Select(s => s.Id).ToListAsync(ct);

                var stackedActive = new List<int>();
                var stackedInactive = new List<int>();
                var stackedPending = new List<int>();

                foreach (var entry in activityData)
                {
                    var dayContribs = await db.DailyContributions
                        .Where(dc => dc.Date == entry.Date)
                        .GroupBy(dc => dc.StudentId)
                        .Select(g => new { StudentId = g.Key, Total = g.Sum(x => x.Count) })
                        .ToListAsync(ct);

                    stackedActive.Add(dayContribs.Where(c => activeIds.Contains(c.StudentId)).Sum(c => c.Total));
                    stackedInactive.Add(dayContribs.Where(c => inactiveIds.Contains(c.StudentId)).Sum(c => c.Total));
                    stackedPending.Add(dayContribs.Where(c => pendingIds.Contains(c.StudentId)).Sum(c => c.Total));
                }

                _snapshotCache[$"stacked_{days}d"] = GenerateStackedBarChart(actDates, stackedActive.ToArray(), stackedInactive.ToArray(), stackedPending.ToArray(), days);

                // ── Scatter ──
                var scatterData = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.StudentId)
                    .Select(g => new { StudentId = g.Key, TotalContribs = g.Sum(x => x.Count), ActiveDays = g.Count(x => x.Count > 0) })
                    .ToListAsync(ct);

                var scatterStudentIds = scatterData.Select(s => s.StudentId).ToList();
                var scatterStudents = await db.Students.Where(s => scatterStudentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

                _snapshotCache[$"scatter_{days}d"] = GenerateScatterBubble(
                    scatterData.Select(s => scatterStudents.TryGetValue(s.StudentId, out var stu) ? stu.GithubUsername : "?").ToArray(),
                    scatterData.Select(s => s.TotalContribs).ToArray(),
                    scatterData.Select(s => s.ActiveDays).ToArray(), days);

                // ── Waterfall ──
                if (days >= 7 && activityData.Count >= 4)
                {
                    int chunkSize = Math.Max(1, activityData.Count / Math.Min(activityData.Count, 8));
                    var chunks = activityData.Select((d, idx) => new { d, idx })
                        .GroupBy(x => x.idx / chunkSize)
                        .Select(g => new { Label = g.First().d.Date.ToString("MM/dd"), Total = g.Sum(x => x.d.Total) })
                        .ToList();
                    _snapshotCache[$"waterfall_{days}d"] = GenerateWaterfallChart(
                        chunks.Select(c => c.Label).ToArray(), chunks.Select(c => c.Total).ToArray(), days);
                }

                // ── Funnel ──
                var weeklyActiveCount = await db.DailyContributions
                    .Where(dc => dc.Date >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)))
                    .Select(dc => dc.StudentId).Distinct().CountAsync(ct);
                var dailyActiveCount = await db.DailyContributions
                    .Where(dc => dc.Date == DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)) && dc.Count > 0)
                    .Select(dc => dc.StudentId).Distinct().CountAsync(ct);
                var topContribCount = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.StudentId)
                    .Where(g => g.Sum(x => x.Count) > 50).CountAsync(ct);

                _snapshotCache[$"funnel_{days}d"] = GenerateFunnelChart(totalForGauge, activeCountDb, weeklyActiveCount, dailyActiveCount, topContribCount, days);

                // ── Top chart ──
                var topData = await db.DailyContributions
                    .Where(dc => dc.Date >= since)
                    .GroupBy(dc => dc.StudentId)
                    .Select(g => new { StudentId = g.Key, Total = g.Sum(x => x.Count) })
                    .OrderByDescending(x => x.Total).Take(15).ToListAsync(ct);

                if (topData.Count > 0)
                {
                    var topStudentIds = topData.Select(t => t.StudentId).ToList();
                    var topStudents = await db.Students.Where(s => topStudentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
                    _snapshotCache[$"top_{days}d"] = GenerateTopChart(
                        topData.Select(t => topStudents.TryGetValue(t.StudentId, out var s) ? s.GithubUsername : "?").ToArray(),
                        topData.Select(t => t.Total).ToArray(), days);
                }

                // ── Weekly comparison ──
                if (days >= 14 && activityData.Count >= 7)
                {
                    var weeklyData = activityData.Select((d, idx) => new { d, Week = idx / 7 })
                        .GroupBy(x => x.Week)
                        .Select(g => new { Label = $"W{g.Key + 1}", Total = g.Sum(x => x.d.Total), Days = g.Count() })
                        .ToList();

                    _snapshotCache[$"weekly_{days}d"] = GenerateWeeklyComparison(
                        weeklyData.Select(w => w.Label).ToArray(),
                        weeklyData.Select(w => w.Total).ToArray(),
                        weeklyData.Select(w => w.Days).ToArray(), days);
                }

                // ── Day of week ──
                var dowData = await db.DailyContributions.Where(dc => dc.Date >= since).ToListAsync(ct);
                var dayOfWeekTotals = new int[7];
                var dayOfWeekCounts = new int[7];
                foreach (var dc in dowData)
                {
                    int dow = ((int)dc.Date.ToDateTime(TimeOnly.MinValue).DayOfWeek + 6) % 7;
                    dayOfWeekTotals[dow] += dc.Count;
                    dayOfWeekCounts[dow]++;
                }
                _snapshotCache[$"dayofweek_{days}d"] = GenerateDayOfWeekChart(dayOfWeekTotals, dayOfWeekCounts, days);
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

    private static int GetMaxStreak(DateOnly[] dates, int[] contributions)
    {
        if (dates.Length == 0) return 0;
        int maxStreak = 0, currentStreak = 0;
        var sorted = dates.Zip(contributions).OrderBy(x => x.First).ToArray();
        DateOnly? prevDate = null;

        foreach (var (date, count) in sorted)
        {
            if (count > 0)
            {
                if (prevDate.HasValue && date.DayNumber - prevDate.Value.DayNumber == 1)
                    currentStreak++;
                else
                    currentStreak = 1;
                maxStreak = Math.Max(maxStreak, currentStreak);
                prevDate = date;
            }
            else
            {
                currentStreak = 0;
                prevDate = null;
            }
        }
        return maxStreak;
    }

    private static string PeriodLabel(int days) => days switch
    {
        1 => "24h",
        7 => "7 Days",
        30 => "30 Days",
        _ => $"{days} Days"
    };
}
