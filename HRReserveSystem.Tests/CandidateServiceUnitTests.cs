using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HRReserveSystem.Tests;

public class CandidateServiceUnitTests
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
    public async Task CreateCandidateAsync_Persists_Candidate_And_Logs_Audit()
    {
        var (db, conn) = CreateInMemorySqlite();
        try
        {
            var httpAccessor = new HttpContextAccessor();
            var audit = new AuditService(db, httpAccessor, new NullLogger<AuditService>());
            var stubEnv = new HrIntegrationTestsStubEnvironment();
            var storage = new CandidateFileStorageService(stubEnv, new LocalObjectStorageService(stubEnv, new NullLogger<LocalObjectStorageService>()));
            var resumeIntel = new ResumeIntelligenceService();
            var queue = new EmailBackgroundQueue();
            var service = new CandidateService(db, storage, resumeIntel, queue, audit, new NullLogger<CandidateService>());

            var candidate = new Candidate
            {
                FullName = "Андрій Мельник",
                Email = "andriy.melnyk@example.com",
                Skills = "C#, .NET 8",
                ExperienceYears = 4
            };

            var created = await service.CreateCandidateAsync(candidate, null, null);

            Assert.True(created.Id > 0);
            Assert.True(await db.Candidates.AnyAsync(c => c.Email == "andriy.melnyk@example.com"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "CreateCandidate" && a.EntityId == created.Id.ToString()));
        }
        finally
        {
            await db.DisposeAsync();
            conn.Close();
        }
    }

    [Fact]
    public async Task SoftDeleteCandidateAsync_Sets_IsDeleted_True_Without_Removing_Entity()
    {
        var (db, conn) = CreateInMemorySqlite();
        try
        {
            var httpAccessor = new HttpContextAccessor();
            var audit = new AuditService(db, httpAccessor, new NullLogger<AuditService>());
            var stubEnv = new HrIntegrationTestsStubEnvironment();
            var storage = new CandidateFileStorageService(stubEnv, new LocalObjectStorageService(stubEnv, new NullLogger<LocalObjectStorageService>()));
            var resumeIntel = new ResumeIntelligenceService();
            var queue = new EmailBackgroundQueue();
            var service = new CandidateService(db, storage, resumeIntel, queue, audit, new NullLogger<CandidateService>());

            var candidate = new Candidate
            {
                FullName = "Олег Сидоренко",
                Email = "oleg.sydorenko@example.com",
                Skills = "SQL, Azure",
                IsDeleted = false
            };
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync();

            var result = await service.SoftDeleteCandidateAsync(candidate.Id);

            Assert.True(result);
            var inDb = await db.Candidates.FindAsync(candidate.Id);
            Assert.NotNull(inDb);
            Assert.True(inDb.IsDeleted);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "SoftDeleteCandidate" && a.EntityId == candidate.Id.ToString()));
        }
        finally
        {
            await db.DisposeAsync();
            conn.Close();
        }
    }

    [Fact]
    public async Task AddNoteAsync_Appends_Timestamped_Note_To_Candidate_Summary()
    {
        var (db, conn) = CreateInMemorySqlite();
        try
        {
            var httpAccessor = new HttpContextAccessor();
            var audit = new AuditService(db, httpAccessor, new NullLogger<AuditService>());
            var stubEnv = new HrIntegrationTestsStubEnvironment();
            var storage = new CandidateFileStorageService(stubEnv, new LocalObjectStorageService(stubEnv, new NullLogger<LocalObjectStorageService>()));
            var resumeIntel = new ResumeIntelligenceService();
            var queue = new EmailBackgroundQueue();
            var service = new CandidateService(db, storage, resumeIntel, queue, audit, new NullLogger<CandidateService>());

            var candidate = new Candidate
            {
                FullName = "Ірина Василенко",
                Email = "iryna.vasylenko@example.com",
                ResumeSummary = "Старий опис."
            };
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync();

            var (success, error, note, _, _) = await service.AddNoteAsync(candidate.Id, null, "Пройшла попередню співбесіду.", "Рекрутер Анна");

            Assert.True(success);
            Assert.Null(error);
            Assert.Contains("Пройшла попередню співбесіду", note);

            var refreshed = await db.Candidates.FindAsync(candidate.Id);
            Assert.NotNull(refreshed);
            Assert.Contains("Пройшла попередню співбесіду", refreshed.ResumeSummary);
            Assert.Contains("Старий опис", refreshed.ResumeSummary);
        }
        finally
        {
            await db.DisposeAsync();
            conn.Close();
        }
    }
}

public class HrIntegrationTestsStubEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "HRReserveSystem.Tests";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
