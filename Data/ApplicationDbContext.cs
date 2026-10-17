using HRReserveSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Data;

public class ApplicationDbContext(DbContextOptions options) : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<Vacancy> Vacancies => Set<Vacancy>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<Interview> Interviews => Set<Interview>();

    public DbSet<InterviewFeedback> InterviewFeedbacks => Set<InterviewFeedback>();

    public DbSet<SoftSkillAssessment> SoftSkillAssessments => Set<SoftSkillAssessment>();

    public DbSet<Recruiter> Recruiters => Set<Recruiter>();

    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();

    public DbSet<CandidateCommunication> CandidateCommunications => Set<CandidateCommunication>();

    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Candidate>()
            .HasIndex(candidate => candidate.Email)
            .IsUnique();

        modelBuilder.Entity<Candidate>()
            .HasIndex(candidate => new { candidate.IsDeleted, candidate.FullName });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(log => new { log.Action, log.CreatedAt });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(log => log.CreatedAt);

        modelBuilder.Entity<EmailOutboxMessage>()
            .HasIndex(m => new { m.Status, m.CreatedAt });

        modelBuilder.Entity<Recruiter>()
            .HasIndex(recruiter => recruiter.Email)
            .IsUnique();

        modelBuilder.Entity<Recruiter>()
            .HasIndex(recruiter => recruiter.Login)
            .IsUnique();

        modelBuilder.Entity<Application>()
            .HasIndex(application => new { application.CandidateId, application.VacancyId })
            .IsUnique();

        modelBuilder.Entity<Application>()
            .HasIndex(a => a.Status);

        modelBuilder.Entity<Application>()
            .HasIndex(a => a.StageId);

        modelBuilder.Entity<Vacancy>()
            .ToTable(table =>
            {
                table.HasCheckConstraint("CK_Vacancies_Status", "\"Status\" IN ('Open','Paused','Closed')");
                table.HasCheckConstraint(
                    "CK_Vacancies_SalaryRange",
                    Database.IsSqlite()
                        ? "CAST(\"SalaryMax\" AS REAL) >= CAST(\"SalaryMin\" AS REAL)"
                        : "\"SalaryMax\" >= \"SalaryMin\"");
            });

        modelBuilder.Entity<Application>()
            .ToTable(table =>
            {
                table.HasCheckConstraint("CK_Applications_Status", "\"Status\" IN ('New','Screening','Interview','TestTask','Offer','Hired','Rejected')");
            });

        modelBuilder.Entity<InterviewFeedback>()
            .ToTable(table =>
            {
                table.HasCheckConstraint("CK_InterviewFeedbacks_Score", "\"Score\" BETWEEN 1 AND 10");
            });

        modelBuilder.Entity<SoftSkillAssessment>()
            .ToTable(table =>
            {
                table.HasCheckConstraint("CK_SoftSkillAssessments_Communication", "\"Communication\" BETWEEN 1 AND 10");
                table.HasCheckConstraint("CK_SoftSkillAssessments_Teamwork", "\"Teamwork\" BETWEEN 1 AND 10");
                table.HasCheckConstraint("CK_SoftSkillAssessments_Responsibility", "\"Responsibility\" BETWEEN 1 AND 10");
                table.HasCheckConstraint("CK_SoftSkillAssessments_StressResistance", "\"StressResistance\" BETWEEN 1 AND 10");
                table.HasCheckConstraint("CK_SoftSkillAssessments_Leadership", "\"Leadership\" BETWEEN 1 AND 10");
            });

        if (Database.IsSqlite())
        {
            modelBuilder.Entity<Vacancy>()
                .Property(vacancy => vacancy.SalaryMin)
                .HasColumnType("TEXT");

            modelBuilder.Entity<Vacancy>()
                .Property(vacancy => vacancy.SalaryMax)
                .HasColumnType("TEXT");
        }

        // NOTE:
        // For local demo we store decimals as TEXT in SQLite to avoid precision/platform differences.
        // When running with PostgreSQL (production/docker), the Postgres migrations in
        // `Migrations/Postgres` should declare these columns as numeric (e.g. numeric(18,2)).
        // If you change provider or adjust model types, ensure Postgres migrations and
        // `PostgresApplicationDbContextModelSnapshot` are updated so EF Core model snapshot
        // matches the real database schema. Failing to keep migrations/snapshot in sync with
        // the live DB can cause InvalidCastException when EF materializes entities.

        modelBuilder.Entity<Interview>()
            .HasOne(interview => interview.Recruiter)
            .WithMany(recruiter => recruiter.Interviews)
            .HasForeignKey(interview => interview.RecruiterId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<InterviewFeedback>()
            .HasOne(feedback => feedback.Recruiter)
            .WithMany(recruiter => recruiter.Feedbacks)
            .HasForeignKey(feedback => feedback.RecruiterId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
