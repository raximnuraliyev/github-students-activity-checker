using System.Globalization;
using System.Text;
using CsvHelper;
using GithubActivityChecker.Configuration;
using GithubActivityChecker.Data;
using GithubActivityChecker.Models;
using GithubActivityChecker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace GithubActivityChecker.TelegramBot;

/// <summary>
/// Background service that runs the Telegram Bot polling loop.
/// Handles admin commands: /status, /list_inactive, /check, /sync_now, /help
/// and visualization commands: /vis_activity, /vis_dist, /vis_trend, /vis_pro
/// </summary>
public class TelegramBotService : BackgroundService
{
    private readonly TelegramBotClient? _bot;
    private readonly TelegramSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly ISyncService _syncService;
    private readonly IPlotService _plotService;
    private readonly ILogger<TelegramBotService> _logger;

    private int _isSyncing; // 0 = idle, 1 = syncing (atomic guard)

    public TelegramBotService(
        IOptions<TelegramSettings> settings,
        IServiceScopeFactory scopeFactory,
        IGitHubService gitHubService,
        ISyncService syncService,
        IPlotService plotService,
        ILogger<TelegramBotService> logger)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _gitHubService = gitHubService;
        _syncService = syncService;
        _plotService = plotService;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_settings.BotToken))
            _bot = new TelegramBotClient(_settings.BotToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.BotToken) || _bot is null)
        {
            _logger.LogWarning("Telegram Bot Token is not configured. Bot service will not start. Set 'Telegram:BotToken' in configuration or user secrets.");
            return;
        }

        _logger.LogInformation("Telegram Bot service starting...");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        _bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Telegram Bot is now receiving updates");

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        // Handle callback queries (language selection from /start)
        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackQueryAsync(bot, callback, ct);
            return;
        }

        if (update.Message is not { Text: { } messageText } message)
            return;

        var chatId = message.Chat.Id;

        // Authorization check
        if (_settings.AuthorizedChatIds.Length > 0 && !_settings.AuthorizedChatIds.Contains(chatId))
        {
            await bot.SendMessage(chatId, "⛔ Unauthorized. Your Chat ID is not in the authorized list.", cancellationToken: ct);
            _logger.LogWarning("Unauthorized access attempt from Chat ID {ChatId}", chatId);
            return;
        }

        var parts = messageText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        // Remove @BotName suffix if present (e.g. /status@MyBot)
        if (command.Contains('@'))
            command = command[..command.IndexOf('@')];

        try
        {
            switch (command)
            {
                case "/start":
                    await SendStartLanguageSelectionAsync(bot, chatId, ct);
                    break;

                case "/help":
                    await SendHelpAsync(bot, chatId, ct);
                    break;

                case "/status":
                    await SendStatusAsync(bot, chatId, ct);
                    break;

                case "/list_inactive":
                    await SendInactiveListAsync(bot, chatId, ct);
                    break;

                case "/check":
                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(chatId, "Usage: /check [github_username]", cancellationToken: ct);
                        return;
                    }
                    await SendCheckAsync(bot, chatId, parts[1], ct);
                    break;

                case "/sync_now":
                    await TriggerManualSyncAsync(bot, chatId, ct);
                    break;

                case "/vis_activity":
                    await SendVisActivityAsync(bot, chatId, parts, ct);
                    break;

                case "/vis_dist":
                    await SendVisDistAsync(bot, chatId, parts, ct);
                    break;

                case "/vis_trend":
                    await SendVisTrendAsync(bot, chatId, parts, ct);
                    break;

                case "/vis_pro":
                    await SendVisProAsync(bot, chatId, parts, ct);
                    break;

                default:
                    await bot.SendMessage(chatId, "Unknown command. Use /help to see available commands.", cancellationToken: ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {Command} from Chat {ChatId}", command, chatId);
            await bot.SendMessage(chatId, "❌ An error occurred processing your command.", cancellationToken: ct);
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Telegram Bot polling error");
        return Task.CompletedTask;
    }

    // ==================== Callback Query Handler ====================

    private async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        if (callback.Message is null || callback.Data is null)
            return;

        var chatId = callback.Message.Chat.Id;

        // Authorization check
        if (_settings.AuthorizedChatIds.Length > 0 && !_settings.AuthorizedChatIds.Contains(chatId))
        {
            await bot.AnswerCallbackQuery(callback.Id, "⛔ Unauthorized.", cancellationToken: ct);
            return;
        }

        try
        {
            switch (callback.Data)
            {
                case "lang_en":
                    await bot.AnswerCallbackQuery(callback.Id, "🇬🇧 English selected", cancellationToken: ct);
                    await SendStartExplanationAsync(bot, chatId, "en", ct);
                    break;
                case "lang_uz":
                    await bot.AnswerCallbackQuery(callback.Id, "🇺🇿 O'zbek tili tanlandi", cancellationToken: ct);
                    await SendStartExplanationAsync(bot, chatId, "uz", ct);
                    break;
                case "lang_ru":
                    await bot.AnswerCallbackQuery(callback.Id, "🇷🇺 Русский выбран", cancellationToken: ct);
                    await SendStartExplanationAsync(bot, chatId, "ru", ct);
                    break;
                default:
                    await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling callback query {Data}", callback.Data);
            await bot.AnswerCallbackQuery(callback.Id, "❌ Error", cancellationToken: ct);
        }
    }

    // ==================== Command Handlers ====================

    private async Task SendStartLanguageSelectionAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🇬🇧 English", "lang_en"),
                InlineKeyboardButton.WithCallbackData("🇺🇿 O'zbekcha", "lang_uz"),
                InlineKeyboardButton.WithCallbackData("🇷🇺 Русский", "lang_ru"),
            }
        });

        await bot.SendMessage(
            chatId,
            "👋 *Welcome to GitHub Activity Monitor!*\n\nPlease choose your language:\nTilni tanlang:\nВыберите язык:",
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task SendStartExplanationAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        var text = lang switch
        {
            "uz" => """
                🤖 *GitHub Faoliyat Monitori va Litsenziya Optimallashtiruvchi*

                📌 *Bu tizim nima qiladi?*
                Bu tizim universitetdagi ~1,500 ta talabaning GitHub faoliyatini avtomatik ravishda kuzatib boradi. Har bir talabaning GitHub Pro litsenziyasi bor va bu tizim ularning litsenziyalaridan samarali foydalanayotganligini tekshiradi.

                ⚙️ *Qanday ishlaydi?*
                1️⃣ *Kunlik sinxronizatsiya* — Har kuni soat 02:00 da tizim GitHub GraphQL API orqali barcha talabalarning contribution (hissa) ma'lumotlarini tortib oladi.
                2️⃣ *Faollik tahlili* — Har bir talabaning oxirgi 30 va 60 kunlik faolligi hisoblanadi.
                3️⃣ *Status belgilash* — Talabalar 3 ta statusga bo'linadi:
                  • ✅ *Faol* — Oxirgi 30 kunda hissa qo'shgan
                  • ⚠️ *Nofaol* — 30+ kun hissa qo'shmagan
                  • 🔴 *O'chirish kutilmoqda* — 60+ kun nofaol
                4️⃣ *Vizualizatsiya* — ScottPlot kutubxonasi yordamida bar chart, histogram, trend va pie chart diagrammalar yaratiladi.
                5️⃣ *Bildirishnomalar* — Nofaol talabalar ro'yxati CSV fayl sifatida yuklab olinadi.

                📊 *Buyruqlar:*
                /status — Umumiy holat ko'rinishi
                /list\_inactive — Nofaol talabalar ro'yxati (CSV)
                /check [username] — Real vaqtda foydalanuvchi tekshiruvi
                /sync\_now — Qo'lda sinxronizatsiya
                /vis\_activity [1d/7d/30d] — Faollik diagrammasi
                /vis\_dist [1d/7d/30d] — Hissalar taqsimoti
                /vis\_trend [1d/7d/30d] — Trend grafigi
                /vis\_pro [1d/7d/30d] — Faol/Nofaol nisbati
                /help — Buyruqlar ro'yxati

                🔐 *Xavfsizlik:*
                Faqat ruxsat berilgan Chat ID'lar ushbu botdan foydalana oladi. Ruxsatsiz foydalanuvchilar avtomatik bloklanadi.

                💡 *Maqsad:*
                GitHub Pro litsenziyalarini samarali boshqarish — nofaol talabalardan litsenziyalarni qaytarib olish va faol talabalarni rag'batlantirish.
                """,

            "ru" => """
                🤖 *GitHub Activity Monitor — Оптимизатор лицензий*

                📌 *Что делает эта система?*
                Система автоматически отслеживает активность ~1,500 студентов на GitHub. У каждого студента есть лицензия GitHub Pro, и система проверяет, эффективно ли они её используют.

                ⚙️ *Как это работает?*
                1️⃣ *Ежедневная синхронизация* — Каждый день в 02:00 система через GitHub GraphQL API загружает данные о вкладах (contributions) всех студентов.
                2️⃣ *Анализ активности* — Для каждого студента рассчитывается активность за последние 30 и 60 дней.
                3️⃣ *Присвоение статуса* — Студенты делятся на 3 категории:
                  • ✅ *Активный* — Были contributions за последние 30 дней
                  • ⚠️ *Неактивный* — Нет contributions более 30 дней
                  • 🔴 *На удаление* — Неактивен более 60 дней
                4️⃣ *Визуализация* — С помощью библиотеки ScottPlot создаются графики: столбчатая диаграмма, гистограмма, тренды и круговая диаграмма.
                5️⃣ *Уведомления* — Список неактивных студентов можно скачать в формате CSV.

                📊 *Команды:*
                /status — Общая статистика
                /list\_inactive — Список неактивных студентов (CSV)
                /check [username] — Проверка пользователя в реальном времени
                /sync\_now — Ручная синхронизация
                /vis\_activity [1d/7d/30d] — График активности
                /vis\_dist [1d/7d/30d] — Распределение вкладов
                /vis\_trend [1d/7d/30d] — График трендов
                /vis\_pro [1d/7d/30d] — Соотношение активных/неактивных
                /help — Список команд

                🔐 *Безопасность:*
                Только авторизованные Chat ID могут использовать бота. Неавторизованные пользователи автоматически блокируются.

                💡 *Цель:*
                Эффективное управление лицензиями GitHub Pro — отзыв лицензий у неактивных студентов и поощрение активных.
                """,

            _ => """
                🤖 *GitHub Activity Monitor & License Optimizer*

                📌 *What does this system do?*
                This system automatically monitors the GitHub activity of ~1,500 university students. Each student has a GitHub Pro license, and this system verifies whether they are actively using it.

                ⚙️ *How does it work?*
                1️⃣ *Daily Sync* — Every day at 02:00 AM, the system fetches contribution data for all students via the GitHub GraphQL API.
                2️⃣ *Activity Analysis* — Each student's activity over the last 30 and 60 days is calculated.
                3️⃣ *Status Assignment* — Students are categorized into 3 statuses:
                  • ✅ *Active* — Had contributions in the last 30 days
                  • ⚠️ *Inactive* — No contributions for 30+ days
                  • 🔴 *Pending Removal* — Inactive for 60+ days
                4️⃣ *Visualization* — Charts are generated using ScottPlot: bar charts, histograms, trend lines, and pie charts to give you a visual overview.
                5️⃣ *Notifications & Reports* — Inactive students can be exported as a CSV file for license review.

                📊 *Commands:*
                /status — Overview of all student statuses
                /list\_inactive — Download inactive students list (CSV)
                /check [username] — Real-time check for a specific student
                /sync\_now — Manually trigger a full sync
                /vis\_activity [1d/7d/30d] — Activity bar chart
                /vis\_dist [1d/7d/30d] — Contribution distribution histogram
                /vis\_trend [1d/7d/30d] — Usage trend line graph
                /vis\_pro [1d/7d/30d] — Active vs Inactive pie chart
                /help — Show command list

                🔐 *Security:*
                Only authorized Chat IDs can interact with this bot. Unauthorized users are automatically blocked.

                💡 *Purpose:*
                Efficiently manage GitHub Pro licenses — reclaim licenses from inactive students and incentivize active usage.
                """
        };

        await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendHelpAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        const string help = """
            🤖 *GitHub Activity Monitor — Commands*

            /status — Summary of all student statuses
            /list\_inactive — Download CSV of inactive students \(30 days\)
            /check \[username\] — Real\-time check for a specific student
            /sync\_now — Manually trigger a full sync

            📊 *Visualization Commands* \(optional: 1d, 7d, 30d\)
            /vis\_activity \[period\] — Activity bar chart
            /vis\_dist \[period\] — Contribution distribution histogram
            /vis\_trend \[period\] — Usage trend line graph
            /vis\_pro \[period\] — Active vs Inactive pie chart

            /help — Show this message
            """;

        await bot.SendMessage(chatId, help, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
    }

    private async Task SendStatusAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var total = await db.Students.CountAsync(ct);
        var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
        var inactive = await db.Students.CountAsync(s => s.Status == StudentStatus.Inactive, ct);
        var pending = await db.Students.CountAsync(s => s.Status == StudentStatus.Pending_Removal, ct);

        var text = $"""
            📊 *Student License Status*

            Total: {total}
            ✅ Active: {active}
            ⚠️ Inactive (30d): {inactive}
            🔴 Pending Removal (60d): {pending}
            """;

        await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendInactiveListAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inactiveStudents = await db.Students
            .Where(s => s.Status == StudentStatus.Inactive || s.Status == StudentStatus.Pending_Removal)
            .OrderBy(s => s.LastActiveDate)
            .Select(s => new InactiveCsvRow
            {
                UniversityId = s.UniversityId,
                GithubUsername = s.GithubUsername,
                Email = s.Email,
                LastActiveDate = s.LastActiveDate,
                Status = s.Status.ToString()
            })
            .ToListAsync(ct);

        if (inactiveStudents.Count == 0)
        {
            await bot.SendMessage(chatId, "✅ No inactive students found!", cancellationToken: ct);
            return;
        }

        // Generate CSV in memory
        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(inactiveStudents);
        }

        memoryStream.Position = 0;

        var document = InputFile.FromStream(memoryStream, $"inactive_students_{DateTime.UtcNow:yyyyMMdd}.csv");
        await bot.SendDocument(chatId, document,
            caption: $"📄 {inactiveStudents.Count} inactive students as of {DateTime.UtcNow:yyyy-MM-dd}",
            cancellationToken: ct);
    }

    private async Task SendCheckAsync(ITelegramBotClient bot, long chatId, string username, CancellationToken ct)
    {
        await bot.SendMessage(chatId, $"🔍 Fetching real-time data for *{username}*...", parseMode: ParseMode.Markdown, cancellationToken: ct);

        var calendar = await _gitHubService.GetContributionCalendarAsync(username, ct);
        if (calendar is null)
        {
            await bot.SendMessage(chatId, $"❌ Could not fetch data for `{username}`. Check the username or API token.", parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int last30 = calendar.Days
            .Where(d => d.Date >= today.AddDays(-30))
            .Sum(d => d.ContributionCount);

        int last60 = calendar.Days
            .Where(d => d.Date >= today.AddDays(-60))
            .Sum(d => d.ContributionCount);

        int last7 = calendar.Days
            .Where(d => d.Date >= today.AddDays(-7))
            .Sum(d => d.ContributionCount);

        var lastActiveDay = calendar.Days
            .Where(d => d.ContributionCount > 0)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        // Check if student exists in DB
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var student = await db.Students.FirstOrDefaultAsync(s => s.GithubUsername == username, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"👤 *{username}*");
        sb.AppendLine();
        sb.AppendLine($"📅 Total (year): {calendar.TotalContributions}");
        sb.AppendLine($"📊 Last 7 days: {last7}");
        sb.AppendLine($"📊 Last 30 days: {last30}");
        sb.AppendLine($"📊 Last 60 days: {last60}");
        sb.AppendLine($"🕐 Last active: {(lastActiveDay is not null ? lastActiveDay.Date.ToString("yyyy-MM-dd") : "Never")}");

        if (student is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"🏫 Uni ID: {student.UniversityId}");
            sb.AppendLine($"📧 Email: {student.Email}");
            sb.AppendLine($"🔖 DB Status: {student.Status}");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("ℹ️ _Not tracked in the database._");
        }

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task TriggerManualSyncAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
        {
            await bot.SendMessage(chatId, "⏳ A sync is already in progress. Please wait.", cancellationToken: ct);
            return;
        }

        try
        {
            await bot.SendMessage(chatId, "🔄 Manual sync started. This may take a while...", cancellationToken: ct);
            await _syncService.RunFullSyncAsync(ct);
            await bot.SendMessage(chatId, "✅ Manual sync completed successfully!", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            await bot.SendMessage(chatId, $"❌ Sync failed: {ex.Message}", cancellationToken: ct);
        }
        finally
        {
            Interlocked.Exchange(ref _isSyncing, 0);
        }
    }

    // ==================== Visualization Command Handlers ====================

    private static int ParseDays(string[] parts)
    {
        if (parts.Length < 2) return 7; // default 7d
        return parts[1].ToLowerInvariant() switch
        {
            "1d" => 1,
            "7d" => 7,
            "30d" => 30,
            _ => 7
        };
    }

    private static string PeriodLabel(int days) => days switch
    {
        1 => "24h",
        7 => "7 days",
        30 => "30 days",
        _ => $"{days} days"
    };

    private async Task SendVisActivityAsync(ITelegramBotClient bot, long chatId, string[] parts, CancellationToken ct)
    {
        int days = ParseDays(parts);
        await bot.SendMessage(chatId, $"📊 Generating activity chart ({PeriodLabel(days)})...", cancellationToken: ct);

        try
        {
            // Try pre-rendered snapshot first
            var snapshotBytes = _plotService.GetSnapshot($"activity_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"activity_{days}d.png"),
                    caption: $"📊 Student Activity — Last {PeriodLabel(days)} (cached snapshot)",
                    cancellationToken: ct);
                return;
            }

            // Generate on-the-fly
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

            var data = await db.DailyContributions
                .Where(dc => dc.Date >= since)
                .GroupBy(dc => dc.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(x => x.Count) })
                .OrderBy(x => x.Date)
                .ToListAsync(ct);

            var dates = data.Select(d => d.Date).ToArray();
            var totals = data.Select(d => d.Total).ToArray();

            var imageBytes = _plotService.GenerateActivityChart(dates, totals, days);
            using var stream = new MemoryStream(imageBytes);

            int totalContribs = totals.Sum();
            await bot.SendPhoto(chatId, InputFile.FromStream(stream, $"activity_{days}d.png"),
                caption: $"📊 Student Activity — Last {PeriodLabel(days)}\nTotal contributions: {totalContribs:N0}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating activity chart");
            await bot.SendMessage(chatId, "❌ Failed to generate activity chart.", cancellationToken: ct);
        }
    }

    private async Task SendVisDistAsync(ITelegramBotClient bot, long chatId, string[] parts, CancellationToken ct)
    {
        int days = ParseDays(parts);
        await bot.SendMessage(chatId, $"📊 Generating distribution histogram ({PeriodLabel(days)})...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"dist_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"distribution_{days}d.png"),
                    caption: $"📊 Contribution Distribution — Last {PeriodLabel(days)} (cached snapshot)",
                    cancellationToken: ct);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

            // Per-student contribution sums for the period
            var studentSums = await db.DailyContributions
                .Where(dc => dc.Date >= since)
                .GroupBy(dc => dc.StudentId)
                .Select(g => g.Sum(x => x.Count))
                .ToListAsync(ct);

            // Include students with 0 contributions
            var activeStudentIds = await db.DailyContributions
                .Where(dc => dc.Date >= since)
                .Select(dc => dc.StudentId)
                .Distinct()
                .ToListAsync(ct);

            var totalStudents = await db.Students.CountAsync(ct);
            var zeroCount = totalStudents - activeStudentIds.Count;
            for (int i = 0; i < zeroCount; i++)
                studentSums.Add(0);

            var imageBytes = _plotService.GenerateDistributionHistogram(studentSums.ToArray(), days);
            using var stream = new MemoryStream(imageBytes);

            double avg = studentSums.Count > 0 ? studentSums.Average() : 0;
            int zeroStudents = studentSums.Count(s => s == 0);
            double inactiveRate = studentSums.Count > 0 ? (double)zeroStudents / studentSums.Count * 100 : 0;

            await bot.SendPhoto(chatId, InputFile.FromStream(stream, $"distribution_{days}d.png"),
                caption: $"📊 Contribution Distribution — Last {PeriodLabel(days)}\n" +
                         $"Students: {studentSums.Count:N0} | Avg: {avg:F1} | {inactiveRate:F1}% with zero contributions",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating distribution histogram");
            await bot.SendMessage(chatId, "❌ Failed to generate distribution chart.", cancellationToken: ct);
        }
    }

    private async Task SendVisTrendAsync(ITelegramBotClient bot, long chatId, string[] parts, CancellationToken ct)
    {
        int days = ParseDays(parts);
        await bot.SendMessage(chatId, $"📊 Generating trend graph ({PeriodLabel(days)})...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"trend_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"trend_{days}d.png"),
                    caption: $"📈 Usage Trend — Last {PeriodLabel(days)} (cached snapshot)",
                    cancellationToken: ct);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

            var data = await db.DailyContributions
                .Where(dc => dc.Date >= since)
                .GroupBy(dc => dc.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(x => x.Count), ActiveStudents = g.Select(x => x.StudentId).Distinct().Count() })
                .OrderBy(x => x.Date)
                .ToListAsync(ct);

            var dates = data.Select(d => d.Date).ToArray();
            var totals = data.Select(d => d.Total).ToArray();
            var activeStudents = data.Select(d => d.ActiveStudents).ToArray();

            var imageBytes = _plotService.GenerateTrendLineChart(dates, totals, activeStudents, days);
            using var stream = new MemoryStream(imageBytes);

            string trendDirection = totals.Length >= 2
                ? (totals[^1] > totals[0] ? "📈 Upward" : totals[^1] < totals[0] ? "📉 Downward" : "➡️ Flat")
                : "➡️ Insufficient data";

            await bot.SendPhoto(chatId, InputFile.FromStream(stream, $"trend_{days}d.png"),
                caption: $"📈 Usage Trend — Last {PeriodLabel(days)}\nTrend: {trendDirection}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating trend chart");
            await bot.SendMessage(chatId, "❌ Failed to generate trend chart.", cancellationToken: ct);
        }
    }

    private async Task SendVisProAsync(ITelegramBotClient bot, long chatId, string[] parts, CancellationToken ct)
    {
        int days = ParseDays(parts);
        await bot.SendMessage(chatId, $"📊 Generating Pro user pie chart ({PeriodLabel(days)})...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"pro_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"pro_status_{days}d.png"),
                    caption: $"🥧 Pro License Status — Last {PeriodLabel(days)} (cached snapshot)",
                    cancellationToken: ct);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var total = await db.Students.CountAsync(ct);
            var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
            var inactive = await db.Students.CountAsync(s => s.Status == StudentStatus.Inactive, ct);
            var pending = await db.Students.CountAsync(s => s.Status == StudentStatus.Pending_Removal, ct);

            var imageBytes = _plotService.GenerateProPieChart(active, inactive, pending, days);
            using var stream = new MemoryStream(imageBytes);

            double inactiveRate = total > 0 ? (double)(inactive + pending) / total * 100 : 0;

            await bot.SendPhoto(chatId, InputFile.FromStream(stream, $"pro_status_{days}d.png"),
                caption: $"🥧 Pro License Status — {total:N0} Students\n" +
                         $"Current inactivity rate is {inactiveRate:F1}%. " +
                         $"{pending} students are candidates for license removal.",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating pro pie chart");
            await bot.SendMessage(chatId, "❌ Failed to generate pro status chart.", cancellationToken: ct);
        }
    }
}

/// <summary>
/// CSV row model for inactive student export.
/// </summary>
public class InactiveCsvRow
{
    public string UniversityId { get; set; } = string.Empty;
    public string GithubUsername { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? LastActiveDate { get; set; }
    public string Status { get; set; } = string.Empty;
}
