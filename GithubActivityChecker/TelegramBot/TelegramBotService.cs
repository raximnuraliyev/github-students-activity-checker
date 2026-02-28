using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
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
/// Role-based access: Head > Admin > Student.
/// All responses are localized based on the user's stored language preference.
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
            _logger.LogWarning("Telegram Bot Token is not configured. Bot service will not start.");
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

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    // Track which chat is waiting for a CSV file and which target
    private readonly ConcurrentDictionary<long, string> _pendingCsvImport = new();

    // ==================== User / Role Management ====================

    /// <summary>
    /// Looks up the BotUser in the database. Creates one if missing.
    /// Auto-assigns Head role if the Telegram username matches HeadUsername config.
    /// </summary>
    private async Task<BotUser> GetOrCreateBotUserAsync(long chatId, string? username, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.BotUsers.FindAsync([chatId], ct);

        if (user is not null)
        {
            // Keep username in sync
            if (username is not null && user.Username != username)
            {
                user.Username = username;
                // Auto-promote to Head if username matches and not already Head
                if (string.Equals(username, _settings.HeadUsername, StringComparison.OrdinalIgnoreCase)
                    && user.Role != BotUserRole.Head)
                {
                    user.Role = BotUserRole.Head;
                }
                await db.SaveChangesAsync(ct);
            }
            return user;
        }

        // New user — determine role
        var role = BotUserRole.Student;
        if (!string.IsNullOrWhiteSpace(username)
            && string.Equals(username, _settings.HeadUsername, StringComparison.OrdinalIgnoreCase))
        {
            role = BotUserRole.Head;
        }

        user = new BotUser
        {
            ChatId = chatId,
            Username = username,
            Role = role,
            Language = "en",
            CreatedAt = DateTime.UtcNow
        };

        db.BotUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private static bool IsAdmin(BotUser u) => u.Role is BotUserRole.Admin or BotUserRole.Head;
    private static bool IsHead(BotUser u) => u.Role is BotUserRole.Head;

    private async Task SaveLanguageAsync(long chatId, string lang, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.BotUsers.FindAsync([chatId], ct);
        if (user is not null)
        {
            user.Language = lang;
            await db.SaveChangesAsync(ct);
        }
    }

    // ==================== Update Handler ====================

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackQueryAsync(bot, callback, ct);
            return;
        }

        if (update.Message is not { } message)
            return;

        var chatId = message.Chat.Id;
        var tgUsername = message.From?.Username;

        // Resolve user (role + language)
        var botUser = await GetOrCreateBotUserAsync(chatId, tgUsername, ct);
        var lang = botUser.Language;

        // Handle document uploads (CSV import) — admin only
        if (message.Document is { } doc)
        {
            if (!IsAdmin(botUser))
            {
                await bot.SendMessage(chatId, Loc.Get("no_permission", lang), cancellationToken: ct);
                return;
            }
            await HandleDocumentUploadAsync(bot, chatId, doc, lang, ct);
            return;
        }

        if (message.Text is not { } messageText)
            return;

        var parts = messageText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        if (command.Contains('@'))
            command = command[..command.IndexOf('@')];

        // Commands available to everyone (students)
        var studentCommands = new HashSet<string> { "/start", "/help", "/check" };
        // Commands only available to Head
        var headCommands = new HashSet<string> { "/add_admin", "/remove_admin", "/list_admins" };

        try
        {
            // Role gating
            if (!studentCommands.Contains(command) && !IsAdmin(botUser))
            {
                await bot.SendMessage(chatId, Loc.Get("no_permission", lang), cancellationToken: ct);
                return;
            }
            if (headCommands.Contains(command) && !IsHead(botUser))
            {
                await bot.SendMessage(chatId, Loc.Get("head_only", lang), cancellationToken: ct);
                return;
            }

            switch (command)
            {
                case "/start":
                    await SendStartLanguageSelectionAsync(bot, chatId, ct);
                    break;

                case "/help":
                    await SendHelpAsync(bot, chatId, botUser, ct);
                    break;

                case "/status":
                    await SendStatusAsync(bot, chatId, lang, ct);
                    break;

                case "/list_inactive":
                    await SendInactiveListAsync(bot, chatId, lang, ct);
                    break;

                case "/check":
                    if (parts.Length < 2)
                    {
                        await bot.SendMessage(chatId, Loc.Get("check_usage", lang), cancellationToken: ct);
                        return;
                    }
                    await SendCheckAsync(bot, chatId, parts[1], lang, ct);
                    break;

                case "/sync_now":
                    await TriggerManualSyncAsync(bot, chatId, lang, ct);
                    break;

                case "/top":
                    int topN = parts.Length >= 2 && int.TryParse(parts[1], out var n) ? Math.Clamp(n, 1, 50) : 10;
                    await SendTopContributorsAsync(bot, chatId, topN, lang, ct);
                    break;

                case "/summary":
                    await SendDetailedSummaryAsync(bot, chatId, lang, ct);
                    break;

                case "/import":
                    await SendImportPromptAsync(bot, chatId, lang, ct);
                    break;

                case "/vis_activity":
                    await SendVisActivityAsync(bot, chatId, parts, lang, ct);
                    break;

                case "/vis_dist":
                    await SendVisDistAsync(bot, chatId, parts, lang, ct);
                    break;

                case "/vis_trend":
                    await SendVisTrendAsync(bot, chatId, parts, lang, ct);
                    break;

                case "/vis_pro":
                    await SendVisProAsync(bot, chatId, parts, lang, ct);
                    break;

                case "/vis_heatmap":
                    await SendSnapshotChartAsync(bot, chatId, "heatmap", parts, lang, ct);
                    break;

                case "/vis_area":
                    await SendSnapshotChartAsync(bot, chatId, "area", parts, lang, ct);
                    break;

                case "/vis_scatter":
                    await SendSnapshotChartAsync(bot, chatId, "scatter", parts, lang, ct);
                    break;

                case "/vis_gauge":
                    await SendSnapshotChartAsync(bot, chatId, "gauge", parts, lang, ct);
                    break;

                case "/vis_waterfall":
                    await SendSnapshotChartAsync(bot, chatId, "waterfall", parts, lang, ct);
                    break;

                case "/vis_funnel":
                    await SendSnapshotChartAsync(bot, chatId, "funnel", parts, lang, ct);
                    break;

                case "/vis_top":
                    await SendSnapshotChartAsync(bot, chatId, "top", parts, lang, ct);
                    break;

                case "/vis_weekly":
                    await SendSnapshotChartAsync(bot, chatId, "weekly", parts, lang, ct);
                    break;

                case "/vis_dayofweek":
                    await SendSnapshotChartAsync(bot, chatId, "dayofweek", parts, lang, ct);
                    break;

                case "/vis_stacked":
                    await SendSnapshotChartAsync(bot, chatId, "stacked", parts, lang, ct);
                    break;

                case "/charts":
                    await SendChartsMenuAsync(bot, chatId, lang, ct);
                    break;

                case "/export":
                    await SendEnhancedExportAsync(bot, chatId, lang, ct);
                    break;

                case "/report":
                    await SendFullReportAsync(bot, chatId, parts, lang, ct);
                    break;

                // Head-only admin management
                case "/add_admin":
                    await HandleAddAdminAsync(bot, chatId, parts, lang, ct);
                    break;

                case "/remove_admin":
                    await HandleRemoveAdminAsync(bot, chatId, parts, lang, ct);
                    break;

                case "/list_admins":
                    await HandleListAdminsAsync(bot, chatId, lang, ct);
                    break;

                default:
                    await bot.SendMessage(chatId, Loc.Get("unknown_cmd", lang), cancellationToken: ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {Command} from Chat {ChatId}", command, chatId);
            await bot.SendMessage(chatId, Loc.Get("error", lang), cancellationToken: ct);
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
        var tgUsername = callback.From.Username;
        var botUser = await GetOrCreateBotUserAsync(chatId, tgUsername, ct);

        try
        {
            switch (callback.Data)
            {
                case "lang_en":
                    await SaveLanguageAsync(chatId, "en", ct);
                    await bot.AnswerCallbackQuery(callback.Id, "🇬🇧 English selected", cancellationToken: ct);
                    await SendStartExplanationAsync(bot, chatId, "en", botUser.Role, ct);
                    break;
                case "lang_uz":
                    await SaveLanguageAsync(chatId, "uz", ct);
                    await bot.AnswerCallbackQuery(callback.Id, "🇺🇿 O'zbek tili tanlandi", cancellationToken: ct);
                    await SendStartExplanationAsync(bot, chatId, "uz", botUser.Role, ct);
                    break;
                case "lang_ru":
                    await SaveLanguageAsync(chatId, "ru", ct);
                    await bot.AnswerCallbackQuery(callback.Id, "🇷🇺 Русский выбран", cancellationToken: ct);
                    await SendStartExplanationAsync(bot, chatId, "ru", botUser.Role, ct);
                    break;
                case "import_students":
                {
                    var lang = botUser.Language;
                    if (!IsAdmin(botUser))
                    {
                        await bot.AnswerCallbackQuery(callback.Id, "⛔", cancellationToken: ct);
                        return;
                    }
                    await bot.AnswerCallbackQuery(callback.Id, "📥", cancellationToken: ct);
                    _pendingCsvImport[chatId] = "students";
                    await bot.SendMessage(chatId, Loc.Get("import_send_csv", lang),
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    break;
                }
                case "import_cancel":
                {
                    var lang = botUser.Language;
                    await bot.AnswerCallbackQuery(callback.Id, "Cancelled", cancellationToken: ct);
                    _pendingCsvImport.TryRemove(chatId, out _);
                    await bot.SendMessage(chatId, Loc.Get("import_cancelled", lang), cancellationToken: ct);
                    break;
                }
                default:
                {
                    // Handle chart menu callbacks: charts_{type}_{period}
                    if (callback.Data.StartsWith("charts_"))
                    {
                        var lang = botUser.Language;
                        if (!IsAdmin(botUser))
                        {
                            await bot.AnswerCallbackQuery(callback.Id, "⛔", cancellationToken: ct);
                            return;
                        }
                        await bot.AnswerCallbackQuery(callback.Id, "📊 Generating...", cancellationToken: ct);

                        var cbParts = callback.Data.Split('_');
                        if (cbParts.Length >= 3)
                        {
                            var chartType = cbParts[1];
                            var period = cbParts[2];
                            // Map chart type to vis command parts format
                            var fakeParts = new[] { $"/vis_{chartType}", period };

                            switch (chartType)
                            {
                                case "activity":
                                    await SendVisActivityAsync(bot, chatId, fakeParts, lang, ct);
                                    break;
                                case "dist":
                                    await SendVisDistAsync(bot, chatId, fakeParts, lang, ct);
                                    break;
                                case "trend":
                                    await SendVisTrendAsync(bot, chatId, fakeParts, lang, ct);
                                    break;
                                case "pro":
                                    await SendVisProAsync(bot, chatId, fakeParts, lang, ct);
                                    break;
                                default:
                                    await SendSnapshotChartAsync(bot, chatId, chartType, fakeParts, lang, ct);
                                    break;
                            }
                        }
                        else if (cbParts.Length == 2)
                        {
                            // Period selection: charts_period_{type} pages
                            await SendChartPeriodMenuAsync(bot, chatId, cbParts[1], lang, ct);
                        }
                    }
                    else
                    {
                        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling callback query {Data}", callback.Data);
            await bot.AnswerCallbackQuery(callback.Id, "❌ Error", cancellationToken: ct);
        }
    }

    // ==================== /start ====================

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

    private async Task SendStartExplanationAsync(ITelegramBotClient bot, long chatId, string lang, BotUserRole role, CancellationToken ct)
    {
        var text = lang switch
        {
            "uz" => $"""
                🤖 *GitHub Faoliyat Monitori va Litsenziya Optimallashtiruvchi*

                📌 *Bu tizim nima qiladi?*
                Bu tizim universitetdagi ~1,500 ta talabaning GitHub faoliyatini avtomatik ravishda kuzatib boradi. Har bir talabaning GitHub Pro litsenziyasi bor va bu tizim ularning litsenziyalaridan samarali foydalanayotganligini tekshiradi.

                ⚙️ *Qanday ishlaydi?*
                1️⃣ *Kunlik sinxronizatsiya* — Har kuni soat 02:00 da tizim GitHub GraphQL API orqali barcha talabalarning contribution ma'lumotlarini tortib oladi.
                2️⃣ *Faollik tahlili* — Har bir talabaning oxirgi 30 va 60 kunlik faolligi hisoblanadi.
                3️⃣ *Status belgilash* — Talabalar 3 ta statusga bo'linadi:
                  • ✅ *Faol* — Oxirgi 30 kunda hissa qo'shgan
                  • ⚠️ *Nofaol* — 30+ kun hissa qo'shmagan
                  • 🔴 *O'chirish kutilmoqda* — 60+ kun nofaol
                4️⃣ *Vizualizatsiya* — Diagrammalar yaratiladi: faollik, taqsimot, trend va pie chart.
                5️⃣ *Bildirishnomalar* — Nofaol talabalar CSV fayl sifatida yuklab olinadi.

                👤 *Sizning rolingiz: {RoleName(role, lang)}*

                📊 *Buyruqlar:*
                /start — Xush kelibsiz + til tanlash
                /help — Buyruqlar ro'yxati
                /check [username] — Real vaqtda foydalanuvchi tekshiruvi
                {(IsAdminRole(role) ? """
                /status — Umumiy holat
                /list\_inactive — Nofaol talabalar (CSV)
                /sync\_now — Qo'lda sinxronizatsiya
                /vis\_activity — Faollik diagrammasi
                /vis\_dist — Hissalar taqsimoti
                /vis\_trend — Trend grafigi
                /vis\_pro — Faol/Nofaol nisbati
                /top [N] — Eng faol talabalar
                /summary — Batafsil hisobot
                /import — CSV import
                """ : "")}
                {(role == BotUserRole.Head ? """
                👑 *Bosh Admin:*
                /add\_admin [chat\_id] — Admin tayinlash
                /remove\_admin [chat\_id] — Adminni olib tashlash
                /list\_admins — Administratorlar
                """ : "")}
                """,

            "ru" => $"""
                🤖 *GitHub Activity Monitor — Оптимизатор лицензий*

                📌 *Что делает эта система?*
                Система автоматически отслеживает активность ~1,500 студентов на GitHub. У каждого студента есть лицензия GitHub Pro, и система проверяет, эффективно ли они её используют.

                ⚙️ *Как это работает?*
                1️⃣ *Ежедневная синхронизация* — Каждый день в 02:00 система через GitHub GraphQL API загружает данные о вкладах всех студентов.
                2️⃣ *Анализ активности* — Для каждого студента рассчитывается активность за последние 30 и 60 дней.
                3️⃣ *Присвоение статуса*:
                  • ✅ *Активный* — contributions за последние 30 дней
                  • ⚠️ *Неактивный* — нет contributions более 30 дней
                  • 🔴 *На удаление* — неактивен более 60 дней
                4️⃣ *Визуализация* — Графики: активность, распределение, тренды, круговая диаграмма.
                5️⃣ *Отчёты* — Список неактивных студентов в формате CSV.

                👤 *Ваша роль: {RoleName(role, lang)}*

                📊 *Команды:*
                /start — Приветствие + выбор языка
                /help — Список команд
                /check [username] — Проверка пользователя
                {(IsAdminRole(role) ? """
                /status — Общая статистика
                /list\_inactive — Неактивные студенты (CSV)
                /sync\_now — Ручная синхронизация
                /vis\_activity — График активности
                /vis\_dist — Распределение вкладов
                /vis\_trend — График трендов
                /vis\_pro — Активные/Неактивные
                /top [N] — Топ участников
                /summary — Подробный отчёт
                /import — Импорт из CSV
                """ : "")}
                {(role == BotUserRole.Head ? """
                👑 *Главный администратор:*
                /add\_admin [chat\_id] — Назначить админа
                /remove\_admin [chat\_id] — Снять админа
                /list\_admins — Список администраторов
                """ : "")}
                """,

            _ => $"""
                🤖 *GitHub Activity Monitor & License Optimizer*

                📌 *What does this system do?*
                This system automatically monitors the GitHub activity of ~1,500 university students. Each student has a GitHub Pro license, and this system verifies whether they are actively using it.

                ⚙️ *How does it work?*
                1️⃣ *Daily Sync* — Every day at 02:00 AM, the system fetches contribution data via the GitHub GraphQL API.
                2️⃣ *Activity Analysis* — Each student's activity over the last 30 and 60 days is calculated.
                3️⃣ *Status Assignment*:
                  • ✅ *Active* — Had contributions in the last 30 days
                  • ⚠️ *Inactive* — No contributions for 30+ days
                  • 🔴 *Pending Removal* — Inactive for 60+ days
                4️⃣ *Visualization* — Charts: activity, distribution, trends, and pie charts.
                5️⃣ *Reports* — Inactive students can be exported as CSV for license review.

                👤 *Your role: {RoleName(role, lang)}*

                📊 *Commands:*
                /start — Welcome + language selection
                /help — Show command list
                /check [username] — Real-time check for a specific user
                {(IsAdminRole(role) ? """
                /status — Overview of all student statuses
                /list\_inactive — Download inactive students (CSV)
                /sync\_now — Manually trigger a full sync
                /vis\_activity — Activity bar chart
                /vis\_dist — Contribution distribution
                /vis\_trend — Usage trend graph
                /vis\_pro — Active vs Inactive pie chart
                /top [N] — Top N contributors
                /summary — Detailed analytics report
                /import — Import students from CSV
                """ : "")}
                {(role == BotUserRole.Head ? """
                👑 *Head Admin:*
                /add\_admin [chat\_id] — Promote user to Admin
                /remove\_admin [chat\_id] — Demote admin to Student
                /list\_admins — Show all administrators
                """ : "")}
                """
        };

        await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private static bool IsAdminRole(BotUserRole r) => r is BotUserRole.Admin or BotUserRole.Head;

    private static string RoleName(BotUserRole role, string lang) => role switch
    {
        BotUserRole.Head => lang switch { "uz" => "👑 Bosh Admin", "ru" => "👑 Главный администратор", _ => "👑 Head Administrator" },
        BotUserRole.Admin => lang switch { "uz" => "🛡️ Admin", "ru" => "🛡️ Администратор", _ => "🛡️ Administrator" },
        _ => lang switch { "uz" => "👤 Talaba", "ru" => "👤 Студент", _ => "👤 Student" },
    };

    // ==================== /help ====================

    private async Task SendHelpAsync(ITelegramBotClient bot, long chatId, BotUser user, CancellationToken ct)
    {
        var lang = user.Language;
        var helpKey = user.Role switch
        {
            BotUserRole.Head => "help_head",
            BotUserRole.Admin => "help_admin",
            _ => "help_student"
        };
        await bot.SendMessage(chatId, Loc.Get(helpKey, lang), parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
    }

    // ==================== /status ====================

    private async Task SendStatusAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var total = await db.Students.CountAsync(ct);
        var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
        var inactive = await db.Students.CountAsync(s => s.Status == StudentStatus.Inactive, ct);
        var pending = await db.Students.CountAsync(s => s.Status == StudentStatus.Pending_Removal, ct);

        var sb = new StringBuilder();
        sb.AppendLine(Loc.Get("status_title", lang));
        sb.AppendLine();
        sb.AppendLine(Loc.Fmt("status_total", lang, total));
        sb.AppendLine(Loc.Fmt("status_active", lang, active));
        sb.AppendLine(Loc.Fmt("status_inactive", lang, inactive));
        sb.AppendLine(Loc.Fmt("status_pending", lang, pending));

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ==================== /list_inactive ====================

    private async Task SendInactiveListAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
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
                Status = s.Status.ToString(),
                DownloadedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
            })
            .ToListAsync(ct);

        if (inactiveStudents.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.Get("no_inactive", lang), cancellationToken: ct);
            return;
        }

        using var memoryStream = new MemoryStream();
        using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            csv.WriteRecords(inactiveStudents);
        }

        memoryStream.Position = 0;
        var document = InputFile.FromStream(memoryStream, $"inactive_students_{DateTime.UtcNow:yyyyMMdd}.csv");
        await bot.SendDocument(chatId, document,
            caption: Loc.Fmt("inactive_caption", lang, inactiveStudents.Count, DateTime.UtcNow.ToString("yyyy-MM-dd")),
            cancellationToken: ct);
    }

    // ==================== /check ====================

    private async Task SendCheckAsync(ITelegramBotClient bot, long chatId, string username, string lang, CancellationToken ct)
    {
        await bot.SendMessage(chatId, Loc.Fmt("check_fetching", lang, username), parseMode: ParseMode.Markdown, cancellationToken: ct);

        var calendar = await _gitHubService.GetContributionCalendarAsync(username, ct);
        if (calendar is null)
        {
            await bot.SendMessage(chatId, Loc.Fmt("check_error", lang, username), parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int last7 = calendar.Days.Where(d => d.Date >= today.AddDays(-7)).Sum(d => d.ContributionCount);
        int last30 = calendar.Days.Where(d => d.Date >= today.AddDays(-30)).Sum(d => d.ContributionCount);
        int last60 = calendar.Days.Where(d => d.Date >= today.AddDays(-60)).Sum(d => d.ContributionCount);

        var lastActiveDay = calendar.Days
            .Where(d => d.ContributionCount > 0)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        int activeDays = calendar.Days.Count(d => d.ContributionCount > 0);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var student = await db.Students.FirstOrDefaultAsync(s => s.GithubUsername == username, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"👤 *{username}*");
        sb.AppendLine();
        sb.AppendLine(Loc.Fmt("check_total_year", lang, calendar.TotalContributions));
        sb.AppendLine(Loc.Fmt("check_last7", lang, last7));
        sb.AppendLine(Loc.Fmt("check_last30", lang, last30));
        sb.AppendLine($"📊 Last 60d: {last60}");
        sb.AppendLine(Loc.Fmt("check_active_days", lang, activeDays));
        sb.AppendLine($"🕐 {(lastActiveDay is not null ? lastActiveDay.Date.ToString("yyyy-MM-dd") : "N/A")}");

        if (student is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"🏫 {student.UniversityId}");
            sb.AppendLine($"📧 {student.Email}");
            sb.AppendLine($"🔖 {student.Status}");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("ℹ️ _Not tracked in DB_");
        }

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ==================== /sync_now ====================

    private async Task TriggerManualSyncAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
        {
            await bot.SendMessage(chatId, Loc.Get("sync_already", lang), cancellationToken: ct);
            return;
        }

        try
        {
            await bot.SendMessage(chatId, Loc.Get("sync_start", lang), cancellationToken: ct);
            await _syncService.RunFullSyncAsync(ct);
            await bot.SendMessage(chatId, Loc.Get("sync_done", lang), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            await bot.SendMessage(chatId, Loc.Fmt("sync_error", lang, ex.Message), cancellationToken: ct);
        }
        finally
        {
            Interlocked.Exchange(ref _isSyncing, 0);
        }
    }

    // ==================== Visualization Helpers ====================

    private static int ParseDays(string[] parts)
    {
        if (parts.Length < 2) return 7;
        return parts[1].ToLowerInvariant() switch
        {
            "1d" => 1,
            "7d" => 7,
            "30d" => 30,
            _ => 7
        };
    }

    // ==================== /vis_activity ====================

    private async Task SendVisActivityAsync(ITelegramBotClient bot, long chatId, string[] parts, string lang, CancellationToken ct)
    {
        int days = ParseDays(parts);
        var period = Loc.PeriodLabel(days, lang);
        await bot.SendMessage(chatId, $"📊 {Loc.Fmt("vis_activity_caption", lang, period)}...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"activity_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"activity_{days}d.png"),
                    caption: $"{Loc.Fmt("vis_activity_caption", lang, period)} {Loc.Get("cached_snapshot", lang)}",
                    cancellationToken: ct);
                return;
            }

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
                caption: $"{Loc.Fmt("vis_activity_caption", lang, period)}\nTotal: {totalContribs:N0}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating activity chart");
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /vis_dist ====================

    private async Task SendVisDistAsync(ITelegramBotClient bot, long chatId, string[] parts, string lang, CancellationToken ct)
    {
        int days = ParseDays(parts);
        var period = Loc.PeriodLabel(days, lang);
        await bot.SendMessage(chatId, $"📊 {Loc.Fmt("vis_dist_caption", lang, period)}...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"dist_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"distribution_{days}d.png"),
                    caption: $"{Loc.Fmt("vis_dist_caption", lang, period)} {Loc.Get("cached_snapshot", lang)}",
                    cancellationToken: ct);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

            var studentSums = await db.DailyContributions
                .Where(dc => dc.Date >= since)
                .GroupBy(dc => dc.StudentId)
                .Select(g => g.Sum(x => x.Count))
                .ToListAsync(ct);

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
                caption: $"{Loc.Fmt("vis_dist_caption", lang, period)}\n" +
                         $"Students: {studentSums.Count:N0} | Avg: {avg:F1} | {inactiveRate:F1}% zero",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating distribution histogram");
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /vis_trend ====================

    private async Task SendVisTrendAsync(ITelegramBotClient bot, long chatId, string[] parts, string lang, CancellationToken ct)
    {
        int days = ParseDays(parts);
        var period = Loc.PeriodLabel(days, lang);
        await bot.SendMessage(chatId, $"📈 {Loc.Fmt("vis_trend_caption", lang, period)}...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"trend_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"trend_{days}d.png"),
                    caption: $"{Loc.Fmt("vis_trend_caption", lang, period)} {Loc.Get("cached_snapshot", lang)}",
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
                ? (totals[^1] > totals[0] ? "📈" : totals[^1] < totals[0] ? "📉" : "➡️")
                : "➡️";

            await bot.SendPhoto(chatId, InputFile.FromStream(stream, $"trend_{days}d.png"),
                caption: $"{Loc.Fmt("vis_trend_caption", lang, period)} {trendDirection}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating trend chart");
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /vis_pro ====================

    private async Task SendVisProAsync(ITelegramBotClient bot, long chatId, string[] parts, string lang, CancellationToken ct)
    {
        int days = ParseDays(parts);
        var period = Loc.PeriodLabel(days, lang);
        await bot.SendMessage(chatId, $"🥧 {Loc.Fmt("vis_pro_caption", lang, "...")}...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"pro_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"pro_status_{days}d.png"),
                    caption: $"{Loc.Fmt("vis_pro_caption", lang, "...")} {Loc.Get("cached_snapshot", lang)}",
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
                caption: Loc.Fmt("vis_pro_caption", lang, $"{total:N0}") +
                         $"\n{inactiveRate:F1}% inactive · {pending} pending removal",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating pro pie chart");
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /import ====================

    private async Task SendImportPromptAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(Loc.Get("import_students_btn", lang), "import_students") },
            new[] { InlineKeyboardButton.WithCallbackData(Loc.Get("import_cancel_btn", lang), "import_cancel") }
        });

        await bot.SendMessage(chatId, Loc.Get("import_title", lang),
            parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task HandleDocumentUploadAsync(ITelegramBotClient bot, long chatId, Document doc, string lang, CancellationToken ct)
    {
        if (!_pendingCsvImport.TryRemove(chatId, out var target))
        {
            await bot.SendMessage(chatId, Loc.Get("import_no_pending", lang), cancellationToken: ct);
            return;
        }

        var fileName = doc.FileName ?? "unknown";
        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessage(chatId, Loc.Get("import_csv_only", lang), parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId, Loc.Fmt("import_processing", lang, target), parseMode: ParseMode.Markdown, cancellationToken: ct);

        try
        {
            var file = await bot.GetFile(doc.FileId, ct);
            if (file.FilePath is null)
            {
                await bot.SendMessage(chatId, "❌ Could not retrieve file.", cancellationToken: ct);
                return;
            }

            using var httpClient = new HttpClient();
            var fileUrl = $"https://api.telegram.org/file/bot{_settings.BotToken}/{file.FilePath}";
            var fileBytes = await httpClient.GetByteArrayAsync(fileUrl, ct);
            using var fileStream = new MemoryStream(fileBytes);

            switch (target)
            {
                case "students":
                    await ProcessStudentCsvImportAsync(bot, chatId, fileStream, lang, ct);
                    break;
                default:
                    await bot.SendMessage(chatId, $"❌ Unknown target: {target}", cancellationToken: ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing CSV import for {Target}", target);
            await bot.SendMessage(chatId, $"❌ {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task ProcessStudentCsvImportAsync(ITelegramBotClient bot, long chatId, Stream csvStream, string lang, CancellationToken ct)
    {
        using var reader = new StreamReader(csvStream, Encoding.UTF8);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        List<StudentCsvImportRow> records;
        try
        {
            records = csv.GetRecords<StudentCsvImportRow>().ToList();
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, Loc.Fmt("import_csv_error", lang, ex.Message),
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        if (records.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.Get("import_empty", lang), cancellationToken: ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        int added = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var row in records)
        {
            if (string.IsNullOrWhiteSpace(row.GithubUsername))
            {
                skipped++;
                continue;
            }

            try
            {
                var existing = await db.Students
                    .FirstOrDefaultAsync(s => s.GithubUsername == row.GithubUsername.Trim(), ct);

                if (existing is not null)
                {
                    if (!string.IsNullOrWhiteSpace(row.UniversityId))
                        existing.UniversityId = row.UniversityId.Trim();
                    if (!string.IsNullOrWhiteSpace(row.Email))
                        existing.Email = row.Email.Trim();
                    existing.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    var student = new Student
                    {
                        Id = Guid.NewGuid(),
                        UniversityId = row.UniversityId?.Trim() ?? "",
                        GithubUsername = row.GithubUsername.Trim(),
                        Email = row.Email?.Trim() ?? "",
                        Status = StudentStatus.Active,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.Students.Add(student);
                    added++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Row '{row.GithubUsername}': {ex.Message}");
                skipped++;
            }
        }

        await db.SaveChangesAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine(Loc.Get("import_complete", lang));
        sb.AppendLine();
        sb.AppendLine(Loc.Fmt("import_total_rows", lang, records.Count));
        sb.AppendLine(Loc.Fmt("import_added", lang, added));
        sb.AppendLine(Loc.Fmt("import_updated", lang, updated));
        sb.AppendLine(Loc.Fmt("import_skipped", lang, skipped));

        if (errors.Count > 0)
        {
            sb.AppendLine($"\n⚠️ Errors ({errors.Count}):");
            foreach (var err in errors.Take(5))
                sb.AppendLine($"  • {err}");
            if (errors.Count > 5)
                sb.AppendLine($"  ... +{errors.Count - 5} more");
        }

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ==================== /top ====================

    private async Task SendTopContributorsAsync(ITelegramBotClient bot, long chatId, int topN, string lang, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var since = DateTime.UtcNow.AddDays(-30);

            var topContributors = await db.DailyContributions
                .Where(dc => dc.Date >= DateOnly.FromDateTime(since))
                .GroupBy(dc => dc.StudentId)
                .Select(g => new
                {
                    StudentId = g.Key,
                    TotalContributions = g.Sum(x => x.Count),
                    ActiveDays = g.Count(x => x.Count > 0),
                    MaxDay = g.Max(x => x.Count)
                })
                .OrderByDescending(x => x.TotalContributions)
                .Take(topN)
                .ToListAsync(ct);

            if (topContributors.Count == 0)
            {
                await bot.SendMessage(chatId, Loc.Get("top_no_data", lang), parseMode: ParseMode.Markdown, cancellationToken: ct);
                return;
            }

            var studentIds = topContributors.Select(x => x.StudentId).ToList();
            var students = await db.Students
                .Where(s => studentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

            var sb = new StringBuilder();
            sb.AppendLine(Loc.Fmt("top_title", lang, topContributors.Count));

            for (int i = 0; i < topContributors.Count; i++)
            {
                var c = topContributors[i];
                var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => $"*{i + 1}.*" };
                var name = students.TryGetValue(c.StudentId, out var stu) ? stu.GithubUsername : "Unknown";
                var status = students.TryGetValue(c.StudentId, out var stu2) ? stu2.Status switch
                {
                    StudentStatus.Active => "✅",
                    StudentStatus.Inactive => "⚠️",
                    StudentStatus.Pending_Removal => "🔴",
                    _ => ""
                } : "";

                sb.AppendLine($"{medal} `{name}` {status}");
                sb.AppendLine(Loc.Fmt("top_contributions", lang, c.TotalContributions, c.ActiveDays, c.MaxDay));
            }

            var totalContribs = topContributors.Sum(x => x.TotalContributions);
            var avgContribs = topContributors.Average(x => x.TotalContributions);
            sb.AppendLine(Loc.Fmt("top_combined", lang, $"{totalContribs:N0}", $"{avgContribs:F1}"));

            await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating top contributors");
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /summary ====================

    private async Task SendDetailedSummaryAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        try
        {
            await bot.SendMessage(chatId, Loc.Get("summary_generating", lang), cancellationToken: ct);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var total = await db.Students.CountAsync(ct);
            var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
            var inactive = await db.Students.CountAsync(s => s.Status == StudentStatus.Inactive, ct);
            var pending = await db.Students.CountAsync(s => s.Status == StudentStatus.Pending_Removal, ct);

            var now = DateTime.UtcNow;
            var since7d = DateOnly.FromDateTime(now.AddDays(-7));
            var since30d = DateOnly.FromDateTime(now.AddDays(-30));

            var contribs7d = await db.DailyContributions.Where(dc => dc.Date >= since7d).SumAsync(dc => dc.Count, ct);
            var contribs30d = await db.DailyContributions.Where(dc => dc.Date >= since30d).SumAsync(dc => dc.Count, ct);

            var uniqueActive7d = await db.DailyContributions
                .Where(dc => dc.Date >= since7d && dc.Count > 0)
                .Select(dc => dc.StudentId).Distinct().CountAsync(ct);

            var uniqueActive30d = await db.DailyContributions
                .Where(dc => dc.Date >= since30d && dc.Count > 0)
                .Select(dc => dc.StudentId).Distinct().CountAsync(ct);

            var topWeek = await db.DailyContributions
                .Where(dc => dc.Date >= since7d)
                .GroupBy(dc => dc.StudentId)
                .Select(g => new { StudentId = g.Key, Total = g.Sum(x => x.Count) })
                .OrderByDescending(x => x.Total)
                .FirstOrDefaultAsync(ct);

            string topWeekName = "N/A";
            int topWeekCount = 0;
            if (topWeek is not null)
            {
                var topStudent = await db.Students.FindAsync([topWeek.StudentId], ct);
                topWeekName = topStudent?.GithubUsername ?? "Unknown";
                topWeekCount = topWeek.Total;
            }

            double avgPerStudent = uniqueActive30d > 0 ? (double)contribs30d / uniqueActive30d : 0;
            double utilization = total > 0 ? (double)active / total * 100 : 0;

            var sb = new StringBuilder();
            sb.AppendLine(Loc.Get("summary_title", lang));

            sb.AppendLine(Loc.Get("summary_students", lang));
            sb.AppendLine($"  Total: {total:N0}");
            sb.AppendLine($"  ✅ {active:N0} ({(total > 0 ? (double)active / total * 100 : 0):F1}%)");
            sb.AppendLine($"  ⚠️ {inactive:N0} ({(total > 0 ? (double)inactive / total * 100 : 0):F1}%)");
            sb.AppendLine($"  🔴 {pending:N0} ({(total > 0 ? (double)pending / total * 100 : 0):F1}%)");

            sb.AppendLine();
            sb.AppendLine(Loc.Get("summary_contribs", lang));
            sb.AppendLine(Loc.Fmt("summary_last7d", lang, $"{contribs7d:N0}"));
            sb.AppendLine(Loc.Fmt("summary_last30d", lang, $"{contribs30d:N0}"));
            sb.AppendLine($"  Active (7d): {uniqueActive7d:N0}");
            sb.AppendLine($"  Active (30d): {uniqueActive30d:N0}");
            sb.AppendLine($"  Avg/student (30d): {avgPerStudent:F1}");

            sb.AppendLine();
            sb.AppendLine(Loc.Get("summary_top_week", lang));
            sb.AppendLine($"  `{topWeekName}` — {topWeekCount:N0}");

            sb.AppendLine();
            sb.AppendLine(Loc.Get("summary_license", lang));
            sb.AppendLine(Loc.Fmt("summary_utilization", lang, $"{utilization:F1}"));
            sb.AppendLine(Loc.Fmt("summary_at_risk", lang, inactive + pending));
            sb.AppendLine($"  {Loc.RiskLevel(utilization, lang)}");

            sb.AppendLine($"\n🕐 _{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC_");

            await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating detailed summary");
            await bot.SendMessage(chatId, Loc.Get("summary_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /charts — Interactive Menu ====================

    private async Task SendChartsMenuAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            // Row 1: Core charts
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Activity", "charts_activity"),
                InlineKeyboardButton.WithCallbackData("📈 Trend", "charts_trend"),
                InlineKeyboardButton.WithCallbackData("🥧 Status", "charts_pro"),
            },
            // Row 2: Distribution & Analysis
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Distribution", "charts_dist"),
                InlineKeyboardButton.WithCallbackData("🔥 Heatmap", "charts_heatmap"),
                InlineKeyboardButton.WithCallbackData("📈 Area", "charts_area"),
            },
            // Row 3: Advanced
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔵 Scatter", "charts_scatter"),
                InlineKeyboardButton.WithCallbackData("⚡ Gauge", "charts_gauge"),
                InlineKeyboardButton.WithCallbackData("💧 Waterfall", "charts_waterfall"),
            },
            // Row 4: Engagement
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔻 Funnel", "charts_funnel"),
                InlineKeyboardButton.WithCallbackData("🏆 Top", "charts_top"),
                InlineKeyboardButton.WithCallbackData("📊 Stacked", "charts_stacked"),
            },
            // Row 5: Patterns
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Weekly", "charts_weekly"),
                InlineKeyboardButton.WithCallbackData("📅 Day-of-Week", "charts_dayofweek"),
            },
        });

        var text = Loc.Get("charts_menu_title", lang);
        await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task SendChartPeriodMenuAsync(ITelegramBotClient bot, long chatId, string chartType, string lang, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("24h", $"charts_{chartType}_1d"),
                InlineKeyboardButton.WithCallbackData("7 Days", $"charts_{chartType}_7d"),
                InlineKeyboardButton.WithCallbackData("30 Days", $"charts_{chartType}_30d"),
            }
        });

        await bot.SendMessage(chatId, $"📊 *Select period:*", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
    }

    // ==================== Generic Snapshot Chart Sender ====================

    private async Task SendSnapshotChartAsync(ITelegramBotClient bot, long chatId, string chartType, string[] parts, string lang, CancellationToken ct)
    {
        int days = ParseDays(parts);
        var period = Loc.PeriodLabel(days, lang);
        var captionKey = $"vis_{chartType}_caption";

        string caption;
        try { caption = Loc.Fmt(captionKey, lang, period); }
        catch { caption = $"📊 {chartType} — {period}"; }

        await bot.SendMessage(chatId, $"📊 {caption}...", cancellationToken: ct);

        try
        {
            var snapshotBytes = _plotService.GetSnapshot($"{chartType}_{days}d");
            if (snapshotBytes is not null)
            {
                using var ms = new MemoryStream(snapshotBytes);
                await bot.SendPhoto(chatId, InputFile.FromStream(ms, $"{chartType}_{days}d.png"),
                    caption: $"{caption} {Loc.Get("cached_snapshot", lang)}", cancellationToken: ct);
                return;
            }

            // Fallback: generate on the fly if no snapshot
            await bot.SendMessage(chatId,
                $"⚠️ No cached snapshot for `{chartType}` ({period}). Run /sync\\_now first.",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {ChartType} chart", chartType);
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /report — Full visual report ====================

    private async Task SendFullReportAsync(ITelegramBotClient bot, long chatId, string[] parts, string lang, CancellationToken ct)
    {
        int days = ParseDays(parts);
        var period = Loc.PeriodLabel(days, lang);
        await bot.SendMessage(chatId, Loc.Fmt("report_generating", lang, period), cancellationToken: ct);

        try
        {
            var chartTypes = new[] { "activity", "trend", "pro", "dist", "heatmap", "area", "gauge", "stacked", "scatter", "funnel", "top", "waterfall", "weekly", "dayofweek" };
            var photos = new List<InputMediaPhoto>();

            foreach (var chartType in chartTypes)
            {
                var snapshotBytes = _plotService.GetSnapshot($"{chartType}_{days}d");
                if (snapshotBytes is not null)
                {
                    var stream = new MemoryStream(snapshotBytes);
                    var media = new InputMediaPhoto(InputFile.FromStream(stream, $"{chartType}_{days}d.png"));
                    if (photos.Count == 0)
                        media.Caption = $"📊 {Loc.Fmt("report_caption", lang, period)}\n🕐 {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
                    photos.Add(media);
                }
            }

            if (photos.Count == 0)
            {
                await bot.SendMessage(chatId, "⚠️ No cached charts available. Run /sync\\_now first.", parseMode: ParseMode.Markdown, cancellationToken: ct);
                return;
            }

            // Telegram allows max 10 photos per album
            for (int i = 0; i < photos.Count; i += 10)
            {
                var batch = photos.Skip(i).Take(10).ToList();
                await bot.SendMediaGroup(chatId, batch, cancellationToken: ct);
            }

            await bot.SendMessage(chatId,
                $"✅ {Loc.Fmt("report_done", lang, photos.Count, period)}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating full report");
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== /export — Enhanced Export ====================

    private async Task SendEnhancedExportAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        try
        {
            await bot.SendMessage(chatId, Loc.Get("export_generating", lang), cancellationToken: ct);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var since30 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
            var since7 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));

            // Get all students with contribution aggregates
            var students = await db.Students.OrderBy(s => s.GithubUsername).ToListAsync(ct);
            var contributions30d = await db.DailyContributions
                .Where(dc => dc.Date >= since30)
                .GroupBy(dc => dc.StudentId)
                .Select(g => new
                {
                    StudentId = g.Key,
                    Total30d = g.Sum(x => x.Count),
                    ActiveDays30d = g.Count(x => x.Count > 0),
                    MaxDay30d = g.Max(x => x.Count),
                    LastContribDate = g.Max(x => x.Date)
                })
                .ToDictionaryAsync(x => x.StudentId, ct);

            var contributions7d = await db.DailyContributions
                .Where(dc => dc.Date >= since7)
                .GroupBy(dc => dc.StudentId)
                .Select(g => new { StudentId = g.Key, Total7d = g.Sum(x => x.Count), ActiveDays7d = g.Count(x => x.Count > 0) })
                .ToDictionaryAsync(x => x.StudentId, ct);

            var rows = students.Select(s =>
            {
                var c30 = contributions30d.GetValueOrDefault(s.Id);
                var c7 = contributions7d.GetValueOrDefault(s.Id);
                return new EnhancedExportRow
                {
                    UniversityId = s.UniversityId,
                    GithubUsername = s.GithubUsername,
                    Email = s.Email,
                    Status = s.Status.ToString(),
                    ContributionsLast7d = c7?.Total7d ?? 0,
                    ActiveDaysLast7d = c7?.ActiveDays7d ?? 0,
                    ContributionsLast30d = c30?.Total30d ?? 0,
                    ActiveDaysLast30d = c30?.ActiveDays30d ?? 0,
                    MaxDayContributions = c30?.MaxDay30d ?? 0,
                    LastContributionDate = c30 != null ? c30.LastContribDate.ToString("yyyy-MM-dd") : "N/A",
                    LastActiveDate = s.LastActiveDate?.ToString("yyyy-MM-dd HH:mm") ?? "N/A",
                    RegisteredDate = s.CreatedAt.ToString("yyyy-MM-dd"),
                    DownloadedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };
            }).ToList();

            using var memoryStream = new MemoryStream();
            using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(rows);
            }

            memoryStream.Position = 0;
            var active = students.Count(s => s.Status == StudentStatus.Active);
            var inactive = students.Count(s => s.Status == StudentStatus.Inactive);
            var pending = students.Count(s => s.Status == StudentStatus.Pending_Removal);
            var fileName = $"github_activity_report_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
            var document = InputFile.FromStream(memoryStream, fileName);

            var captionText = $"📊 *GitHub Activity Report*\n" +
                              $"📅 Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n" +
                              $"👥 Total: {students.Count:N0} | ✅ {active} | ⚠️ {inactive} | 🔴 {pending}\n" +
                              $"📈 Includes: 7d & 30d contributions, active days, peak day, last activity";

            await bot.SendDocument(chatId, document, caption: captionText, parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating enhanced export");
            await bot.SendMessage(chatId, Loc.Get("vis_error", lang), cancellationToken: ct);
        }
    }

    // ==================== Head Admin Commands ====================

    private async Task HandleAddAdminAsync(ITelegramBotClient bot, long chatId, string[] parts, string lang, CancellationToken ct)
    {
        if (parts.Length < 2 || !long.TryParse(parts[1], out var targetChatId))
        {
            await bot.SendMessage(chatId, Loc.Get("add_admin_usage", lang), parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var target = await db.BotUsers.FindAsync([targetChatId], ct);
        if (target is null)
        {
            // Create user entry with Admin role
            target = new BotUser
            {
                ChatId = targetChatId,
                Role = BotUserRole.Admin,
                Language = "en",
                CreatedAt = DateTime.UtcNow
            };
            db.BotUsers.Add(target);
        }
        else
        {
            target.Role = BotUserRole.Admin;
        }

        await db.SaveChangesAsync(ct);

        await bot.SendMessage(chatId,
            Loc.Fmt("admin_added", lang, target.Username ?? "?", targetChatId),
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task HandleRemoveAdminAsync(ITelegramBotClient bot, long chatId, string[] parts, string lang, CancellationToken ct)
    {
        if (parts.Length < 2 || !long.TryParse(parts[1], out var targetChatId))
        {
            await bot.SendMessage(chatId, Loc.Get("remove_admin_usage", lang), parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var target = await db.BotUsers.FindAsync([targetChatId], ct);
        if (target is not null)
        {
            target.Role = BotUserRole.Student;
            await db.SaveChangesAsync(ct);
        }

        await bot.SendMessage(chatId,
            Loc.Fmt("admin_removed", lang, targetChatId),
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task HandleListAdminsAsync(ITelegramBotClient bot, long chatId, string lang, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admins = await db.BotUsers
            .Where(u => u.Role == BotUserRole.Admin || u.Role == BotUserRole.Head)
            .OrderBy(u => u.Role)
            .ThenBy(u => u.CreatedAt)
            .ToListAsync(ct);

        if (admins.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.Get("admin_no_admins", lang), parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(Loc.Get("admin_list_title", lang));

        foreach (var admin in admins)
        {
            var roleIcon = admin.Role == BotUserRole.Head ? "👑" : "🛡️";
            sb.AppendLine($"{roleIcon} `{admin.Username ?? "?"}` — `{admin.ChatId}` ({admin.Role})");
        }

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, cancellationToken: ct);
    }
}

// ==================== CSV Models ====================

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
    public string DownloadedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
}

/// <summary>
/// Enhanced CSV row model for full activity export with contribution analytics.
/// </summary>
public class EnhancedExportRow
{
    [Name("university_id")]
    public string UniversityId { get; set; } = string.Empty;

    [Name("github_username")]
    public string GithubUsername { get; set; } = string.Empty;

    [Name("email")]
    public string Email { get; set; } = string.Empty;

    [Name("status")]
    public string Status { get; set; } = string.Empty;

    [Name("contributions_7d")]
    public int ContributionsLast7d { get; set; }

    [Name("active_days_7d")]
    public int ActiveDaysLast7d { get; set; }

    [Name("contributions_30d")]
    public int ContributionsLast30d { get; set; }

    [Name("active_days_30d")]
    public int ActiveDaysLast30d { get; set; }

    [Name("max_day_contributions")]
    public int MaxDayContributions { get; set; }

    [Name("last_contribution_date")]
    public string LastContributionDate { get; set; } = string.Empty;

    [Name("last_active_date")]
    public string LastActiveDate { get; set; } = string.Empty;

    [Name("registered_date")]
    public string RegisteredDate { get; set; } = string.Empty;

    [Name("downloaded_at")]
    public string DownloadedAt { get; set; } = string.Empty;
}

/// <summary>
/// CSV row model for student CSV import.
/// Maps snake_case CSV columns: university_id, github_username, email
/// </summary>
public class StudentCsvImportRow
{
    [Name("university_id")]
    public string UniversityId { get; set; } = string.Empty;

    [Name("github_username")]
    public string GithubUsername { get; set; } = string.Empty;

    [Name("email")]
    public string Email { get; set; } = string.Empty;
}
