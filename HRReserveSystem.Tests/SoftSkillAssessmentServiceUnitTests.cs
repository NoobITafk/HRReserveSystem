using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HRReserveSystem.Tests;

public class SoftSkillAssessmentServiceUnitTests
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

    [Fact]
    public async Task GetAssessmentForEditAsync_Blocks_IDOR_When_User_Is_Not_Owner_Or_Admin()
    {
        var (db, conn) = CreateInMemorySqlite();
        try
        {
            var audit = new AuditService(db, new HttpContextAccessor(), new NullLogger<AuditService>());
            var service = new SoftSkillAssessmentService(db, audit, new NullLogger<SoftSkillAssessmentService>());

            var recruiter10 = new Recruiter { Id = 10, FullName = "Рекрутер 10", Login = "rec10", Email = "rec10@hr.ua" };
            var recruiter20 = new Recruiter { Id = 20, FullName = "Рекрутер 20", Login = "rec20", Email = "rec20@hr.ua" };
            db.Recruiters.AddRange(recruiter10, recruiter20);

            var candidate = new Candidate
            {
                FullName = "Максим Тестовий",
                Email = "mak@example.com"
            };
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync();

            var assessment = new SoftSkillAssessment
            {
                CandidateId = candidate.Id,
                RecruiterId = 10,
                Communication = 9,
                Teamwork = 8,
                Responsibility = 8,
                StressResistance = 7,
                Leadership = 9,
                CreatedAt = DateTime.UtcNow
            };
            db.SoftSkillAssessments.Add(assessment);
            await db.SaveChangesAsync();

            // When Recruiter 20 tries to edit Recruiter 10's soft skills assessment (non-admin)
            var (item, forbidden) = await service.GetAssessmentForEditAsync(assessment.Id, currentRecruiterId: 20, isAdmin: false);
            Assert.True(forbidden);
            Assert.Null(item);

            // When Owner (Recruiter 10) edits
            var (ownerItem, ownerForbidden) = await service.GetAssessmentForEditAsync(assessment.Id, currentRecruiterId: 10, isAdmin: false);
            Assert.False(ownerForbidden);
            Assert.NotNull(ownerItem);

            // When Admin edits
            var (adminItem, adminForbidden) = await service.GetAssessmentForEditAsync(assessment.Id, currentRecruiterId: 99, isAdmin: true);
            Assert.False(adminForbidden);
            Assert.NotNull(adminItem);
        }
        finally
        {
            await db.DisposeAsync();
            conn.Close();
        }
    }

    [Fact]
    public async Task UpdateAssessmentAsync_Blocks_Unauthorized_Interviewer()
    {
        var (db, conn) = CreateInMemorySqlite();
        try
        {
            var audit = new AuditService(db, new HttpContextAccessor(), new NullLogger<AuditService>());
            var service = new SoftSkillAssessmentService(db, audit, new NullLogger<SoftSkillAssessmentService>());

            var recruiter5 = new Recruiter { Id = 5, FullName = "Рекрутер 5", Login = "rec5", Email = "rec5@hr.ua" };
            var recruiter99 = new Recruiter { Id = 99, FullName = "Рекрутер 99", Login = "rec99", Email = "rec99@hr.ua" };
            db.Recruiters.AddRange(recruiter5, recruiter99);

            var candidate = new Candidate
            {
                FullName = "Іван Кандидат",
                Email = "ivan@example.com"
            };
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync();

            var assessment = new SoftSkillAssessment
            {
                CandidateId = candidate.Id,
                RecruiterId = 5,
                Communication = 6,
                Teamwork = 6,
                Responsibility = 6,
                StressResistance = 6,
                Leadership = 6
            };
            db.SoftSkillAssessments.Add(assessment);
            await db.SaveChangesAsync();

            var maliciousUpdate = new SoftSkillAssessment
            {
                Id = assessment.Id,
                CandidateId = candidate.Id,
                Communication = 1,
                OverallComment = "Tampered"
            };

            var (success, error, forbidden) = await service.UpdateAssessmentAsync(maliciousUpdate, currentRecruiterId: 99, isAdmin: false);
            Assert.True(forbidden);
            Assert.False(success);

            var inDb = await db.SoftSkillAssessments.FindAsync(assessment.Id);
            Assert.Equal(6, inDb!.Communication);
        }
        finally
        {
            await db.DisposeAsync();
            conn.Close();
        }
    }
}
