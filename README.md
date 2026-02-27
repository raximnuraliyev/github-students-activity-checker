# GitHub Activity Monitor & License Optimizer

A .NET Worker Service that monitors GitHub contribution activity for university student accounts and provides an admin interface via Telegram Bot to identify inactive users and optimize GitHub Pro licensing costs.

---

## Architecture

```
┌──────────────────────────────────────────────────┐
│              .NET Worker Service                 │
│                                                  │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────┐ │
│  │ Quartz.NET  │  │ GitHub       │  │ Telegram │ │
│  │ Scheduler   │──│ GraphQL      │  │ Bot      │ │
│  │ (02:00 AM)  │  │ Service      │  │ Service  │ │
│  └─────┬───────┘  └──────┬───────┘  └─────┬────┘ │
│        │                 │                │      │
│        └────────┬────────┘                │      │
│                 ▼                         │      │
│         ┌──────────────┐                  │      │
│         │  Sync Engine │◄─────────────────┘      │
│         └──────┬───────┘                         │
│                │                                 │
│                ▼                                 │
│         ┌───────────────┐                        │
│         │  PostgreSQL   │                        │
│         │  (EF Core)    │                        │
│         └───────────────┘                        │
└──────────────────────────────────────────────────┘
```

## Tech Stack

| Component       | Technology                      |
| --------------- | ------------------------------- |
| Runtime         | .NET 10 (Worker Service)        |
| Database        | PostgreSQL + EF Core            |
| GitHub API      | GraphQL v4 (GraphQL.Client)     |
| Scheduling      | Quartz.NET                      |
| Admin Interface | Telegram Bot API                |
| Logging         | Serilog (Console + File)        |
| Export          | CsvHelper                       |

## Database Schema

### `students`
| Column           | Type      | Description                              |
| ---------------- | --------- | ---------------------------------------- |
| id               | UUID (PK) | Auto-generated unique ID                 |
| university_id    | VARCHAR   | Student's official university ID         |
| github_username  | VARCHAR   | Unique, indexed GitHub handle            |
| email            | VARCHAR   | University email for license association |
| last_active_date | TIMESTAMP | Date of most recent contribution         |
| status           | VARCHAR   | Active / Inactive / Pending_Removal      |

### `daily_contributions`
| Column     | Type      | Description                          |
| ---------- | --------- | ------------------------------------ |
| student_id | UUID (FK) | References `students.id`             |
| date       | DATE      | Specific day being tracked           |
| count      | INT       | Number of contributions on that day  |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 14+](https://www.postgresql.org/download/)
- A **GitHub Personal Access Token** (PAT) with `read:user` scope
- A **Telegram Bot Token** from [@BotFather](https://t.me/BotFather)

### 1. Clone & Configure

```bash
cd GithubActivityChecker
```

Set secrets via User Secrets (recommended for local dev):

```bash
cd GithubActivityChecker
dotnet user-secrets set "GitHub:PersonalAccessToken" "ghp_your_token_here"
dotnet user-secrets set "Telegram:BotToken" "123456789:ABCdef..."
dotnet user-secrets set "Telegram:AuthorizedChatIds:0" "your_chat_id"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=github_activity_monitor;Username=postgres;Password=your_password"
```

### 2. Run with PostgreSQL (Local)

Ensure PostgreSQL is running, then:

```bash
cd GithubActivityChecker
dotnet run
```

The service will automatically apply EF Core migrations on startup.

### 3. Run with Docker Compose

```bash
# Copy and fill in your secrets
cp .env.example .env
# Edit .env with your real tokens

docker compose up --build -d
```

## Telegram Bot Commands

| Command             | Description                                         |
| ------------------- | --------------------------------------------------- |
| `/status`           | Summary: Total / Active / Inactive / Pending counts |
| `/list_inactive`    | Download CSV of all inactive students               |
| `/check [username]` | Real-time GitHub activity for a specific user       |
| `/sync_now`         | Manually trigger a full sync                        |
| `/help`             | Show available commands                             |

## Inactivity Policy

| Condition                          | Status            |
| ---------------------------------- | ----------------- |
| 0 contributions in last 30 days    | **Inactive**      |
| 0 contributions in last 60 days    | **Pending_Removal** |
| Any contributions in last 30 days  | **Active**        |

These thresholds are configurable in `appsettings.json` under `InactivityPolicy`.

## Scheduling

The full sync runs every night at **02:00 AM** by default (configurable Quartz cron expression):

```json
{
  "SyncSchedule": {
    "CronExpression": "0 0 2 * * ?"
  }
}
```

## Rate Limiting

- GitHub GraphQL API allows **5,000 points/hour**
- Students are processed in **batches of 50** with a configurable delay between batches (default: 5 seconds)
- Batch size and delay are configurable in `appsettings.json` under `GitHub`

## Privacy

The system only stores **contribution counts**, never repository names, commit messages, or code content.

## Logging

Serilog writes to both console and rolling log files in the `logs/` directory (30-day retention).

## Adding Students

Insert students directly into the `students` table:

```sql
INSERT INTO students (university_id, github_username, email)
VALUES ('STU-001', 'octocat', 'student@university.edu');
```

Or build an import script/endpoint as needed for your CSV of 1,500 students.

## Project Structure

```
GithubActivityChecker/
├── Configuration/          # Strongly-typed settings classes
│   ├── GitHubSettings.cs
│   ├── InactivityPolicySettings.cs
│   ├── SyncScheduleSettings.cs
│   └── TelegramSettings.cs
├── Data/                   # EF Core DbContext & Migrations
│   ├── AppDbContext.cs
│   └── Migrations/
├── Jobs/                   # Quartz.NET job definitions
│   └── FullSyncJob.cs
├── Models/                 # Domain entities
│   ├── Student.cs
│   ├── StudentStatus.cs
│   ├── DailyContribution.cs
│   └── ContributionCalendarResult.cs
├── Services/               # Core business logic
│   ├── IGitHubService.cs
│   ├── GitHubService.cs
│   ├── ISyncService.cs
│   └── SyncService.cs
├── TelegramBot/            # Telegram admin interface
│   └── TelegramBotService.cs
├── Program.cs              # Host bootstrap & DI wiring
├── appsettings.json        # Default configuration
├── Dockerfile
└── GithubActivityChecker.csproj
```
#   g i t h u b - s t u d e n t s - a c t i v i t y - c h e c k e r  
 