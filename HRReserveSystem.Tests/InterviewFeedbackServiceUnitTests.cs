using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HRReserveSystem.Tests;

public class InterviewFeedbackServiceUnitTests
{
    private static (ApplicationDbContext Db, SqliteConnection Connection) CreateInMemorySqlite()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    private static async Task<(Recruiter Recruiter1, Recruiter Recruiter2, Interview Interview)> SeedHierarchyAsync(ApplicationDbContext db)
    {
        var recruiter1 = new Recruiter
        {
            FullName = "Олена HR",
            Email = "olena@hr.ua",
            Login = $"olena_{Guid.NewGuid():N}",
            Role = "Interviewer"
        };
        var recruiter2 = new Recruiter
        {
            FullName = "Ігор Tech",
            Email = "igor@hr.ua",
            Login = $"igor_{Guid.NewGuid():N}",
            Role = "Interviewer"
        };
        db.Recruiters.AddRange(recruiter1, recruiter2);

        var candidate = new Candidate
        {
            FullName = "Петро Кандидат",
            Email = "petro@example.com",
            Skills = "C#"
        };
        var vacancy = new Vacancy
        {
            Title = "Senior .NET Engineer",
            Status = "Open"
        };
        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var application = new Application
        {
            CandidateId = candidate.Id,
            VacancyId = vacancy.Id,
            Status = "Interview",
            AppliedAt = DateTime.UtcNow
        };
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var interview = new Interview
        {
            ApplicationId = application.Id,
            RecruiterId = recruiter1.Id,
            InterviewDate = DateTime.UtcNow.AddDays(1),
            InterviewType = "Technical",
            Result = "Passed"
        };
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        return (recruiter1, recruiter2, interview);
    }

    [Fact]
    public async Task GetFeedbackForEditAsync_Blocks_IDOR_When_User_Is_Not_Owner_Or_Admin()
    {
        var (db, conn) = CreateInMemorySqlite();
        try
        {
            var audit = new AuditService(db, new HttpContextAccessor(), new NullLogger<AuditService>());
            var service = new InterviewFeedbackService(db, audit, new NullLogger<InterviewFeedbackService>());

            var (r1, r2, interview) = await SeedHierarchyAsync(db);

            var feedback = new InterviewFeedback
            {
                InterviewId = interview.Id,
                RecruiterId = r1.Id,
                Comment = "Strong senior background",
                Score = 5,
                Recommendation = "Hire",
                CreatedAt = DateTime.UtcNow
            };
            db.InterviewFeedbacks.Add(feedback);
            await db.SaveChangesAsync();

            // When Recruiter 2 tries to edit Recruiter 1's feedback (non-admin)
            var (fb, forbidden) = await service.GetFeedbackForEditAsync(feedback.Id, currentRecruiterId: r2.Id, isAdmin: false);

            Assert.True(forbidden);
            Assert.Null(fb);

            // When Owner (Recruiter 1) edits
            var (ownerFb, ownerForbidden) = await service.GetFeedbackForEditAsync(feedback.Id, currentRecruiterId: r1.Id, isAdmin: false);
            Assert.False(ownerForbidden);
            Assert.NotNull(ownerFb);

            // When Admin edits
            var (adminFb, adminForbidden) = await service.GetFeedbackForEditAsync(feedback.Id, currentRecruiterId: 999, isAdmin: true);
            Assert.False(adminForbidden);
            Assert.NotNull(adminFb);
        }
        finally
        {
            await db.DisposeAsync();
            conn.Close();
        }
    }

    [Fact]
    public async Task UpdateFeedbackAsync_Blocks_Unauthorized_Modifications()
    {
        var (db, conn) = CreateInMemorySqlite();
        try
        {
            var audit = new AuditService(db, new HttpContextAccessor(), new NullLogger<AuditService>());
            var service = new InterviewFeedbackService(db, audit, new NullLogger<InterviewFeedbackService>());

            var (r1, r2, interview) = await SeedHierarchyAsync(db);

            var feedback = new InterviewFeedback
            {
                InterviewId = interview.Id,
                RecruiterId = r1.Id,
                Comment = "Initial feedback",
                Score = 3,
                Recommendation = "Consider",
                CreatedAt = DateTime.UtcNow
            };
            db.InterviewFeedbacks.Add(feedback);
            await db.SaveChangesAsync();

            var maliciousUpdate = new InterviewFeedback
            {
                Id = feedback.Id,
                Comment = "Hacked feedback",
                Score = 1,
                Recommendation = "Reject"
            };

            // Attempt from r2 (not owner, not admin)
            var (success, error, forbidden) = await service.UpdateFeedbackAsync(maliciousUpdate, currentRecruiterId: r2.Id, isAdmin: false);

            Assert.True(forbidden);
            Assert.False(success);

            var inDb = await db.InterviewFeedbacks.FindAsync(feedback.Id);
            Assert.Equal("Initial feedback", inDb!.Comment);
            Assert.Equal(3, inDb.Score);
        }
        finally
        {
            await db.DisposeAsync();
            conn.Close();
        }
    }
}
