using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GithubActivityChecker.Data.Migrations;

/// <summary>
/// Creates PostgreSQL VIEWs for pre-aggregated contribution data used by the visualization module.
/// </summary>
public partial class AddContributionViews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 7-day rolling summary per student
        migrationBuilder.Sql("""
            CREATE OR REPLACE VIEW vw_student_7d_summary AS
            SELECT
                s.id AS student_id,
                s.github_username,
                s.university_id,
                s.status,
                COALESCE(SUM(dc.count), 0) AS total_contributions_7d,
                MAX(dc.date) AS last_contribution_date
            FROM students s
            LEFT JOIN daily_contributions dc
                ON dc.student_id = s.id
                AND dc.date >= CURRENT_DATE - INTERVAL '7 days'
            GROUP BY s.id, s.github_username, s.university_id, s.status;
            """);

        // 30-day rolling summary per student
        migrationBuilder.Sql("""
            CREATE OR REPLACE VIEW vw_student_30d_summary AS
            SELECT
                s.id AS student_id,
                s.github_username,
                s.university_id,
                s.status,
                COALESCE(SUM(dc.count), 0) AS total_contributions_30d,
                MAX(dc.date) AS last_contribution_date
            FROM students s
            LEFT JOIN daily_contributions dc
                ON dc.student_id = s.id
                AND dc.date >= CURRENT_DATE - INTERVAL '30 days'
            GROUP BY s.id, s.github_username, s.university_id, s.status;
            """);

        // Daily aggregate across all students (for trend/activity charts)
        migrationBuilder.Sql("""
            CREATE OR REPLACE VIEW vw_daily_aggregate AS
            SELECT
                dc.date,
                SUM(dc.count) AS total_contributions,
                COUNT(DISTINCT dc.student_id) AS active_students
            FROM daily_contributions dc
            GROUP BY dc.date
            ORDER BY dc.date;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS vw_daily_aggregate;");
        migrationBuilder.Sql("DROP VIEW IF EXISTS vw_student_30d_summary;");
        migrationBuilder.Sql("DROP VIEW IF EXISTS vw_student_7d_summary;");
    }
}
