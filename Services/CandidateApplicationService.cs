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
}
