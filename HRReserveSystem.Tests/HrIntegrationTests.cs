using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace HRReserveSystem.Tests;

public class HrIntegrationTests
{
    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HRReserveSystem.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record InterviewPostData(
        int InterviewId,
        int ApplicationId,
        string CandidateName,
        string CandidateEmail,
        string VacancyTitle,
        int? RecruiterId,
        string? RecruiterEmail);

    [Fact]
    public void DatabaseConfiguration_Defaults_To_Sqlite()
    {
        var configuration = new ConfigurationBuilder().Build();

        var provider = DatabaseConfiguration.GetProvider(configuration, new StubEnvironment());

        Assert.Equal(DatabaseConfiguration.SqliteProvider, provider);
    }

    [Fact]
    public void DatabaseConfiguration_Uses_Postgres_DatabaseUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSQL",
                ["DATABASE_URL"] = "postgres://hr_user:secret@postgres:5432/hrreserve"
            })
            .Build();

        var provider = DatabaseConfiguration.GetProvider(configuration, new StubEnvironment());
        var connectionString = DatabaseConfiguration.GetConnectionString(configuration, provider);

        Assert.Equal(DatabaseConfiguration.PostgresProvider, provider);
        Assert.Contains("Host=postgres", connectionString);
        Assert.Contains("Database=hrreserve", connectionString);
        Assert.Contains("Username=hr_user", connectionString);
        Assert.Contains("Password=secret", connectionString);
    }

    [Fact]
    public async Task Project_Starts_And_Login_Page_Loads()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_Endpoints_Are_Available_Without_Login(string path)
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Error_Page_Does_Not_Show_Development_Instructions()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Home/Error?statusCode=500");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Request ID", html);
        Assert.DoesNotContain("Development environment", html);
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Works()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await LoginAsync(client, "admin", "admin123");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString ?? "/");
    }

    [Fact]
    public async Task Login_With_Invalid_Password_Does_Not_Work()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await LoginAsync(client, "admin", "wrong-password");
        var dashboardResponse = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, dashboardResponse.StatusCode);
        Assert.Contains("/Account/Login", dashboardResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Recruiter_Cannot_Open_Admin_Pages()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await client.GetAsync("/Recruiters");

        AssertAccessDeniedRedirect(response);
    }

    [Fact]
    public async Task Logout_Get_Is_Not_Allowed_And_Does_Not_Sign_Out()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        var logoutResponse = await client.GetAsync("/Account/Logout");
        var dashboardResponse = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
    }

    [Fact]
    public async Task Interviewer_Cannot_Open_Candidates()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "interviewer", "interviewer123");

        var response = await client.GetAsync("/Candidates");

        AssertAccessDeniedRedirect(response);
    }

    [Fact]
    public async Task Interviewer_Dashboard_Does_Not_Render_Hiring_Links()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "interviewer", "interviewer123");

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("href=\"/Candidates", html);
        Assert.DoesNotContain("href=\"/Vacancies", html);
        Assert.DoesNotContain("href=\"/Applications", html);
        Assert.Contains("href=\"/InterviewFeedbacks", html);
        Assert.Contains("href=\"/SoftSkillAssessments", html);
    }

    [Fact]
    public async Task Candidate_Create_Works()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await PostFormAsync(client, "/Candidates/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Тестовий Кандидат",
            ["Email"] = "candidate.integration@example.com",
            ["Phone"] = "+380671234000",
            ["ExperienceYears"] = "2",
            ["Skills"] = "ASP.NET Core, SQL",
            ["ResumeSummary"] = "Кандидат для integration test."
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Candidates.AnyAsync(candidate => candidate.Email == "candidate.integration@example.com"));
    }

    [Fact]
    public async Task Candidate_Create_Ignores_Posted_ResumeFilePath()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await PostFormAsync(client, "/Candidates/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Path Injection Candidate",
            ["Email"] = "path.injection@example.com",
            ["Phone"] = "+380671234002",
            ["ExperienceYears"] = "1",
            ["Skills"] = "Security review",
            ["ResumeFilePath"] = "https://example.com/evil.exe"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var resumePath = await db.Candidates
            .Where(candidate => candidate.Email == "path.injection@example.com")
            .Select(candidate => candidate.ResumeFilePath)
            .SingleAsync();
        Assert.Null(resumePath);
    }

    [Fact]
    public async Task Candidate_Duplicate_Email_Does_Not_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await PostFormAsync(client, "/Candidates/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Дублікат",
            ["Email"] = "olena.koval@example.com",
            ["Phone"] = "+380671234001",
            ["ExperienceYears"] = "3",
            ["Skills"] = "C#"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Candidates.CountAsync(candidate => candidate.Email == "olena.koval@example.com"));
    }

    [Fact]
    public async Task Duplicate_Application_Does_Not_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await PostFormAsync(client, "/Applications/Create", new Dictionary<string, string>
        {
            ["CandidateId"] = "1",
            ["VacancyId"] = "1",
            ["Status"] = "New",
            ["RecruiterComment"] = "Duplicate pair test."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Applications.CountAsync(application => application.CandidateId == 1 && application.VacancyId == 1));
    }

    [Fact]
    public async Task Database_Rejects_Duplicate_Application()
    {
        using var factory = new HrReserveWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Applications.Add(new Application
        {
            CandidateId = 1,
            VacancyId = 1,
            Status = "New",
            AppliedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Interview_Create_With_Smtp_Disabled_Creates_Outbox_Message()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");
        var interviewData = await GetNewInterviewPostDataAsync(factory);
        var interviewDate = new DateTime(2031, 6, 3, 10, 30, 0);

        var response = await PostFormAsync(client, "/Interviews/Create", new Dictionary<string, string>
        {
            ["ApplicationId"] = interviewData.ApplicationId.ToString(),
            ["RecruiterId"] = interviewData.RecruiterId?.ToString() ?? string.Empty,
            ["InterviewDate"] = interviewDate.ToString("yyyy-MM-ddTHH:mm"),
            ["InterviewType"] = "Technical",
            ["Result"] = "Pending",
            ["Notes"] = "Outbox create integration test."
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var message = AssertSingleOutboxMessage(factory);
        Assert.Contains("Заплановано співбесіду", message);
        Assert.Contains(interviewData.CandidateEmail, message);
        Assert.Contains(interviewData.CandidateName, message);
        Assert.Contains(interviewData.VacancyTitle, message);
        Assert.Contains(interviewDate.ToString("g"), message);
        Assert.Contains("Technical", message);
        Assert.Contains("Outbox create integration test.", message);
        Assert.Contains("SMTP is disabled or host is empty.", message);

        if (!string.IsNullOrWhiteSpace(interviewData.RecruiterEmail))
        {
            Assert.Contains(interviewData.RecruiterEmail, message);
        }
    }

    [Fact]
    public async Task Interview_Edit_With_Smtp_Disabled_Creates_Outbox_Message()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");
        var interviewData = await GetExistingInterviewPostDataAsync(factory);
        var interviewDate = new DateTime(2031, 7, 4, 14, 45, 0);

        var response = await PostFormAsync(client, $"/Interviews/Edit/{interviewData.InterviewId}", new Dictionary<string, string>
        {
            ["Id"] = interviewData.InterviewId.ToString(),
            ["ApplicationId"] = interviewData.ApplicationId.ToString(),
            ["RecruiterId"] = interviewData.RecruiterId?.ToString() ?? string.Empty,
            ["InterviewDate"] = interviewDate.ToString("yyyy-MM-ddTHH:mm"),
            ["InterviewType"] = "Final",
            ["Result"] = "Pending",
            ["Notes"] = "Outbox edit integration test."
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var message = AssertSingleOutboxMessage(factory);
        Assert.Contains("Оновлено співбесіду", message);
        Assert.Contains(interviewData.CandidateEmail, message);
        Assert.Contains(interviewData.CandidateName, message);
        Assert.Contains(interviewData.VacancyTitle, message);
        Assert.Contains(interviewDate.ToString("g"), message);
        Assert.Contains("Final", message);
        Assert.Contains("Outbox edit integration test.", message);
        Assert.Contains("SMTP is disabled or host is empty.", message);

        if (!string.IsNullOrWhiteSpace(interviewData.RecruiterEmail))
        {
            Assert.Contains(interviewData.RecruiterEmail, message);
        }
    }

    [Fact]
    public async Task Email_RedirectAllTo_Rewrites_Outbox_Recipient()
    {
        using var factory = new HrReserveWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Email:RedirectAllTo"] = "qa-check@example.com"
        });
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");
        var interviewData = await GetNewInterviewPostDataAsync(factory);

        var response = await PostFormAsync(client, "/Interviews/Create", new Dictionary<string, string>
        {
            ["ApplicationId"] = interviewData.ApplicationId.ToString(),
            ["RecruiterId"] = interviewData.RecruiterId?.ToString() ?? string.Empty,
            ["InterviewDate"] = new DateTime(2031, 8, 5, 9, 15, 0).ToString("yyyy-MM-ddTHH:mm"),
            ["InterviewType"] = "HR",
            ["Result"] = "Pending",
            ["Notes"] = "Redirect recipient integration test."
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var message = AssertSingleOutboxMessage(factory);
        Assert.Contains("To: qa-check@example.com", message);
        Assert.Contains(interviewData.CandidateEmail, message);
    }

    [Fact]
    public async Task Feedback_Score_11_Does_Not_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        var response = await PostFormAsync(client, "/InterviewFeedbacks/Create", new Dictionary<string, string>
        {
            ["InterviewId"] = "1",
            ["Comment"] = "Score validation test.",
            ["Score"] = "11",
            ["Recommendation"] = "Hire"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.InterviewFeedbacks.AnyAsync(feedback => feedback.Score == 11));
    }

    [Fact]
    public async Task Feedback_Score_0_Does_Not_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        var response = await PostFormAsync(client, "/InterviewFeedbacks/Create", new Dictionary<string, string>
        {
            ["InterviewId"] = "1",
            ["Comment"] = "Score zero validation test.",
            ["Score"] = "0",
            ["Recommendation"] = "Hire"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.InterviewFeedbacks.AnyAsync(feedback => feedback.Score == 0));
    }

    [Fact]
    public async Task Feedback_Valid_Score_Passes()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        var response = await PostFormAsync(client, "/InterviewFeedbacks/Create", new Dictionary<string, string>
        {
            ["InterviewId"] = "1",
            ["Comment"] = "Valid feedback score integration test.",
            ["Score"] = "10",
            ["Recommendation"] = "Hire"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.InterviewFeedbacks.AnyAsync(feedback =>
            feedback.Score == 10 &&
            feedback.Comment == "Valid feedback score integration test."));
    }

    [Fact]
    public async Task Database_Rejects_Invalid_Feedback_Score()
    {
        using var factory = new HrReserveWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.InterviewFeedbacks.Add(new InterviewFeedback
        {
            InterviewId = 1,
            Comment = "Direct DB constraint test.",
            Score = 11,
            Recommendation = "Hire",
            CreatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SoftSkill_Score_Outside_Range_Does_Not_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        var response = await PostFormAsync(client, "/SoftSkillAssessments/Create", new Dictionary<string, string>
        {
            ["CandidateId"] = "1",
            ["Communication"] = "0",
            ["Teamwork"] = "11",
            ["Responsibility"] = "5",
            ["StressResistance"] = "5",
            ["Leadership"] = "5",
            ["OverallComment"] = "Range validation test."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.SoftSkillAssessments.AnyAsync(assessment => assessment.Communication == 0 || assessment.Teamwork == 11));
    }

    [Fact]
    public async Task SoftSkill_Valid_Scores_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        var response = await PostFormAsync(client, "/SoftSkillAssessments/Create", new Dictionary<string, string>
        {
            ["CandidateId"] = "2",
            ["Communication"] = "1",
            ["Teamwork"] = "10",
            ["Responsibility"] = "5",
            ["StressResistance"] = "6",
            ["Leadership"] = "7",
            ["OverallComment"] = "Valid soft skills integration test."
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.SoftSkillAssessments.AnyAsync(assessment =>
            assessment.CandidateId == 2 &&
            assessment.Communication == 1 &&
            assessment.Teamwork == 10 &&
            assessment.OverallComment == "Valid soft skills integration test."));
    }

    [Fact]
    public async Task Upload_Exe_Resume_Does_Not_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var beforePath = await GetCandidateResumePathAsync(factory, 1);
        var response = await UploadResumeAsync(client, "/Candidates/UploadResume/1", "malware.exe", "MZ"u8.ToArray());
        var afterPath = await GetCandidateResumePathAsync(factory, 1);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(beforePath, afterPath);
    }

    [Fact]
    public async Task Upload_Too_Large_Resume_Does_Not_Pass()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var beforePath = await GetCandidateResumePathAsync(factory, 1);
        var oversizedPdf = new byte[(5 * 1024 * 1024) + 1];
        var response = await UploadResumeAsync(client, "/Candidates/UploadResume/1", "too-large.pdf", oversizedPdf);
        var afterPath = await GetCandidateResumePathAsync(factory, 1);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(beforePath, afterPath);
    }

    [Fact]
    public async Task Api_Unauthenticated_Request_Returns_401_Not_Login_Redirect()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/candidates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_Candidates_Returns_Dtos_Without_Password_Fields()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await client.GetAsync("/api/candidates");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("olena.koval@example.com", json);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Api_Minimal_Endpoints_Return_Json()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        foreach (var path in new[] { "/api/candidates", "/api/vacancies", "/api/applications", "/api/interviews", "/api/soft-skills" })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Api_Forbidden_Role_Returns_403()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "interviewer", "interviewer123");

        var response = await client.GetAsync("/api/candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("resume.pdf", "%PDF-1.7")]
    [InlineData("resume.doc", "DOC")]
    [InlineData("resume.docx", "DOCX")]
    public async Task Upload_Allowed_Resume_File_Passes(string fileName, string content)
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await UploadResumeAsync(client, "/Candidates/UploadResume/1", fileName, System.Text.Encoding.UTF8.GetBytes(content));
        var resumePath = await GetCandidateResumePathAsync(factory, 1);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/uploads/resumes/", resumePath);
        Assert.EndsWith(Path.GetExtension(fileName), resumePath);
        Assert.Matches(@"^/uploads/resumes/[0-9a-f]{32}\.(pdf|doc|docx)$", resumePath);
        Assert.DoesNotContain(fileName, resumePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_Resume_Does_Not_Allow_Path_Traversal()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var candidate = await db.Candidates.FirstAsync(candidate => candidate.Id == 1);
            candidate.ResumeFilePath = "/uploads/resumes/../../appsettings.json";
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/Candidates/DownloadResume/1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Hidden_Candidate_Can_Be_Restored_From_UI()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var hideResponse = await PostFormAsync(client, "/Candidates/Delete/1", "/Candidates/Delete/1", []);
        Assert.Equal(HttpStatusCode.Redirect, hideResponse.StatusCode);

        var hiddenListResponse = await client.GetAsync("/Candidates?showHidden=true");
        var hiddenListHtml = await hiddenListResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, hiddenListResponse.StatusCode);
        Assert.Contains("olena.koval@example.com", hiddenListHtml);
        Assert.Contains("Відновити", hiddenListHtml);

        var restoreResponse = await PostFormAsync(client, "/Candidates?showHidden=true", "/Candidates/Restore/1", []);
        Assert.Equal(HttpStatusCode.Redirect, restoreResponse.StatusCode);

        var activeListResponse = await client.GetAsync("/Candidates");
        var activeListHtml = await activeListResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, activeListResponse.StatusCode);
        Assert.Contains("olena.koval@example.com", activeListHtml);
    }

    [Fact]
    public async Task Archived_Vacancy_Can_Be_Restored_From_UI()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var archiveResponse = await PostFormAsync(client, "/Vacancies/Delete/1", "/Vacancies/Delete/1", []);
        Assert.Equal(HttpStatusCode.Redirect, archiveResponse.StatusCode);

        var archiveListResponse = await client.GetAsync("/Vacancies?showArchived=true");
        var archiveListHtml = await archiveListResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, archiveListResponse.StatusCode);
        Assert.Contains("Junior .NET Developer", archiveListHtml);
        Assert.Contains("Відновити", archiveListHtml);

        var restoreResponse = await PostFormAsync(client, "/Vacancies?showArchived=true", "/Vacancies/Restore/1", []);
        Assert.Equal(HttpStatusCode.Redirect, restoreResponse.StatusCode);

        var activeListResponse = await client.GetAsync("/Vacancies");
        var activeListHtml = await activeListResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, activeListResponse.StatusCode);
        Assert.Contains("Junior .NET Developer", activeListHtml);
    }

    [Fact]
    public async Task Register_Page_Loads_And_Creates_New_Recruiter_With_AutoLogin()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var getResponse = await client.GetAsync("/Account/Register");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var registerResponse = await PostFormAsync(client, "/Account/Register", new Dictionary<string, string>
        {
            ["FullName"] = "Новий Рекрутер",
            ["Email"] = "new.recruiter@company.ua",
            ["Login"] = "new.recruiter",
            ["Role"] = "Recruiter",
            ["Password"] = "securePass123",
            ["ConfirmPassword"] = "securePass123"
        });

        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);

        var dashboardResponse = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Recruiters.AnyAsync(r => r.Login == "new.recruiter" && r.Role == "Recruiter"));
    }

    [Fact]
    public async Task Register_Rejects_Admin_Role_Escalation_And_Duplicate_Login()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var roleEscalationResponse = await PostFormAsync(client, "/Account/Register", new Dictionary<string, string>
        {
            ["FullName"] = "Хакер Адмін",
            ["Email"] = "hacker@company.ua",
            ["Login"] = "hacker.admin",
            ["Role"] = "Admin",
            ["Password"] = "securePass123",
            ["ConfirmPassword"] = "securePass123"
        });

        Assert.Equal(HttpStatusCode.OK, roleEscalationResponse.StatusCode);

        var duplicateLoginResponse = await PostFormAsync(client, "/Account/Register", new Dictionary<string, string>
        {
            ["FullName"] = "Дублікат Логіна",
            ["Email"] = "unique.mail@company.ua",
            ["Login"] = "admin",
            ["Role"] = "Recruiter",
            ["Password"] = "securePass123",
            ["ConfirmPassword"] = "securePass123"
        });

        Assert.Equal(HttpStatusCode.OK, duplicateLoginResponse.StatusCode);
    }

    [Fact]
    public async Task Profile_Page_Loads_And_Updates_Display_Name()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var profileResponse = await client.GetAsync("/Account/Profile");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);

        var updateResponse = await PostFormAsync(client, "/Account/Profile", new Dictionary<string, string>
        {
            ["Id"] = "2",
            ["FullName"] = "Оновлений Рекрутер",
            ["Email"] = "recruiter.updated@example.com"
        });

        Assert.Equal(HttpStatusCode.Redirect, updateResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recruiter = await db.Recruiters.FirstAsync(r => r.Login == "recruiter");
        Assert.Equal("Оновлений Рекрутер", recruiter.FullName);
    }

    [Fact]
    public async Task Application_Quick_Status_Update_Works()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await PostFormAsync(client, "/Applications", "/Applications/UpdateStatus", new Dictionary<string, string>
        {
            ["id"] = "1",
            ["status"] = "Offer"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var app = await db.Applications.FirstAsync(a => a.Id == 1);
        Assert.Equal("Offer", app.Status);
    }

    [Fact]
    public void ResumeIntelligenceService_Extracts_Skills_And_Contact_Correctly()
    {
        var service = new ResumeIntelligenceService();
        var sampleText = @"
            Олександр Шевченко
            Senior .NET Developer
            Email: oleksandr.dev@example.com
            Телефон: +380 50 123 4567
            Досвід роботи: 5 років комерційної розробки на C#, ASP.NET Core, PostgreSQL, Docker, Kubernetes та React.
            Архітектура Clean Architecture, Microservices, REST API.
        ";

        var parsed = service.ParseText(sampleText);

        Assert.NotNull(parsed);
        Assert.Equal("oleksandr.dev@example.com", parsed.DetectedEmail);
        Assert.Equal("+380 50 123 4567", parsed.DetectedPhone);
        Assert.Equal(5, parsed.DetectedExperienceYears);
        Assert.Contains("C#", parsed.DetectedSkills);
        Assert.Contains(".NET", parsed.DetectedSkills);
        Assert.Contains("PostgreSQL", parsed.DetectedSkills);
        Assert.Contains("Docker", parsed.DetectedSkills);
    }

    [Fact]
    public async Task Interviewer_Registration_And_Dashboard_Displays_Successfully()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var regResponse = await PostFormAsync(client, "/Account/Register", new Dictionary<string, string>
        {
            ["FullName"] = "Інтерв'юер Тестовий",
            ["Email"] = "interviewer.test@example.com",
            ["Login"] = "interviewer_test",
            ["Password"] = "interviewer123",
            ["ConfirmPassword"] = "interviewer123",
            ["Role"] = "Interviewer"
        });

        Assert.Equal(HttpStatusCode.Redirect, regResponse.StatusCode);

        // Verify in database
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Recruiters.FirstOrDefaultAsync(r => r.Login == "interviewer_test");
        Assert.NotNull(user);
        Assert.Equal("Interviewer", user.Role);

        // Access Interviewer Dashboard page
        var dashboardResponse = await client.GetAsync("/Home/Index");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        var html = await dashboardResponse.Content.ReadAsStringAsync();
        Assert.Contains("Призначені співбесіди", html);
        Assert.Contains("Останні експертні відгуки", html);
        Assert.Contains("Матриця оцінок Soft Skills", html);
        Assert.Contains("Швидкі дії інтерв'юера", html);
    }

    [Fact]
    public async Task Kanban_Page_Loads_And_Renders_Pipeline_Stages()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await client.GetAsync("/Applications/Kanban");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Канбан-дошка пайплайну", html);
        Assert.Contains("kanban-board", html);
        Assert.Contains("kanban-column", html);
        Assert.Contains("HR-скринінг", html);
        Assert.Contains("Співбесіди", html);
        Assert.Contains("Оффер", html);
    }

    [Fact]
    public async Task Applications_UpdateStatusAjax_Updates_Stage_Successfully()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var content = JsonContent.Create(new { Id = 1, Status = "Interview" });

        var response = await client.PostAsync("/Applications/UpdateStatusAjax", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"status\":\"Interview\"", json);

        // Verify in DB
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var app = await db.Applications.FirstAsync(a => a.Id == 1);
        Assert.Equal("Interview", app.Status);
    }

    [Fact]
    public async Task Candidates_QuickViewJson_Returns_Candidate_Details()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var response = await client.GetAsync("/Candidates/QuickViewJson?id=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"fullName\":", json);
        Assert.Contains("\"email\":", json);
        Assert.Contains("\"applications\":", json);
    }

    [Fact]
    public async Task Candidates_AddNoteAjax_Saves_Recruiter_Comment()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var content = JsonContent.Create(new { CandidateId = 1, ApplicationId = (int?)null, Note = "Перевірено навички, гарне враження" });
        var response = await client.PostAsync("/Candidates/AddNoteAjax", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var note = doc.RootElement.GetProperty("note").GetString();
        Assert.NotNull(note);
        Assert.Contains("Перевірено навички", note);
    }

    [Fact]
    public async Task Candidates_QuickImportParse_Extracts_Structured_Data()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var rawText = @"Дмитро Кравченко
dmitry.kravchenko@gmail.com
+380671112233
Senior .NET Developer, 7 років досвіду
Стек: C#, .NET, PostgreSQL, Docker, React, AWS";

        var content = JsonContent.Create(new { RawText = rawText });

        var response = await client.PostAsync("/Candidates/QuickImportParse", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
        Assert.Contains("dmitry.kravchenko@gmail.com", json);
        Assert.Contains("C#", json);
        Assert.Contains(".NET", json);
        Assert.Contains("PostgreSQL", json);
        Assert.Contains("\"experienceYears\":7", json);
    }

    [Fact]
    public async Task Careers_PublicBoard_AllowsBrowsingAndApplyingSuccessfully()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        // 1. Browse public careers board anonymously
        var indexResponse = await client.GetAsync("/Careers");
        Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("Приєднуйтесь до нашої команди", indexHtml);

        // 2. View details of first vacancy
        var detailsResponse = await client.GetAsync("/Careers/Details/1");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Подати відгук", detailsHtml);

        // 3. Submit application
        var applyFields = new Dictionary<string, string>
        {
            ["vacancyId"] = "1",
            ["fullName"] = "Максим Карпенко",
            ["email"] = "karpenko.careers@example.com",
            ["phone"] = "+380501112233",
            ["experienceYears"] = "4",
            ["skills"] = "C#, ASP.NET Core, PostgreSQL",
            ["coverLetter"] = "Доброго дня, зацікавила вакансія!"
        };

        var applyResponse = await PostFormAsync(client, "/Careers/Details/1", "/Careers/Apply", applyFields);
        Assert.Equal(HttpStatusCode.Redirect, applyResponse.StatusCode);
        Assert.Contains("/Careers/Success", applyResponse.Headers.Location?.OriginalString);

        // 4. Verify candidate and application in DB
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var candidate = await db.Candidates.FirstOrDefaultAsync(c => c.Email == "karpenko.careers@example.com");
        Assert.NotNull(candidate);
        Assert.Equal("Максим Карпенко", candidate.FullName);

        var application = await db.Applications.FirstOrDefaultAsync(a => a.CandidateId == candidate.Id && a.VacancyId == 1);
        Assert.NotNull(application);
        Assert.Equal("New", application.Status);

        var notification = await db.AppNotifications.FirstOrDefaultAsync(n => n.Message.Contains(candidate.FullName));
        Assert.NotNull(notification);
    }

    [Fact]
    public async Task Notifications_RecruiterCanFetchAndMarkAsRead()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        // 1. Fetch unread notifications
        var getResponse = await client.GetAsync("/Notifications/GetUnread");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var json = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"count\":", json);

        // 2. Mark all as read
        var markResponse = await client.PostAsync("/Notifications/MarkAllAsRead", null);
        Assert.Equal(HttpStatusCode.OK, markResponse.StatusCode);

        // 3. Verify count is now 0
        var getAfterResponse = await client.GetAsync("/Notifications/GetUnread");
        var afterJson = await getAfterResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"count\":0", afterJson);
    }

    [Fact]
    public async Task Candidates_SendMessageAjax_RecordsCommunicationAndNotification()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var payload = new
        {
            CandidateId = 1,
            Subject = "Запрошення на HR-скринінг",
            Body = "Доброго дня! Будемо раді поспілкуватися на короткій онлайн-зустрічі.",
            Channel = "Email",
            TemplateType = "screening"
        };

        var response = await client.PostAsync("/Candidates/SendMessageAjax", JsonContent.Create(payload));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);

        // Verify in database
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var comm = await db.CandidateCommunications.FirstOrDefaultAsync(c => c.CandidateId == 1 && c.Subject == "Запрошення на HR-скринінг");
        Assert.NotNull(comm);
        Assert.Equal("Email", comm.Channel);
        Assert.Equal("Sent", comm.Status);
    }

    [Fact]
    public async Task Anonymous_User_Cannot_Access_Direct_Resume_Uploads()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/uploads/resumes/sensitive_candidate_cv.pdf");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_User_Cannot_Download_Resume_Without_Auth()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/Candidates/DownloadResume/1");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Recruiter_Can_Download_Resume_When_Authenticated()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        // Upload allowed resume first
        var uploadResponse = await UploadResumeAsync(client, "/Candidates/UploadResume/1", "candidate_cv.pdf", "%PDF-1.4 test"u8.ToArray());
        Assert.Equal(HttpStatusCode.Redirect, uploadResponse.StatusCode);

        // Download through authorized endpoint
        var downloadResponse = await client.GetAsync("/Candidates/DownloadResume/1");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("application/pdf", downloadResponse.Content.Headers.ContentType?.MediaType);
        var bytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task BackgroundEmailQueue_Processes_WorkItem_Asynchronously()
    {
        using var factory = new HrReserveWebApplicationFactory();
        var queue = factory.Services.GetRequiredService<IEmailBackgroundQueue>();
        var tcs = new TaskCompletionSource<bool>();

        await queue.QueueBackgroundWorkItemAsync((sp, ct) =>
        {
            tcs.TrySetResult(true);
            return ValueTask.CompletedTask;
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, completed);
        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task Careers_Apply_Enforces_RateLimiting_On_Excessive_Requests()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 6; i++)
        {
            var applyFields = new Dictionary<string, string>
            {
                ["vacancyId"] = "1",
                ["fullName"] = $"Тестовий Кандидат {i}",
                ["email"] = $"rate.limit.test.{i}@example.com",
                ["phone"] = "+380501112233",
                ["experienceYears"] = "3",
                ["skills"] = "C#",
                ["coverLetter"] = "Rate limit test"
            };
            responses.Add(await PostFormAsync(client, "/Careers/Details/1", "/Careers/Apply", applyFields));
        }

        // Within 10-minute window with permit limit 5, the 6th request is rejected with 429 Too Many Requests
        var lastResponse = responses.Last();
        Assert.Equal((HttpStatusCode)429, lastResponse.StatusCode);
    }

    [Fact]
    public async Task Careers_Apply_Handles_Concurrent_Submissions_Gracefully()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client1 = CreateClient(factory);
        using var client2 = CreateClient(factory);

        var email = $"race.candidate.{Guid.NewGuid():N}@example.com";
        var applyFields1 = new Dictionary<string, string>
        {
            ["vacancyId"] = "1",
            ["fullName"] = "Паралельний Кандидат",
            ["email"] = email,
            ["phone"] = "+380509998877",
            ["experienceYears"] = "4",
            ["skills"] = "C#, .NET, Docker",
            ["coverLetter"] = "Потік 1"
        };
        var applyFields2 = new Dictionary<string, string>
        {
            ["vacancyId"] = "1",
            ["fullName"] = "Паралельний Кандидат",
            ["email"] = email,
            ["phone"] = "+380509998877",
            ["experienceYears"] = "4",
            ["skills"] = "C#, .NET, Docker",
            ["coverLetter"] = "Потік 2"
        };

        var token1 = await GetAntiforgeryTokenAsync(client1, "/Careers/Details/1");
        var token2 = await GetAntiforgeryTokenAsync(client2, "/Careers/Details/1");
        applyFields1["__RequestVerificationToken"] = token1;
        applyFields2["__RequestVerificationToken"] = token2;

        var task1 = client1.PostAsync("/Careers/Apply", new FormUrlEncodedContent(applyFields1));
        var task2 = client2.PostAsync("/Careers/Apply", new FormUrlEncodedContent(applyFields2));

        var results = await Task.WhenAll(task1, task2);

        // Both concurrent requests must successfully redirect to /Careers/Success without 500 error
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
        Assert.All(results, r => Assert.Contains("/Careers/Success", r.Headers.Location?.OriginalString));
    }

    [Fact]
    public async Task AuditLogs_Requires_Admin_Role()
    {
        using var factory = new HrReserveWebApplicationFactory();

        // 1. Anonymous user redirected to Login
        using (var anonClient = CreateClient(factory))
        {
            var anonResponse = await anonClient.GetAsync("/AuditLogs");
            Assert.Equal(HttpStatusCode.Redirect, anonResponse.StatusCode);
            Assert.Contains("/Account/Login", anonResponse.Headers.Location?.OriginalString);
        }

        // 2. Recruiter user redirected to AccessDenied
        using (var recruiterClient = CreateClient(factory))
        {
            await LoginAsync(recruiterClient, "recruiter", "recruiter123");
            var recruiterResponse = await recruiterClient.GetAsync("/AuditLogs");
            AssertAccessDeniedRedirect(recruiterResponse);
        }

        // 3. Admin user allowed with 200 OK
        using (var adminClient = CreateClient(factory))
        {
            await LoginAsync(adminClient, "admin", "admin123");
            var adminResponse = await adminClient.GetAsync("/AuditLogs");
            Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
            var content = await adminResponse.Content.ReadAsStringAsync();
            Assert.Contains("Журнал аудиту безпеки", content);
        }
    }

    [Fact]
    public async Task Sensitive_Operations_Generate_Audit_Logs()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        // 1. Export CSV
        var exportResponse = await client.GetAsync("/Candidates/ExportCsv");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);

        // Verify audit log exists
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var auditEntry = await db.AuditLogs.FirstOrDefaultAsync(l => l.Action == "ExportCsv");
            Assert.NotNull(auditEntry);
            Assert.Equal("Candidate", auditEntry.EntityName);
            Assert.Equal("admin", auditEntry.UserName);
        }
    }

    [Fact]
    public void CandidateExportService_Builds_Valid_Csv()
    {
        var exportService = new CandidateExportService();
        var candidates = new List<Candidate>
        {
            new()
            {
                Id = 1,
                FullName = "Олександр Коваленко",
                Email = "alex@example.com",
                Phone = "+380501234567",
                Skills = "C#, .NET, SQL",
                ExperienceYears = 5,
                ResumeSummary = "Senior .NET Engineer",
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var bytes = exportService.BuildCandidatesCsv(candidates);
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Id,FullName,Email,Phone,ExperienceYears,Skills", csv);
        Assert.Contains("Олександр Коваленко", csv);
        Assert.Contains("alex@example.com", csv);
    }

    [Fact]
    public async Task SendMessageAjax_Persists_EmailOutboxMessage()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "recruiter", "recruiter123");

        var token = await GetAntiforgeryTokenAsync(client, "/Candidates/Details/1");
        var requestObj = new
        {
            candidateId = 1,
            subject = "Тестове запрошення на співбесіду",
            body = "Доброго дня! Запрошуємо вас на технічну зустріч.",
            channel = "Email",
            templateType = "Screening"
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/Candidates/SendMessageAjax")
        {
            Content = JsonContent.Create(requestObj)
        };
        requestMessage.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(requestMessage);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Check EmailOutboxMessages table
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var outboxItem = await db.EmailOutboxMessages
                .FirstOrDefaultAsync(m => m.Subject == "Тестове запрошення на співбесіду");
            Assert.NotNull(outboxItem);
            Assert.Equal("Screening", outboxItem.TemplateType);

            // Also verify audit log
            var audit = await db.AuditLogs
                .FirstOrDefaultAsync(a => a.Action == "SendOutreachEmail" && a.EntityId == "1");
            Assert.NotNull(audit);
        }
    }

    [Fact]
    public async Task Account_Login_Success_And_Failure_Records_AuditLogs()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);

        // Failed login
        var failResponse = await LoginAsync(client, "admin", "wrongpassword");
        Assert.Equal(HttpStatusCode.OK, failResponse.StatusCode);

        // Successful login
        var successResponse = await LoginAsync(client, "admin", "admin123");
        Assert.Equal(HttpStatusCode.Redirect, successResponse.StatusCode);

        // Logout
        var logoutResponse = await PostFormAsync(client, "/Home/Index", "/Account/Logout", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);

        // Verify in DB
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var failedLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "LoginFailed" && a.EntityName == "Account");
        Assert.NotNull(failedLog);
        Assert.Contains("admin", failedLog.Details);

        var successLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "LoginSuccess" && a.EntityName == "Account");
        Assert.NotNull(successLog);
        Assert.Contains("Адміністратор", successLog.Details);

        var logoutLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "Logout" && a.EntityName == "Account");
        Assert.NotNull(logoutLog);
    }

    [Fact]
    public void ApplicationDbContext_Model_Configures_Covering_Indexes()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var candidateEntity = db.Model.FindEntityType(typeof(Candidate));
        Assert.NotNull(candidateEntity);
        var candidateIndexes = candidateEntity.GetIndexes();
        Assert.Contains(candidateIndexes, idx => idx.Properties.Any(p => p.Name == "IsDeleted") && idx.Properties.Any(p => p.Name == "FullName"));

        var auditEntity = db.Model.FindEntityType(typeof(AuditLog));
        Assert.NotNull(auditEntity);
        var auditIndexes = auditEntity.GetIndexes();
        Assert.Contains(auditIndexes, idx => idx.Properties.Any(p => p.Name == "Action") && idx.Properties.Any(p => p.Name == "CreatedAt"));

        var outboxEntity = db.Model.FindEntityType(typeof(EmailOutboxMessage));
        Assert.NotNull(outboxEntity);
        var outboxIndexes = outboxEntity.GetIndexes();
        Assert.Contains(outboxIndexes, idx => idx.Properties.Any(p => p.Name == "Status") && idx.Properties.Any(p => p.Name == "CreatedAt"));
    }

    [Fact]
    public async Task AuditLogs_Outbox_Endpoint_Returns_View_With_Metrics()
    {
        using var factory = new HrReserveWebApplicationFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", "admin123");

        var response = await client.GetAsync("/AuditLogs/Outbox");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Черга пошти (Transactional Outbox)", html);
        Assert.Contains("Очікують відправки", html);
    }

    [Fact]
    public async Task CandidateApplicationService_Moves_Stage_Cleanly()
    {
        using var factory = new HrReserveWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var appService = scope.ServiceProvider.GetRequiredService<ICandidateApplicationService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstApp = await db.Applications.FirstAsync();
        var (success, error) = await appService.MoveApplicationStageAsync(firstApp.Id, "Interview", null);

        Assert.True(success);
        Assert.Null(error);

        var updated = await db.Applications.FirstAsync(a => a.Id == firstApp.Id);
        Assert.Equal("Interview", updated.Status);
    }

    private static async Task<InterviewPostData> GetNewInterviewPostDataAsync(HrReserveWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var application = await db.Applications
            .AsNoTracking()
            .Include(item => item.Candidate)
            .Include(item => item.Vacancy)
            .OrderBy(item => item.Id)
            .FirstAsync();
        var recruiter = await db.Recruiters
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .FirstAsync(item => item.Login == "recruiter");

        return new InterviewPostData(
            InterviewId: 0,
            ApplicationId: application.Id,
            CandidateName: application.Candidate!.FullName,
            CandidateEmail: application.Candidate.Email,
            VacancyTitle: application.Vacancy!.Title,
            RecruiterId: recruiter.Id,
            RecruiterEmail: recruiter.Email);
    }

    private static async Task<InterviewPostData> GetExistingInterviewPostDataAsync(HrReserveWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var interview = await db.Interviews
            .AsNoTracking()
            .Include(item => item.Application)
                .ThenInclude(application => application!.Candidate)
            .Include(item => item.Application)
                .ThenInclude(application => application!.Vacancy)
            .Include(item => item.Recruiter)
            .OrderBy(item => item.Id)
            .FirstAsync();

        return new InterviewPostData(
            InterviewId: interview.Id,
            ApplicationId: interview.ApplicationId,
            CandidateName: interview.Application!.Candidate!.FullName,
            CandidateEmail: interview.Application.Candidate.Email,
            VacancyTitle: interview.Application.Vacancy!.Title,
            RecruiterId: interview.RecruiterId,
            RecruiterEmail: interview.Recruiter?.Email);
    }

    private static string AssertSingleOutboxMessage(HrReserveWebApplicationFactory factory)
    {
        Assert.True(Directory.Exists(factory.OutboxPath), $"Outbox directory was not created: {factory.OutboxPath}");
        var filePath = Assert.Single(Directory.GetFiles(factory.OutboxPath, "*.txt"));
        return File.ReadAllText(filePath);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password)
    {
        return await PostFormAsync(client, "/Account/Login", new Dictionary<string, string>
        {
            ["Login"] = login,
            ["Password"] = password,
            ["RememberMe"] = "false"
        });
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, path);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPath, string postPath, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, tokenPath);
        return await client.PostAsync(postPath, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> UploadResumeAsync(HttpClient client, string path, string fileName, byte[] bytes)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Candidates/Details/1");
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new ByteArrayContent(bytes), "resumeFile", fileName);

        return await client.PostAsync(path, content);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
        {
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        }

        Assert.True(match.Success, $"Antiforgery token was not found on {path}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<string?> GetCandidateResumePathAsync(HrReserveWebApplicationFactory factory, int candidateId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Candidates
            .Where(candidate => candidate.Id == candidateId)
            .Select(candidate => candidate.ResumeFilePath)
            .FirstAsync();
    }

    private static void AssertAccessDeniedRedirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.OriginalString);
    }
}
