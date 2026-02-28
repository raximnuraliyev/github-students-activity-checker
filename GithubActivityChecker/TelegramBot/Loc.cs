namespace GithubActivityChecker.TelegramBot;

/// <summary>
/// Simple localization helper for Telegram bot responses.
/// Supports 3 languages: en (English), uz (O'zbek), ru (Русский).
/// </summary>
public static class Loc
{
    public static string Get(string key, string lang) =>
        Strings.TryGetValue(key, out var dict) && dict.TryGetValue(lang, out var val) ? val : dict?["en"] ?? key;

    public static string Fmt(string key, string lang, params object[] args) =>
        string.Format(Get(key, lang), args);

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        // ── General ──
        ["unauthorized"] = new()
        {
            ["en"] = "⛔ Unauthorized. Your Chat ID is not in the authorized list.",
            ["uz"] = "⛔ Ruxsat berilmagan. Sizning Chat ID ruxsat berilganlar ro'yxatida emas.",
            ["ru"] = "⛔ Нет доступа. Ваш Chat ID не в списке авторизованных."
        },
        ["unknown_cmd"] = new()
        {
            ["en"] = "Unknown command. Use /help to see available commands.",
            ["uz"] = "Noma'lum buyruq. Mavjud buyruqlarni ko'rish uchun /help dan foydalaning.",
            ["ru"] = "Неизвестная команда. Используйте /help для списка команд."
        },
        ["error"] = new()
        {
            ["en"] = "❌ An error occurred processing your command.",
            ["uz"] = "❌ Buyruqni bajarishda xatolik yuz berdi.",
            ["ru"] = "❌ Произошла ошибка при обработке команды."
        },
        ["no_permission"] = new()
        {
            ["en"] = "🔒 You don't have permission to use this command. Only admins can use it.",
            ["uz"] = "🔒 Sizda bu buyruqni ishlatish huquqi yo'q. Faqat adminlar foydalana oladi.",
            ["ru"] = "🔒 У вас нет прав на эту команду. Только для администраторов."
        },
        ["head_only"] = new()
        {
            ["en"] = "🔒 This command is only available to the Head administrator.",
            ["uz"] = "🔒 Bu buyruq faqat Bosh administrator uchun.",
            ["ru"] = "🔒 Эта команда доступна только Главному администратору."
        },

        // ── /status ──
        ["status_title"] = new()
        {
            ["en"] = "📊 *Student License Status*",
            ["uz"] = "📊 *Talabalar litsenziya holati*",
            ["ru"] = "📊 *Статус лицензий студентов*"
        },
        ["status_total"] = new()
        {
            ["en"] = "Total: {0}",
            ["uz"] = "Jami: {0}",
            ["ru"] = "Всего: {0}"
        },
        ["status_active"] = new()
        {
            ["en"] = "✅ Active: {0}",
            ["uz"] = "✅ Faol: {0}",
            ["ru"] = "✅ Активные: {0}"
        },
        ["status_inactive"] = new()
        {
            ["en"] = "⚠️ Inactive (30d): {0}",
            ["uz"] = "⚠️ Nofaol (30k): {0}",
            ["ru"] = "⚠️ Неактивные (30д): {0}"
        },
        ["status_pending"] = new()
        {
            ["en"] = "🔴 Pending Removal (60d): {0}",
            ["uz"] = "🔴 O'chirish kutilmoqda (60k): {0}",
            ["ru"] = "🔴 Ожидают удаления (60д): {0}"
        },

        // ── /list_inactive ──
        ["no_inactive"] = new()
        {
            ["en"] = "✅ No inactive students found!",
            ["uz"] = "✅ Nofaol talabalar topilmadi!",
            ["ru"] = "✅ Неактивных студентов не найдено!"
        },
        ["inactive_caption"] = new()
        {
            ["en"] = "📄 {0} inactive students as of {1}",
            ["uz"] = "📄 {1} holatiga ko'ra {0} ta nofaol talaba",
            ["ru"] = "📄 {0} неактивных студентов на {1}"
        },

        // ── /check ──
        ["check_usage"] = new()
        {
            ["en"] = "Usage: /check [github_username]",
            ["uz"] = "Foydalanish: /check [github_username]",
            ["ru"] = "Использование: /check [github_username]"
        },
        ["check_fetching"] = new()
        {
            ["en"] = "🔍 Fetching real-time data for *{0}*...",
            ["uz"] = "🔍 *{0}* uchun real vaqt ma'lumotlari olinmoqda...",
            ["ru"] = "🔍 Получение данных в реальном времени для *{0}*..."
        },
        ["check_error"] = new()
        {
            ["en"] = "❌ Could not fetch data for `{0}`. Check the username or API token.",
            ["uz"] = "❌ `{0}` uchun ma'lumot olinmadi. Username yoki API tokenni tekshiring.",
            ["ru"] = "❌ Не удалось получить данные для `{0}`. Проверьте имя пользователя или API токен."
        },
        ["check_result_title"] = new()
        {
            ["en"] = "🔎 *Real-time check: {0}*",
            ["uz"] = "🔎 *Real vaqt tekshiruvi: {0}*",
            ["ru"] = "🔎 *Проверка в реальном времени: {0}*"
        },
        ["check_total_year"] = new()
        {
            ["en"] = "Total contributions (year): {0}",
            ["uz"] = "Yillik hissalar: {0}",
            ["ru"] = "Всего за год: {0}"
        },
        ["check_last7"] = new()
        {
            ["en"] = "Last 7 days: {0}",
            ["uz"] = "Oxirgi 7 kun: {0}",
            ["ru"] = "Последние 7 дней: {0}"
        },
        ["check_last30"] = new()
        {
            ["en"] = "Last 30 days: {0}",
            ["uz"] = "Oxirgi 30 kun: {0}",
            ["ru"] = "Последние 30 дней: {0}"
        },
        ["check_active_days"] = new()
        {
            ["en"] = "Active days this year: {0}",
            ["uz"] = "Shu yildagi faol kunlar: {0}",
            ["ru"] = "Активных дней в этом году: {0}"
        },

        // ── /sync_now ──
        ["sync_start"] = new()
        {
            ["en"] = "🔄 Starting manual sync... This may take a few minutes.",
            ["uz"] = "🔄 Qo'lda sinxronizatsiya boshlanmoqda... Bir necha daqiqa olishi mumkin.",
            ["ru"] = "🔄 Запуск ручной синхронизации... Это может занять несколько минут."
        },
        ["sync_already"] = new()
        {
            ["en"] = "⏳ A sync is already in progress. Please wait.",
            ["uz"] = "⏳ Sinxronizatsiya allaqachon davom etmoqda. Iltimos kuting.",
            ["ru"] = "⏳ Синхронизация уже выполняется. Пожалуйста, подождите."
        },
        ["sync_done"] = new()
        {
            ["en"] = "✅ Manual sync completed!",
            ["uz"] = "✅ Qo'lda sinxronizatsiya yakunlandi!",
            ["ru"] = "✅ Ручная синхронизация завершена!"
        },
        ["sync_error"] = new()
        {
            ["en"] = "❌ Sync failed: {0}",
            ["uz"] = "❌ Sinxronizatsiya xatosi: {0}",
            ["ru"] = "❌ Ошибка синхронизации: {0}"
        },

        // ── Visualization ──
        ["vis_activity_caption"] = new()
        {
            ["en"] = "📊 Daily Activity — Last {0}",
            ["uz"] = "📊 Kunlik faollik — Oxirgi {0}",
            ["ru"] = "📊 Ежедневная активность — Последние {0}"
        },
        ["vis_dist_caption"] = new()
        {
            ["en"] = "📊 Contribution Distribution — Last {0}",
            ["uz"] = "📊 Hissalar taqsimoti — Oxirgi {0}",
            ["ru"] = "📊 Распределение вкладов — Последние {0}"
        },
        ["vis_trend_caption"] = new()
        {
            ["en"] = "📈 Usage Trend — Last {0}",
            ["uz"] = "📈 Foydalanish trendi — Oxirgi {0}",
            ["ru"] = "📈 Тренд использования — Последние {0}"
        },
        ["vis_pro_caption"] = new()
        {
            ["en"] = "🥧 Pro License Status — {0} Students",
            ["uz"] = "🥧 Pro litsenziya holati — {0} ta talaba",
            ["ru"] = "🥧 Статус Pro лицензий — {0} студентов"
        },
        ["vis_error"] = new()
        {
            ["en"] = "❌ Failed to generate chart.",
            ["uz"] = "❌ Diagramma yaratib bo'lmadi.",
            ["ru"] = "❌ Не удалось создать график."
        },
        ["cached_snapshot"] = new()
        {
            ["en"] = "(cached snapshot)",
            ["uz"] = "(keshdan olingan)",
            ["ru"] = "(из кеша)"
        },

        // ── /top ──
        ["top_title"] = new()
        {
            ["en"] = "🏆 *Top {0} Contributors (Last 30 Days)*\n",
            ["uz"] = "🏆 *Top {0} kontributor (Oxirgi 30 kun)*\n",
            ["ru"] = "🏆 *Топ {0} участников (Последние 30 дней)*\n"
        },
        ["top_contributions"] = new()
        {
            ["en"] = "    📊 {0} contributions · {1} active days · Peak: {2}/day",
            ["uz"] = "    📊 {0} hissa · {1} faol kun · Eng ko'p: {2}/kun",
            ["ru"] = "    📊 {0} вкладов · {1} активных дней · Пик: {2}/день"
        },
        ["top_combined"] = new()
        {
            ["en"] = "\n📈 Combined: {0} contributions\n📊 Average: {1} per student",
            ["uz"] = "\n📈 Jami: {0} hissa\n📊 O'rtacha: {1} har bir talaba uchun",
            ["ru"] = "\n📈 Всего: {0} вкладов\n📊 Среднее: {1} на студента"
        },
        ["top_no_data"] = new()
        {
            ["en"] = "📊 No contribution data available yet. Run /sync\\_now first.",
            ["uz"] = "📊 Hissa ma'lumotlari hali mavjud emas. Avval /sync\\_now ni ishga tushiring.",
            ["ru"] = "📊 Данных о вкладах пока нет. Сначала запустите /sync\\_now."
        },

        // ── /summary ──
        ["summary_generating"] = new()
        {
            ["en"] = "📊 Generating detailed summary...",
            ["uz"] = "📊 Batafsil hisobot tayyorlanmoqda...",
            ["ru"] = "📊 Формирование подробного отчёта..."
        },
        ["summary_title"] = new()
        {
            ["en"] = "📊 *Detailed Analytics Summary*\n",
            ["uz"] = "📊 *Batafsil analitika hisoboti*\n",
            ["ru"] = "📊 *Подробный аналитический отчёт*\n"
        },
        ["summary_students"] = new()
        {
            ["en"] = "👥 *Student Overview*",
            ["uz"] = "👥 *Talabalar ko'rinishi*",
            ["ru"] = "👥 *Обзор студентов*"
        },
        ["summary_contribs"] = new()
        {
            ["en"] = "📈 *Contribution Activity*",
            ["uz"] = "📈 *Hissa faolligi*",
            ["ru"] = "📈 *Активность вкладов*"
        },
        ["summary_top_week"] = new()
        {
            ["en"] = "🏆 *Top Contributor This Week*",
            ["uz"] = "🏆 *Shu haftaning eng faol kontributori*",
            ["ru"] = "🏆 *Лучший участник недели*"
        },
        ["summary_license"] = new()
        {
            ["en"] = "🔑 *License Utilization*",
            ["uz"] = "🔑 *Litsenziya foydalanish*",
            ["ru"] = "🔑 *Использование лицензий*"
        },
        ["summary_last7d"] = new()
        {
            ["en"] = "  Last 7 days: {0} contributions",
            ["uz"] = "  Oxirgi 7 kun: {0} hissa",
            ["ru"] = "  Последние 7 дней: {0} вкладов"
        },
        ["summary_last30d"] = new()
        {
            ["en"] = "  Last 30 days: {0} contributions",
            ["uz"] = "  Oxirgi 30 kun: {0} hissa",
            ["ru"] = "  Последние 30 дней: {0} вкладов"
        },
        ["summary_utilization"] = new()
        {
            ["en"] = "  Utilization Rate: {0}%",
            ["uz"] = "  Foydalanish darajasi: {0}%",
            ["ru"] = "  Уровень использования: {0}%"
        },
        ["summary_at_risk"] = new()
        {
            ["en"] = "  Licenses at risk: {0}",
            ["uz"] = "  Xavf ostidagi litsenziyalar: {0}",
            ["ru"] = "  Лицензий под угрозой: {0}"
        },
        ["summary_error"] = new()
        {
            ["en"] = "❌ Failed to generate summary.",
            ["uz"] = "❌ Hisobot yaratib bo'lmadi.",
            ["ru"] = "❌ Не удалось создать отчёт."
        },

        // ── /import ──
        ["import_title"] = new()
        {
            ["en"] = "📥 *Import Data*\n\nChoose the target table to import into:",
            ["uz"] = "📥 *Ma'lumot import qilish*\n\nImport qilinadigan jadvalni tanlang:",
            ["ru"] = "📥 *Импорт данных*\n\nВыберите целевую таблицу:"
        },
        ["import_students_btn"] = new()
        {
            ["en"] = "📋 Students",
            ["uz"] = "📋 Talabalar",
            ["ru"] = "📋 Студенты"
        },
        ["import_cancel_btn"] = new()
        {
            ["en"] = "❌ Cancel",
            ["uz"] = "❌ Bekor qilish",
            ["ru"] = "❌ Отмена"
        },
        ["import_send_csv"] = new()
        {
            ["en"] = "📥 *Send a CSV file now* to import students.\n\n" +
                     "Required columns: `university_id`, `github_username`, `email`\n\n" +
                     "Example:\n```\nuniversity_id,github_username,email\nSTU001,johndoe,john@uni.edu\nSTU002,janedoe,jane@uni.edu\n```",
            ["uz"] = "📥 Talabalarni import qilish uchun *CSV faylni hozir yuboring*.\n\n" +
                     "Kerakli ustunlar: `university_id`, `github_username`, `email`\n\n" +
                     "Misol:\n```\nuniversity_id,github_username,email\nSTU001,johndoe,john@uni.edu\nSTU002,janedoe,jane@uni.edu\n```",
            ["ru"] = "📥 *Отправьте CSV файл* для импорта студентов.\n\n" +
                     "Обязательные столбцы: `university_id`, `github_username`, `email`\n\n" +
                     "Пример:\n```\nuniversity_id,github_username,email\nSTU001,johndoe,john@uni.edu\nSTU002,janedoe,jane@uni.edu\n```"
        },
        ["import_cancelled"] = new()
        {
            ["en"] = "❌ Import cancelled.",
            ["uz"] = "❌ Import bekor qilindi.",
            ["ru"] = "❌ Импорт отменён."
        },
        ["import_no_pending"] = new()
        {
            ["en"] = "📎 You sent a file, but no import is pending.\nUse /import first to choose a target table.",
            ["uz"] = "📎 Siz fayl yubordingiz, lekin import kutilmayapti.\nAvval /import buyrug'idan foydalaning.",
            ["ru"] = "📎 Вы отправили файл, но импорт не ожидается.\nСначала используйте /import."
        },
        ["import_csv_only"] = new()
        {
            ["en"] = "❌ Only CSV files are supported. Please send a `.csv` file.",
            ["uz"] = "❌ Faqat CSV fayllar qo'llab-quvvatlanadi. `.csv` fayl yuboring.",
            ["ru"] = "❌ Поддерживаются только CSV файлы. Отправьте `.csv` файл."
        },
        ["import_processing"] = new()
        {
            ["en"] = "⏳ Processing CSV for *{0}* table...",
            ["uz"] = "⏳ *{0}* jadvali uchun CSV fayl qayta ishlanmoqda...",
            ["ru"] = "⏳ Обработка CSV для таблицы *{0}*..."
        },
        ["import_complete"] = new()
        {
            ["en"] = "✅ *CSV Import Complete*",
            ["uz"] = "✅ *CSV import yakunlandi*",
            ["ru"] = "✅ *Импорт CSV завершён*"
        },
        ["import_total_rows"] = new()
        {
            ["en"] = "📊 Total rows: {0}",
            ["uz"] = "📊 Jami qatorlar: {0}",
            ["ru"] = "📊 Всего строк: {0}"
        },
        ["import_added"] = new()
        {
            ["en"] = "➕ Added: {0}",
            ["uz"] = "➕ Qo'shildi: {0}",
            ["ru"] = "➕ Добавлено: {0}"
        },
        ["import_updated"] = new()
        {
            ["en"] = "🔄 Updated: {0}",
            ["uz"] = "🔄 Yangilandi: {0}",
            ["ru"] = "🔄 Обновлено: {0}"
        },
        ["import_skipped"] = new()
        {
            ["en"] = "⏭️ Skipped: {0}",
            ["uz"] = "⏭️ O'tkazildi: {0}",
            ["ru"] = "⏭️ Пропущено: {0}"
        },
        ["import_csv_error"] = new()
        {
            ["en"] = "❌ CSV parsing failed: {0}\n\nMake sure columns are: `university_id`, `github_username`, `email`",
            ["uz"] = "❌ CSV tahlili muvaffaqiyatsiz: {0}\n\nUstunlar: `university_id`, `github_username`, `email` bo'lishi kerak",
            ["ru"] = "❌ Ошибка разбора CSV: {0}\n\nУбедитесь, что столбцы: `university_id`, `github_username`, `email`"
        },
        ["import_empty"] = new()
        {
            ["en"] = "⚠️ CSV file is empty or has no valid rows.",
            ["uz"] = "⚠️ CSV fayl bo'sh yoki yaroqli qatorlar yo'q.",
            ["ru"] = "⚠️ CSV файл пуст или не содержит валидных строк."
        },

        // ── Admin management (Head only) ──
        ["admin_added"] = new()
        {
            ["en"] = "✅ User *{0}* (Chat ID: `{1}`) has been promoted to *Admin*.",
            ["uz"] = "✅ Foydalanuvchi *{0}* (Chat ID: `{1}`) *Admin* darajasiga ko'tarildi.",
            ["ru"] = "✅ Пользователь *{0}* (Chat ID: `{1}`) назначен *Админом*."
        },
        ["admin_removed"] = new()
        {
            ["en"] = "✅ User (Chat ID: `{0}`) has been demoted to *Student*.",
            ["uz"] = "✅ Foydalanuvchi (Chat ID: `{0}`) *Talaba* darajasiga tushirildi.",
            ["ru"] = "✅ Пользователь (Chat ID: `{0}`) понижен до *Студента*."
        },
        ["admin_list_title"] = new()
        {
            ["en"] = "👥 *Bot Administrators*\n",
            ["uz"] = "👥 *Bot administratorlari*\n",
            ["ru"] = "👥 *Администраторы бота*\n"
        },
        ["admin_no_admins"] = new()
        {
            ["en"] = "No administrators set. Use /add\\_admin to add one.",
            ["uz"] = "Administratorlar belgilanmagan. /add\\_admin orqali qo'shing.",
            ["ru"] = "Администраторов нет. Используйте /add\\_admin для добавления."
        },
        ["add_admin_usage"] = new()
        {
            ["en"] = "Usage: /add\\_admin [chat\\_id]\nForward a message from the user first to get their Chat ID.",
            ["uz"] = "Foydalanish: /add\\_admin [chat\\_id]\nFoydalanuvchining Chat ID sini olish uchun uning xabarini forward qiling.",
            ["ru"] = "Использование: /add\\_admin [chat\\_id]\nПерешлите сообщение от пользователя для получения Chat ID."
        },
        ["remove_admin_usage"] = new()
        {
            ["en"] = "Usage: /remove\\_admin [chat\\_id]",
            ["uz"] = "Foydalanish: /remove\\_admin [chat\\_id]",
            ["ru"] = "Использование: /remove\\_admin [chat\\_id]"
        },

        // ── /help ──
        ["help_student"] = new()
        {
            ["en"] = """
                🤖 *GitHub Activity Monitor — Commands*

                📋 *Available Commands*
                /start — Welcome \+ language selection
                /help — Show this message
                /check \[username\] — Check a GitHub user's activity
                """,
            ["uz"] = """
                🤖 *GitHub Faoliyat Monitori — Buyruqlar*

                📋 *Mavjud buyruqlar*
                /start — Xush kelibsiz \+ til tanlash
                /help — Ushbu xabarni ko'rsatish
                /check \[username\] — GitHub foydalanuvchi faolligini tekshirish
                """,
            ["ru"] = """
                🤖 *GitHub Activity Monitor — Команды*

                📋 *Доступные команды*
                /start — Приветствие \+ выбор языка
                /help — Показать это сообщение
                /check \[username\] — Проверить активность пользователя GitHub
                """
        },
        ["help_admin"] = new()
        {
            ["en"] = """
                🤖 *GitHub Activity Monitor — Admin Commands*

                📋 *Core Commands*
                /status — Summary of all student statuses
                /list\_inactive — Download CSV of inactive students
                /check \[username\] — Real\-time check for a specific student
                /sync\_now — Manually trigger a full sync

                📊 *Visualization* \(optional: 1d, 7d, 30d\)
                /vis\_activity \[period\] — Activity bar chart
                /vis\_dist \[period\] — Distribution histogram
                /vis\_trend \[period\] — Usage trend line
                /vis\_pro \[period\] — Active vs Inactive donut
                /vis\_heatmap \[period\] — Activity heatmap
                /vis\_area \[period\] — Cumulative area chart
                /vis\_scatter \[period\] — Student scatter map
                /vis\_gauge \[period\] — License KPI gauge
                /vis\_waterfall \[period\] — Period changes
                /vis\_funnel \[period\] — Engagement funnel
                /vis\_top \[period\] — Top contributors chart
                /vis\_weekly \[period\] — Weekly comparison
                /vis\_dayofweek \[period\] — Day\-of\-week patterns
                /vis\_stacked \[period\] — Stacked status bars
                /charts — Interactive chart gallery

                📈 *Analytics \& Reports*
                /top \[N\] — Top N contributors \(default: 10\)
                /summary — Detailed analytics report
                /report \[period\] — Full visual report \(all charts\)
                /export — Enhanced CSV with analytics

                📥 *Import*
                /import — Import students from a CSV file

                /start — Welcome \+ language selection
                /help — Show this message
                """,
            ["uz"] = """
                🤖 *GitHub Faoliyat Monitori — Admin buyruqlari*

                📋 *Asosiy buyruqlar*
                /status — Barcha talabalar holati
                /list\_inactive — Nofaol talabalar CSV fayli
                /check \[username\] — Real vaqtda tekshiruv
                /sync\_now — Qo'lda sinxronizatsiya

                📊 *Vizualizatsiya* \(ixtiyoriy: 1d, 7d, 30d\)
                /vis\_activity \[davr\] — Faollik diagrammasi
                /vis\_dist \[davr\] — Taqsimot histogrammasi
                /vis\_trend \[davr\] — Trend grafigi
                /vis\_pro \[davr\] — Faol/Nofaol donut
                /vis\_heatmap \[davr\] — Issiqlik xaritasi
                /vis\_area \[davr\] — Yig'ma maydon grafigi
                /vis\_scatter \[davr\] — Talabalar scatter xaritasi
                /vis\_gauge \[davr\] — Litsenziya KPI
                /vis\_waterfall \[davr\] — Davr o'zgarishlari
                /vis\_funnel \[davr\] — Jalb qilish voronkasi
                /vis\_top \[davr\] — Top kontributorlar
                /vis\_weekly \[davr\] — Haftalik taqqoslash
                /vis\_dayofweek \[davr\] — Hafta kunlari
                /vis\_stacked \[davr\] — Status bo'yicha
                /charts — Interaktiv diagramma galereyasi

                📈 *Analitika va hisobotlar*
                /top \[N\] — Eng faol N ta talaba \(standart: 10\)
                /summary — Batafsil analitika hisoboti
                /report \[davr\] — To'liq vizual hisobot
                /export — Kengaytirilgan CSV eksport

                📥 *Import*
                /import — CSV fayldan talabalarni import qilish

                /start — Xush kelibsiz \+ til tanlash
                /help — Ushbu xabarni ko'rsatish
                """,
            ["ru"] = """
                🤖 *GitHub Activity Monitor — Команды администратора*

                📋 *Основные команды*
                /status — Статистика студентов
                /list\_inactive — CSV неактивных студентов
                /check \[username\] — Проверка в реальном времени
                /sync\_now — Ручная синхронизация

                📊 *Визуализация* \(опционально: 1d, 7d, 30d\)
                /vis\_activity \[период\] — График активности
                /vis\_dist \[период\] — Гистограмма распределения
                /vis\_trend \[период\] — Линия тренда
                /vis\_pro \[период\] — Активные/Неактивные пончик
                /vis\_heatmap \[период\] — Тепловая карта
                /vis\_area \[период\] — Кумулятивная область
                /vis\_scatter \[период\] — Карта активности
                /vis\_gauge \[период\] — KPI лицензий
                /vis\_waterfall \[период\] — Изменения за период
                /vis\_funnel \[период\] — Воронка вовлечённости
                /vis\_top \[период\] — Лучшие участники
                /vis\_weekly \[период\] — Сравнение по неделям
                /vis\_dayofweek \[период\] — По дням недели
                /vis\_stacked \[период\] — По статусу
                /charts — Галерея визуализаций

                📈 *Аналитика и отчёты*
                /top \[N\] — Топ N участников \(по умолч\.: 10\)
                /summary — Подробный аналитический отчёт
                /report \[период\] — Полный визуальный отчёт
                /export — Расширенный CSV с аналитикой

                📥 *Импорт*
                /import — Импорт студентов из CSV

                /start — Приветствие \+ выбор языка
                /help — Показать это сообщение
                """
        },
        ["help_head"] = new()
        {
            ["en"] = """
                🤖 *GitHub Activity Monitor — Head Admin Commands*

                📋 *Core Commands*
                /status — Summary of all student statuses
                /list\_inactive — Download CSV of inactive students
                /check \[username\] — Real\-time check for a specific student
                /sync\_now — Manually trigger a full sync

                📊 *Visualization* \(optional: 1d, 7d, 30d\)
                /vis\_activity \[period\] — Activity bar chart
                /vis\_dist \[period\] — Distribution histogram
                /vis\_trend \[period\] — Usage trend line
                /vis\_pro \[period\] — Active vs Inactive donut
                /vis\_heatmap \[period\] — Activity heatmap
                /vis\_area \[period\] — Cumulative area chart
                /vis\_scatter \[period\] — Student scatter map
                /vis\_gauge \[period\] — License KPI gauge
                /vis\_waterfall \[period\] — Period changes
                /vis\_funnel \[period\] — Engagement funnel
                /vis\_top \[period\] — Top contributors chart
                /vis\_weekly \[period\] — Weekly comparison
                /vis\_dayofweek \[period\] — Day\-of\-week patterns
                /vis\_stacked \[period\] — Stacked status bars
                /charts — Interactive chart gallery

                📈 *Analytics \& Reports*
                /top \[N\] — Top N contributors \(default: 10\)
                /summary — Detailed analytics report
                /report \[period\] — Full visual report \(all charts\)
                /export — Enhanced CSV with analytics

                📥 *Import*
                /import — Import students from a CSV file

                👑 *Head Admin*
                /add\_admin \[chat\_id\] — Promote user to Admin
                /remove\_admin \[chat\_id\] — Demote admin to Student
                /list\_admins — Show all administrators

                /start — Welcome \+ language selection
                /help — Show this message
                """,
            ["uz"] = """
                🤖 *GitHub Faoliyat Monitori — Bosh Admin buyruqlari*

                📋 *Asosiy buyruqlar*
                /status — Barcha talabalar holati
                /list\_inactive — Nofaol talabalar CSV fayli
                /check \[username\] — Real vaqtda tekshiruv
                /sync\_now — Qo'lda sinxronizatsiya

                📊 *Vizualizatsiya* \(ixtiyoriy: 1d, 7d, 30d\)
                /vis\_activity \[davr\] — Faollik diagrammasi
                /vis\_dist \[davr\] — Taqsimot histogrammasi
                /vis\_trend \[davr\] — Trend grafigi
                /vis\_pro \[davr\] — Faol/Nofaol donut
                /vis\_heatmap \[davr\] — Issiqlik xaritasi
                /vis\_area \[davr\] — Yig'ma maydon grafigi
                /vis\_scatter \[davr\] — Talabalar scatter xaritasi
                /vis\_gauge \[davr\] — Litsenziya KPI
                /vis\_waterfall \[davr\] — Davr o'zgarishlari
                /vis\_funnel \[davr\] — Jalb qilish voronkasi
                /vis\_top \[davr\] — Top kontributorlar
                /vis\_weekly \[davr\] — Haftalik taqqoslash
                /vis\_dayofweek \[davr\] — Hafta kunlari
                /vis\_stacked \[davr\] — Status bo'yicha
                /charts — Interaktiv diagramma galereyasi

                📈 *Analitika va hisobotlar*
                /top \[N\] — Eng faol N ta talaba \(standart: 10\)
                /summary — Batafsil analitika hisoboti
                /report \[davr\] — To'liq vizual hisobot
                /export — Kengaytirilgan CSV eksport

                📥 *Import*
                /import — CSV fayldan talabalarni import qilish

                👑 *Bosh Admin*
                /add\_admin \[chat\_id\] — Foydalanuvchini Adminga ko'tarish
                /remove\_admin \[chat\_id\] — Adminni Talabaga tushirish
                /list\_admins — Barcha administratorlarni ko'rsatish

                /start — Xush kelibsiz \+ til tanlash
                /help — Ushbu xabarni ko'rsatish
                """,
            ["ru"] = """
                🤖 *GitHub Activity Monitor — Команды Главного администратора*

                📋 *Основные команды*
                /status — Статистика студентов
                /list\_inactive — CSV неактивных студентов
                /check \[username\] — Проверка в реальном времени
                /sync\_now — Ручная синхронизация

                📊 *Визуализация* \(опционально: 1d, 7d, 30d\)
                /vis\_activity \[период\] — График активности
                /vis\_dist \[период\] — Гистограмма распределения
                /vis\_trend \[период\] — Линия тренда
                /vis\_pro \[период\] — Активные/Неактивные пончик
                /vis\_heatmap \[период\] — Тепловая карта
                /vis\_area \[период\] — Кумулятивная область
                /vis\_scatter \[период\] — Карта активности
                /vis\_gauge \[период\] — KPI лицензий
                /vis\_waterfall \[период\] — Изменения за период
                /vis\_funnel \[период\] — Воронка вовлечённости
                /vis\_top \[период\] — Лучшие участники
                /vis\_weekly \[период\] — Сравнение по неделям
                /vis\_dayofweek \[период\] — По дням недели
                /vis\_stacked \[период\] — По статусу
                /charts — Галерея визуализаций

                📈 *Аналитика и отчёты*
                /top \[N\] — Топ N участников \(по умолч\.: 10\)
                /summary — Подробный аналитический отчёт
                /report \[период\] — Полный визуальный отчёт
                /export — Расширенный CSV с аналитикой

                📥 *Импорт*
                /import — Импорт студентов из CSV

                👑 *Главный администратор*
                /add\_admin \[chat\_id\] — Назначить пользователя Админом
                /remove\_admin \[chat\_id\] — Понизить админа до Студента
                /list\_admins — Показать всех администраторов

                /start — Приветствие \+ выбор языка
                /help — Показать это сообщение
                """
        },

        // ── Period labels ──
        ["period_24h"] = new()
        {
            ["en"] = "24h",
            ["uz"] = "24 soat",
            ["ru"] = "24ч"
        },
        ["period_7d"] = new()
        {
            ["en"] = "7 Days",
            ["uz"] = "7 Kun",
            ["ru"] = "7 Дней"
        },
        ["period_30d"] = new()
        {
            ["en"] = "30 Days",
            ["uz"] = "30 Kun",
            ["ru"] = "30 Дней"
        },

        // ── Risk levels ──
        ["risk_healthy"] = new()
        {
            ["en"] = "🟢 Healthy",
            ["uz"] = "🟢 Yaxshi",
            ["ru"] = "🟢 Здоровый"
        },
        ["risk_moderate"] = new()
        {
            ["en"] = "🟡 Moderate",
            ["uz"] = "🟡 O'rtacha",
            ["ru"] = "🟡 Умеренный"
        },
        ["risk_concerning"] = new()
        {
            ["en"] = "🟠 Concerning",
            ["uz"] = "🟠 Xavotirli",
            ["ru"] = "🟠 Тревожный"
        },
        ["risk_critical"] = new()
        {
            ["en"] = "🔴 Critical",
            ["uz"] = "🔴 Kritik",
            ["ru"] = "🔴 Критический"
        },

        // ── New Chart Captions ──
        ["vis_heatmap_caption"] = new()
        {
            ["en"] = "🔥 Activity Heatmap — {0}",
            ["uz"] = "🔥 Faollik issiqlik xaritasi — {0}",
            ["ru"] = "🔥 Тепловая карта активности — {0}"
        },
        ["vis_area_caption"] = new()
        {
            ["en"] = "📈 Cumulative Activity — {0}",
            ["uz"] = "📈 Yig'ma faollik — {0}",
            ["ru"] = "📈 Кумулятивная активность — {0}"
        },
        ["vis_scatter_caption"] = new()
        {
            ["en"] = "🔵 Student Activity Map — {0}",
            ["uz"] = "🔵 Talabalar faollik xaritasi — {0}",
            ["ru"] = "🔵 Карта активности студентов — {0}"
        },
        ["vis_gauge_caption"] = new()
        {
            ["en"] = "⚡ License Utilization KPI — {0}",
            ["uz"] = "⚡ Litsenziya foydalanish KPI — {0}",
            ["ru"] = "⚡ KPI использования лицензий — {0}"
        },
        ["vis_waterfall_caption"] = new()
        {
            ["en"] = "💧 Period Changes — {0}",
            ["uz"] = "💧 Davr o'zgarishlari — {0}",
            ["ru"] = "💧 Изменения за период — {0}"
        },
        ["vis_funnel_caption"] = new()
        {
            ["en"] = "🔻 Engagement Funnel — {0}",
            ["uz"] = "🔻 Jalb qilish voronkasi — {0}",
            ["ru"] = "🔻 Воронка вовлечённости — {0}"
        },
        ["vis_top_caption"] = new()
        {
            ["en"] = "🏆 Top Contributors — {0}",
            ["uz"] = "🏆 Eng faol kontributorlar — {0}",
            ["ru"] = "🏆 Лучшие участники — {0}"
        },
        ["vis_weekly_caption"] = new()
        {
            ["en"] = "📊 Weekly Comparison — {0}",
            ["uz"] = "📊 Haftalik taqqoslash — {0}",
            ["ru"] = "📊 Сравнение по неделям — {0}"
        },
        ["vis_dayofweek_caption"] = new()
        {
            ["en"] = "📅 Day-of-Week Patterns — {0}",
            ["uz"] = "📅 Hafta kunlari bo'yicha faollik — {0}",
            ["ru"] = "📅 Активность по дням недели — {0}"
        },
        ["vis_stacked_caption"] = new()
        {
            ["en"] = "📊 Contributions by Status — {0}",
            ["uz"] = "📊 Status bo'yicha hissalar — {0}",
            ["ru"] = "📊 Вклады по статусу — {0}"
        },

        // ── /charts menu ──
        ["charts_menu_title"] = new()
        {
            ["en"] = "📊 *Visualization Gallery*\nChoose a chart type:",
            ["uz"] = "📊 *Vizualizatsiya galereyasi*\nDiagramma turini tanlang:",
            ["ru"] = "📊 *Галерея визуализаций*\nВыберите тип графика:"
        },

        // ── /report ──
        ["report_generating"] = new()
        {
            ["en"] = "📊 Generating full report for {0}\\.\\.\\.",
            ["uz"] = "📊 {0} uchun to'liq hisobot tayyorlanmoqda\\.\\.\\.",
            ["ru"] = "📊 Формирование полного отчёта за {0}\\.\\.\\."
        },
        ["report_caption"] = new()
        {
            ["en"] = "Full Visual Report — {0}",
            ["uz"] = "To'liq vizual hisobot — {0}",
            ["ru"] = "Полный визуальный отчёт — {0}"
        },
        ["report_done"] = new()
        {
            ["en"] = "✅ {0} charts delivered for {1}",
            ["uz"] = "✅ {1} uchun {0} ta diagramma yuborildi",
            ["ru"] = "✅ {0} графиков доставлено за {1}"
        },

        // ── /export ──
        ["export_generating"] = new()
        {
            ["en"] = "📊 Generating enhanced export\\.\\.\\.",
            ["uz"] = "📊 Kengaytirilgan eksport tayyorlanmoqda\\.\\.\\.",
            ["ru"] = "📊 Формирование расширенного экспорта\\.\\.\\."
        },
    };

    public static string PeriodLabel(int days, string lang) => days switch
    {
        1 => Get("period_24h", lang),
        7 => Get("period_7d", lang),
        30 => Get("period_30d", lang),
        _ => $"{days} {(lang == "ru" ? "Дней" : lang == "uz" ? "Kun" : "Days")}"
    };

    public static string RiskLevel(double utilization, string lang) => utilization switch
    {
        >= 80 => Get("risk_healthy", lang),
        >= 60 => Get("risk_moderate", lang),
        >= 40 => Get("risk_concerning", lang),
        _ => Get("risk_critical", lang)
    };
}
