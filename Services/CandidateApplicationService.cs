using HRReserveSystem.Data;
using HRReserveSystem.Hubs;
using HRReserveSystem.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class CandidateApplicationService(
    ApplicationDbContext context,
    ICandidateFileStorageService fileStorage,
    ResumeIntelligenceService resumeIntelligence,
    IHubContext<KanbanHub> hubContext,
    IAuditService auditService,
    ILogger<CandidateApplicationService> logger) : ICandidateApplicationService
{
    private static readonly string[] AllowedResumeExtensions = [".pdf", ".doc", ".docx"];
    private const long MaxResumeFileSize = 5 * 1024 * 1024;

    public async Task<(bool Success, string? Error, Candidate Candidate, Application Application)> ApplyFromCareersAsync(
        string fullName,
        string email,
        string? phone,
        string? linkedinUrl,
        string? telegramUsername,
        string? portfolioUrl,
        string? notes,
        string? clientIp,
        IFormFile resumeFile,
        int vacancyId,
        CancellationToken ct = default)
    {
        var vacancy = await context.Vacancies.FirstOrDefaultAsync(v => v.Id == vacancyId && !v.IsArchived, ct);
        if (vacancy is null)
        {
            return (false, "Вакансію не знайдено або вона вже закрита.", null!, null!);
        }

        var cleanEmail = email.Trim().ToLowerInvariant();
        var candidate = await context.Candidates.FirstOrDefaultAsync(c => c.Email.ToLower() == cleanEmail && !c.IsDeleted, ct);

        if (candidate is null)
        {
            candidate = new Candidate
            {
                FullName = fullName.Trim(),
                Email = cleanEmail,
                Phone = phone?.Trim(),
                Skills = "Заявка через кар'єрний портал",
                CreatedAt = DateTime.UtcNow
            };
            context.Candidates.Add(candidate);

            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                context.Entry(candidate).State = EntityState.Detached;
                candidate = await context.Candidates.FirstOrDefaultAsync(c => c.Email.ToLower() == cleanEmail && !c.IsDeleted, ct);
                if (candidate is null) throw;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(phone)) candidate.Phone = phone.Trim();
            await context.SaveChangesAsync(ct);
        }

        if (resumeFile is not null && resumeFile.Length > 0)
        {
            var ext = Path.GetExtension(resumeFile.FileName).ToLowerInvariant();
            if (AllowedResumeExtensions.Contains(ext) && resumeFile.Length <= MaxResumeFileSize)
            {
                var storedPath = await fileStorage.SaveResumeFileAsync(resumeFile);
                if (!string.IsNullOrWhiteSpace(storedPath))
                {
                    candidate.ResumeFilePath = storedPath;
                    var physicalPath = fileStorage.GetPhysicalResumePath(storedPath);
                    if (System.IO.File.Exists(physicalPath))
                    {
                        var parsed = resumeIntelligence.ParseResumeFile(physicalPath);
                        if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
                        {
                            candidate.ResumeSummary = parsed.Summary;
                        }
                    }
                    await context.SaveChangesAsync(ct);
                }
            }
        }

        var application = await context.Applications.FirstOrDefaultAsync(a => a.CandidateId == candidate.Id && a.VacancyId == vacancyId, ct);
        if (application is null)
        {
            var defaultStage = await context.PipelineStages
                .AsNoTracking()
                .Where(s => s.Code == "New" && (s.VacancyId == vacancyId || s.IsDefault))
                .OrderBy(s => s.Order)
                .FirstOrDefaultAsync(ct);

            application = new Application
            {
                CandidateId = candidate.Id,
                VacancyId = vacancyId,
                Status = "New",
                StageId = defaultStage?.Id,
                AppliedAt = DateTime.UtcNow,
                RecruiterComment = string.IsNullOrWhiteSpace(notes) ? "Відгук через публічний кар'єрний портал" : $"Супровідний лист: {notes.Trim()}"
            };

            context.Applications.Add(application);
            context.AppNotifications.Add(new AppNotification
            {
                Title = $"Новий відгук: {vacancy.Title}",
                Message = $"Кандидат: {candidate.FullName} ({candidate.Email})",
                TargetUrl = $"/Applications/Kanban?vacancyId={vacancyId}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                context.Entry(application).State = EntityState.Detached;
                application = await context.Applications.FirstOrDefaultAsync(a => a.CandidateId == candidate.Id && a.VacancyId == vacancyId, ct);
                if (application is null) throw;
            }

            try
            {
                await hubContext.Clients.Group($"vacancy-{vacancyId}").SendAsync("ApplicationCreated", new
                {
                    id = application.Id,
                    candidateName = candidate.FullName,
                    vacancyId = vacancyId,
                    status = "New"
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to broadcast ApplicationCreated via SignalR");
            }
        }

        return (true, null, candidate, application);
    }

    public async Task<(bool Success, string? Error)> MoveApplicationStageAsync(
        int applicationId,
        string targetStatus,
        int? targetStageId,
        CancellationToken ct = default)
    {
        if (!HrOptions.ApplicationStatuses.Contains(targetStatus))
        {
            return (false, "Некоректний статус етапу відбору.");
        }

        var application = await context.Applications
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);

        if (application is null)
        {
            return (false, "Заявку не знайдено.");
        }

        application.Status = targetStatus;
        if (targetStageId.HasValue && targetStageId.Value > 0)
        {
            application.StageId = targetStageId.Value;
        }

        if (targetStatus == "Offer" || targetStatus == "Hired")
        {
            context.AppNotifications.Add(new AppNotification
            {
                Title = targetStatus == "Hired" ? "Кандидата прийнято на роботу!" : "Кандидату виставлено офер!",
                Message = $"{application.Candidate?.FullName ?? "Кандидата"} переведено на етап «{HrOptions.ApplicationStatusLabel(targetStatus)}»",
                TargetUrl = $"/Applications/Kanban?vacancyId={application.VacancyId}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync(ct);
        await auditService.LogAsync(
            "UpdateCandidateStatus",
            "Application",
            application.Id.ToString(),
            $"Зміна статусу кандидата {application.Candidate?.FullName} на «{HrOptions.ApplicationStatusLabel(targetStatus)}»");

        try
        {
            var payload = new
            {
                id = application.Id,
                candidateName = application.Candidate?.FullName,
                vacancyId = application.VacancyId,
                status = application.Status,
                stageId = application.StageId,
                updatedBy = "Система/Рекрутер"
            };

            await hubContext.Clients.Group($"vacancy-{application.VacancyId}").SendAsync("ApplicationMoved", payload, ct);
            await hubContext.Clients.Group("vacancy-all").SendAsync("ApplicationMoved", payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast ApplicationMoved via SignalR");
        }

        return (true, null);
    }

    public async Task<(List<Application> Items, int TotalItems, int TotalPages)> GetApplicationsAsync(string? status, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        IQueryable<Application> query = context.Applications
            .AsNoTracking()
            .Include(application => application.Candidate)
            .Include(application => application.Vacancy)
            .Where(application => application.Candidate != null && !application.Candidate.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(application => application.Status == status);
        }

        var totalItems = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        if (totalPages > 0 && page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(application => application.AppliedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalItems, totalPages);
    }

    public async Task<Application?> GetApplicationDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Applications
            .AsNoTracking()
            .Include(item => item.Candidate)
            .Include(item => item.Vacancy)
            .Include(item => item.Interviews)
                .ThenInclude(interview => interview.Recruiter)
            .Include(item => item.Stage)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
    }

    public async Task<Application> CreateApplicationAsync(Application application, CancellationToken ct = default)
    {
        application.AppliedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(application.Status))
        {
            application.Status = "Applied";
        }

        context.Applications.Add(application);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("CreateApplication", "Application", application.Id.ToString(), $"Подано заявку на вакансію #{application.VacancyId}");
        return application;
    }

    public async Task<(bool Success, string? Error)> UpdateApplicationAsync(Application application, CancellationToken ct = default)
    {
        var existing = await context.Applications.FirstOrDefaultAsync(a => a.Id == application.Id, ct);
        if (existing is null)
        {
            return (false, "Заявку не знайдено.");
        }

        existing.VacancyId = application.VacancyId;
        existing.Status = application.Status;
        existing.RecruiterComment = application.RecruiterComment;
        existing.StageId = application.StageId;

        await context.SaveChangesAsync(ct);
        await auditService.LogAsync("UpdateApplication", "Application", application.Id.ToString(), $"Оновлено заявку #{application.Id}");
        return (true, null);
    }

    public async Task<bool> DeleteApplicationAsync(int id, CancellationToken ct = default)
    {
        var application = await context.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (application is null)
        {
            return false;
        }

        context.Applications.Remove(application);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("DeleteApplication", "Application", id.ToString(), $"Видалено заявку #{id}");
        return true;
    }

    public async Task<(List<Vacancy> Vacancies, List<PipelineStage> Stages, List<Application> Applications)> GetKanbanDataAsync(int? vacancyId, CancellationToken ct = default)
    {
        var vacancies = await context.Vacancies
            .AsNoTracking()
            .Where(v => !v.IsArchived)
            .OrderBy(v => v.Title)
            .ToListAsync(ct);

        var stages = await context.PipelineStages
            .AsNoTracking()
            .Where(s => s.VacancyId == vacancyId || s.IsDefault)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

        IQueryable<Application> query = context.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .Include(a => a.Vacancy)
            .Include(a => a.Interviews)
            .Include(a => a.Stage)
            .Where(a => a.Candidate != null && !a.Candidate.IsDeleted);

        if (vacancyId.HasValue && vacancyId.Value > 0)
        {
            query = query.Where(a => a.VacancyId == vacancyId.Value);
        }

        var applications = await query
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync(ct);

        return (vacancies, stages, applications);
    }

    public async Task<Application?> GetApplicationForEditAsync(int id, CancellationToken ct = default)
    {
        return await context.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<bool> ApplicationPairExistsAsync(int candidateId, int vacancyId, int? excludedApplicationId = null, CancellationToken ct = default)
    {
        return await context.Applications.AnyAsync(item =>
            item.CandidateId == candidateId &&
            item.VacancyId == vacancyId &&
            (!excludedApplicationId.HasValue || item.Id != excludedApplicationId.Value), ct);
    }

    public async Task<(List<Candidate> Candidates, List<Vacancy> Vacancies)> GetApplicationSelectListsDataAsync(CancellationToken ct = default)
    {
        var candidates = await context.Candidates
            .AsNoTracking()
            .Where(candidate => !candidate.IsDeleted)
            .OrderBy(candidate => candidate.FullName)
            .ToListAsync(ct);

        var vacancies = await context.Vacancies
            .AsNoTracking()
            .Where(vacancy => !vacancy.IsArchived)
            .OrderBy(vacancy => vacancy.Title)
            .ToListAsync(ct);

        return (candidates, vacancies);
    }

    public async Task<(bool Success, string? CandidateName, string? Error)> UpdateApplicationStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var application = await context.Applications
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (application is null)
        {
            return (false, null, "Заявку не знайдено.");
        }

        application.Status = status;
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("UpdateApplicationStatus", "Application", id.ToString(), $"Змінено статус на {status}");
        return (true, application.Candidate?.FullName, null);
    }

    public async Task<Application?> GetApplicationWithCandidateAsync(int id, CancellationToken ct = default)
    {
        return await context.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }
}
