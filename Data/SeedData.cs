using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Data;

public static class SeedData
{
    public static async Task InitializeAsync(
        ApplicationDbContext context,
        IdentityRecruiterSyncService identitySync,
        IPasswordHasher<Recruiter> passwordHasher)
    {
        await EnsureDatabaseSchemaUpdatedAsync(context);

        var recruiters = await EnsureRecruitersAsync(context, passwordHasher);
        await identitySync.SyncRecruitersAsync(recruiters);

        var databaseHasHrData =
            await context.Candidates.AnyAsync() ||
            await context.Vacancies.AnyAsync() ||
            await context.Applications.AnyAsync() ||
            await context.Interviews.AnyAsync() ||
            await context.InterviewFeedbacks.AnyAsync() ||
            await context.SoftSkillAssessments.AnyAsync();

        if (databaseHasHrData)
        {
            await NormalizeExistingRecordsAsync(context, recruiters);
            return;
        }

        var candidates = new List<Candidate>
        {
            new()
            {
                FullName = "Олена Коваль",
                Email = "olena.koval@example.com",
                Phone = "+380671112233",
                Skills = "ASP.NET Core, C#, SQL, HR analytics",
                ResumeSummary = "4 роки досвіду у .NET-розробці, внутрішніх HR-сервісах та роботі з SQL-звітами.",
                ExperienceYears = 4,
                CreatedAt = DateTime.UtcNow.AddDays(-14)
            },
            new()
            {
                FullName = "Андрій Мельник",
                Email = "andrii.melnyk@example.com",
                Phone = "+380501234567",
                Skills = "JavaScript, React, UI testing, REST API",
                ResumeSummary = "Frontend developer з досвідом побудови SPA, тестування UI та інтеграції з REST API.",
                ExperienceYears = 3,
                CreatedAt = DateTime.UtcNow.AddDays(-11)
            },
            new()
            {
                FullName = "Марія Шевченко",
                Email = "maria.shevchenko@example.com",
                Phone = "+380931112244",
                Skills = "Project management, communication, English B2",
                ResumeSummary = "Координаторка проєктів із сильними soft skills, досвідом комунікації з командами та клієнтами.",
                ExperienceYears = 6,
                CreatedAt = DateTime.UtcNow.AddDays(-8)
            },
            new()
            {
                FullName = "Дмитро Іваненко",
                Email = "dmytro.ivanenko@example.com",
                Phone = "+380681234567",
                Skills = "QA, test cases, Postman, SQL",
                ResumeSummary = "QA engineer з практикою ручного тестування, API-перевірок і підготовки тестової документації.",
                ExperienceYears = 2,
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            },
            new()
            {
                FullName = "Ірина Бондар",
                Email = "iryna.bondar@example.com",
                Phone = "+380991112255",
                Skills = "Recruiting, sourcing, interviews, CRM",
                ResumeSummary = "Рекрутерка з досвідом пошуку IT-кандидатів, проведення HR-інтерв'ю та ведення кадрового резерву.",
                ExperienceYears = 5,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            }
        };

        var vacancies = new List<Vacancy>
        {
            new()
            {
                Title = "Junior .NET Developer",
                Description = "Розробка внутрішніх HR-сервісів на ASP.NET Core MVC.",
                Requirements = ".NET 8, C#, SQL, базове розуміння MVC та Entity Framework Core.",
                SalaryMin = 900,
                SalaryMax = 1400,
                Status = "Open",
                CreatedAt = DateTime.UtcNow.AddDays(-12)
            },
            new()
            {
                Title = "HR Analyst",
                Description = "Аналіз кандидатів, підготовка звітів і підтримка рекрутингових процесів.",
                Requirements = "Excel, SQL, уважність до даних, комунікація, аналітичне мислення.",
                SalaryMin = 800,
                SalaryMax = 1300,
                Status = "Paused",
                CreatedAt = DateTime.UtcNow.AddDays(-9)
            },
            new()
            {
                Title = "QA Engineer",
                Description = "Тестування веб-застосунків, підготовка тест-кейсів і контроль якості релізів.",
                Requirements = "Test cases, Postman, SQL, базове розуміння HTTP та клієнт-серверної архітектури.",
                SalaryMin = 700,
                SalaryMax = 1200,
                Status = "Closed",
                CreatedAt = DateTime.UtcNow.AddDays(-6)
            }
        };

        var applications = new List<Application>
        {
            new() { Candidate = candidates[0], Vacancy = vacancies[0], Status = "Hired", AppliedAt = DateTime.UtcNow.AddDays(-10) },
            new() { Candidate = candidates[1], Vacancy = vacancies[0], Status = "Rejected", AppliedAt = DateTime.UtcNow.AddDays(-8) },
            new() { Candidate = candidates[2], Vacancy = vacancies[1], Status = "Interview", AppliedAt = DateTime.UtcNow.AddDays(-5) },
            new() { Candidate = candidates[3], Vacancy = vacancies[2], Status = "TestTask", AppliedAt = DateTime.UtcNow.AddDays(-3) },
            new() { Candidate = candidates[4], Vacancy = vacancies[1], Status = "Screening", AppliedAt = DateTime.UtcNow.AddDays(-1) }
        };

        var admin = recruiters.Single(recruiter => recruiter.Login == "admin");
        var recruiter = recruiters.Single(item => item.Login == "recruiter");
        var interviewer = recruiters.Single(item => item.Login == "interviewer");

        var interviews = new List<Interview>
        {
            new()
            {
                Application = applications[0],
                Recruiter = recruiter,
                InterviewDate = DateTime.Now.AddDays(-7),
                InterviewType = "Technical",
                Result = "Passed",
                Notes = "Кандидатка добре пояснила досвід з ASP.NET Core та EF Core."
            },
            new()
            {
                Application = applications[2],
                Recruiter = admin,
                InterviewDate = DateTime.Now.AddDays(2),
                InterviewType = "HR",
                Result = "Pending",
                Notes = "Потрібно перевірити мотивацію та очікування щодо ролі HR Analyst."
            },
            new()
            {
                Application = applications[1],
                Recruiter = recruiter,
                InterviewDate = DateTime.Now.AddDays(-4),
                InterviewType = "Final",
                Result = "Failed",
                Notes = "Кандидат не підтвердив потрібний рівень практичного досвіду."
            }
        };

        var feedbacks = new List<InterviewFeedback>
        {
            new()
            {
                Interview = interviews[0],
                Recruiter = interviewer,
                Comment = "Сильна технічна база, структурні відповіді, хороша командна взаємодія.",
                Score = 9,
                Recommendation = "Hire",
                CreatedAt = DateTime.UtcNow.AddDays(-7)
            },
            new()
            {
                Interview = interviews[1],
                Recruiter = recruiter,
                Comment = "Є потенціал для кадрового резерву, але варто уточнити очікування щодо аналітичних задач.",
                Score = 7,
                Recommendation = "Maybe",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new()
            {
                Interview = interviews[2],
                Recruiter = interviewer,
                Comment = "Недостатньо практичного досвіду для фінального етапу цієї вакансії.",
                Score = 4,
                Recommendation = "Reject",
                CreatedAt = DateTime.UtcNow.AddDays(-4)
            }
        };

        var assessments = new List<SoftSkillAssessment>
        {
            new()
            {
                Candidate = candidates[0],
                Communication = 9,
                Teamwork = 8,
                Responsibility = 9,
                StressResistance = 8,
                Leadership = 7,
                OverallComment = "Добре підходить для кадрового резерву на технічні ролі."
            },
            new()
            {
                Candidate = candidates[2],
                Communication = 8,
                Teamwork = 9,
                Responsibility = 8,
                StressResistance = 7,
                Leadership = 8,
                OverallComment = "Сильні управлінські навички та зріла комунікація."
            },
            new()
            {
                Candidate = candidates[4],
                Communication = 9,
                Teamwork = 8,
                Responsibility = 8,
                StressResistance = 7,
                Leadership = 9,
                OverallComment = "Добрий профіль для рекрутингової команди та роботи з кандидатами."
            }
        };

        context.Candidates.AddRange(candidates);
        context.Vacancies.AddRange(vacancies);
        context.Applications.AddRange(applications);
        context.Interviews.AddRange(interviews);
        context.InterviewFeedbacks.AddRange(feedbacks);
        context.SoftSkillAssessments.AddRange(assessments);

        await context.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<Recruiter>> EnsureRecruitersAsync(
        ApplicationDbContext context,
        IPasswordHasher<Recruiter> passwordHasher)
    {
        var createdAt = DateTime.UtcNow;

        foreach (var demoUser in DemoCredentials.Users)
        {
            var recruiter = await context.Recruiters.FirstOrDefaultAsync(item => item.Login == demoUser.Login);
            if (recruiter is null)
            {
                recruiter = new Recruiter
                {
                    FullName = demoUser.FullName,
                    Email = demoUser.Email,
                    Login = demoUser.Login,
                    Role = demoUser.Role,
                    CreatedAt = createdAt.AddDays(demoUser.Role switch
                    {
                        "Admin" => -30,
                        "Recruiter" => -25,
                        _ => -20
                    })
                };

                recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, demoUser.Password);
                context.Recruiters.Add(recruiter);
                continue;
            }

            recruiter.FullName = demoUser.FullName;
            recruiter.Email = demoUser.Email;
            recruiter.Role = demoUser.Role;

            if (!PasswordMatches(passwordHasher, recruiter, demoUser.Password))
            {
                recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, demoUser.Password);
            }
        }

        await NormalizeExistingPasswordHashesAsync(context, passwordHasher);
        await context.SaveChangesAsync();

        return await context.Recruiters
            .OrderBy(recruiter => recruiter.Id)
            .ToListAsync();
    }

    private static async Task NormalizeExistingPasswordHashesAsync(
        ApplicationDbContext context,
        IPasswordHasher<Recruiter> passwordHasher)
    {
        var recruiters = await context.Recruiters.ToListAsync();
        foreach (var recruiter in recruiters)
        {
            if (!string.IsNullOrWhiteSpace(recruiter.PasswordHash) && LooksLikeIdentityPasswordHash(recruiter.PasswordHash))
            {
                continue;
            }

            var password = DemoCredentials.GetPassword(recruiter.Login);
            password ??= string.IsNullOrWhiteSpace(recruiter.PasswordHash) ? $"{recruiter.Login}123" : recruiter.PasswordHash;
            recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, password);
        }
    }

    private static bool PasswordMatches(IPasswordHasher<Recruiter> passwordHasher, Recruiter recruiter, string password)
    {
        try
        {
            return passwordHasher.VerifyHashedPassword(recruiter, recruiter.PasswordHash, password)
                is not PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool LooksLikeIdentityPasswordHash(string passwordHash)
    {
        try
        {
            var bytes = Convert.FromBase64String(passwordHash);
            return bytes.Length > 0 && bytes[0] == 0x01;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task NormalizeExistingRecordsAsync(ApplicationDbContext context, IReadOnlyList<Recruiter> recruiters)
    {
        var admin = recruiters.FirstOrDefault(item => item.Role == "Admin") ?? recruiters.First();
        var interviewer = recruiters.FirstOrDefault(item => item.Role == "Interviewer") ?? admin;

        var candidates = await context.Candidates.ToListAsync();
        foreach (var candidate in candidates.Where(candidate => candidate.ResumeFilePath?.StartsWith("/resumes/", StringComparison.OrdinalIgnoreCase) == true))
        {
            candidate.ResumeFilePath = null;
        }

        var applications = await context.Applications.ToListAsync();
        foreach (var application in applications)
        {
            application.Status = application.Status switch
            {
                "In review" => "Screening",
                "Accepted" => "Hired",
                _ when !HrOptions.ApplicationStatuses.Contains(application.Status) => "New",
                _ => application.Status
            };
        }

        var interviews = await context.Interviews.ToListAsync();
        foreach (var interview in interviews)
        {
            interview.RecruiterId ??= admin.Id;
            interview.InterviewType = interview.InterviewType switch
            {
                "HR screening" => "HR",
                "Online" => "HR",
                _ when !HrOptions.InterviewTypes.Contains(interview.InterviewType) => "HR",
                _ => interview.InterviewType
            };
            interview.Result = interview.Result switch
            {
                "Planned" => "Pending",
                "Rescheduled" => "Pending",
                _ when !HrOptions.InterviewResults.Contains(interview.Result) => "Pending",
                _ => interview.Result
            };
        }

        var feedbacks = await context.InterviewFeedbacks.ToListAsync();
        foreach (var feedback in feedbacks)
        {
            feedback.RecruiterId ??= interviewer.Id;
            feedback.Recommendation = feedback.Recommendation switch
            {
                "Reserve" => "Maybe",
                "Need more interviews" => "Maybe",
                _ when !HrOptions.FeedbackRecommendations.Contains(feedback.Recommendation) => "Maybe",
                _ => feedback.Recommendation
            };
        }

        await context.SaveChangesAsync();
    }

    private static async Task EnsureDatabaseSchemaUpdatedAsync(ApplicationDbContext context)
    {
        try
        {
            if (context.Database.IsSqlite())
            {
                var recruiterCols = await context.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Recruiters');").ToListAsync();
                if (!recruiterCols.Contains("AvatarUrl"))
                {
                    await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Recruiters\" ADD COLUMN \"AvatarUrl\" TEXT;");
                }

                var candidateCols = await context.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Candidates');").ToListAsync();
                if (!candidateCols.Contains("AvatarUrl"))
                {
                    await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Candidates\" ADD COLUMN \"AvatarUrl\" TEXT;");
                }
            }
            else
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Recruiters\" ADD COLUMN IF NOT EXISTS \"AvatarUrl\" text;");
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Candidates\" ADD COLUMN IF NOT EXISTS \"AvatarUrl\" text;");
            }
        }
        catch
        {
            // Ignore in-memory test databases or if already migrated
        }
    }
}
