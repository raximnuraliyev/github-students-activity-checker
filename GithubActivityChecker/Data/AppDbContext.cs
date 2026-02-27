using GithubActivityChecker.Models;
using Microsoft.EntityFrameworkCore;

namespace GithubActivityChecker.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<DailyContribution> DailyContributions => Set<DailyContribution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---------- Student ----------
        modelBuilder.Entity<Student>(entity =>
        {
            entity.ToTable("students");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.UniversityId)
                .HasColumnName("university_id")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.GithubUsername)
                .HasColumnName("github_username")
                .HasMaxLength(255)
                .IsRequired();

            entity.HasIndex(e => e.GithubUsername)
                .IsUnique();

            entity.Property(e => e.Email)
                .HasColumnName("email")
                .HasMaxLength(320);

            entity.Property(e => e.LastActiveDate)
                .HasColumnName("last_active_date");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(StudentStatus.Active);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("now()");

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("now()");
        });

        // ---------- DailyContribution ----------
        modelBuilder.Entity<DailyContribution>(entity =>
        {
            entity.ToTable("daily_contributions");

            // Composite primary key: (StudentId, Date)
            entity.HasKey(e => new { e.StudentId, e.Date });

            entity.Property(e => e.StudentId)
                .HasColumnName("student_id");

            entity.Property(e => e.Date)
                .HasColumnName("date");

            entity.Property(e => e.Count)
                .HasColumnName("count")
                .HasDefaultValue(0);

            entity.HasOne(e => e.Student)
                .WithMany(s => s.DailyContributions)
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.StudentId, e.Date });
        });
    }
}
