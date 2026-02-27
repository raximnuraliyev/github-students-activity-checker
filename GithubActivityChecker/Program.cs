using GithubActivityChecker;

using GithubActivityChecker.Configuration;
using GithubActivityChecker.Data;
using GithubActivityChecker.Jobs;
using GithubActivityChecker.Services;
using GithubActivityChecker.TelegramBot;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;

// ───── Bootstrap Serilog ─────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    Log.Information("Starting GitHub Activity Monitor service");

    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();

    // ───── Configuration Binding ─────
    builder.Services.Configure<GitHubSettings>(builder.Configuration.GetSection(GitHubSettings.SectionName));
    builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection(TelegramSettings.SectionName));
    builder.Services.Configure<InactivityPolicySettings>(builder.Configuration.GetSection(InactivityPolicySettings.SectionName));
    builder.Services.Configure<SyncScheduleSettings>(builder.Configuration.GetSection(SyncScheduleSettings.SectionName));

    // ───── PostgreSQL / EF Core ─────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    // ───── Application Services ─────
    builder.Services.AddSingleton<IGitHubService, GitHubService>();
    builder.Services.AddSingleton<ISyncService, SyncService>();
    builder.Services.AddSingleton<IPlotService, PlotService>();

    // ───── Quartz.NET Scheduler ─────
    var cronExpression = builder.Configuration
        .GetSection(SyncScheduleSettings.SectionName)
        .GetValue<string>("CronExpression") ?? "0 0 2 * * ?";

    builder.Services.AddQuartz(q =>
    {
        var jobKey = new JobKey("FullSyncJob");
        q.AddJob<FullSyncJob>(opts => opts.WithIdentity(jobKey));

        q.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity("FullSyncJob-Trigger")
            .WithCronSchedule(cronExpression));

        // Snapshot job: regenerates visualization charts after sync (5 min offset)
        var snapshotKey = new JobKey("SnapshotJob");
        q.AddJob<SnapshotJob>(opts => opts.WithIdentity(snapshotKey));

        q.AddTrigger(opts => opts
            .ForJob(snapshotKey)
            .WithIdentity("SnapshotJob-Trigger")
            .WithCronSchedule("0 5 2 * * ?")); // 02:05 AM daily
    });
    builder.Services.AddQuartzHostedService(opts =>
    {
        opts.WaitForJobsToComplete = true;
    });

    // ───── Telegram Bot ─────
    builder.Services.AddHostedService<TelegramBotService>();

    // ───── Build & Run ─────
    var host = builder.Build();

    // Auto-apply pending migrations on startup
    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied successfully");
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
