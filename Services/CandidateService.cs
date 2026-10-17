using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class CandidateService(
    ApplicationDbContext context,
    ICandidateFileStorageService fileStorage,
    ResumeIntelligenceService resumeIntelligence,
    IEmailBackgroundQueue emailQueue,
    IAuditService auditService,
    ILogger<CandidateService> logger) : ICandidateService
{
    public async Task<(List<Candidate> Items, int TotalItems, int TotalPages)> GetCandidatesAsync(
        string? search,
        int? minExperience,
        bool showHidden = false,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        var query = ApplyFilters(context.Candidates.AsNoTracking().Where(candidate => candidate.IsDeleted == showHidden), search, minExperience);

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages) page = totalPages;

        var items = await query
            .OrderBy(candidate => candidate.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalItems, totalPages);
    }

    public async Task<List<Candidate>> GetCandidatesForExportAsync(
        string? search,
        int? minExperience,
        bool showHidden = false,
        CancellationToken ct = default)
    {
        return await ApplyFilters(context.Candidates.AsNoTracking().Where(candidate => candidate.IsDeleted == showHidden), search, minExperience)
            .OrderBy(candidate => candidate.FullName)
            .ToListAsync(ct);
    }

    public async Task<Candidate?> GetCandidateDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Candidates
            .AsNoTracking()
            .Include(item => item.Applications)
                .ThenInclude(application => application.Vacancy)
            .Include(item => item.SoftSkillAssessments)
            .Include(item => item.Communications)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
    }

    public async Task<bool> EmailExistsAsync(string email, int? excludeId = null, CancellationToken ct = default)
    {
        var cleanEmail = email.Trim().ToLowerInvariant();
        return await context.Candidates.AnyAsync(c =>
            c.Email.ToLower() == cleanEmail &&
            (!excludeId.HasValue || c.Id != excludeId.Value), ct);
    }

    public async Task<Candidate> CreateCandidateAsync(
        Candidate candidate,
        IFormFile? resumeFile,
        IFormFile? avatarFile,
        CancellationToken ct = default)
    {
        var uploadedResumePath = await fileStorage.SaveResumeFileAsync(resumeFile);
        if (!string.IsNullOrWhiteSpace(uploadedResumePath))
        {
            candidate.ResumeFilePath = uploadedResumePath;
            var fullPath = fileStorage.GetPhysicalResumePath(uploadedResumePath);
            var parsed = resumeIntelligence.ParseResumeFile(fullPath);
            if (parsed.Success && string.IsNullOrWhiteSpace(candidate.ResumeSummary) && !string.IsNullOrWhiteSpace(parsed.Summary))
            {
                candidate.ResumeSummary = parsed.Summary;
            }
        }

        var uploadedAvatarPath = await fileStorage.SaveAvatarFileAsync(avatarFile);
        if (!string.IsNullOrWhiteSpace(uploadedAvatarPath))
        {
            candidate.AvatarUrl = uploadedAvatarPath;
        }

        candidate.CreatedAt = DateTime.UtcNow;
        context.Candidates.Add(candidate);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("CreateCandidate", "Candidate", candidate.Id.ToString(), $"Створено кандидата {candidate.FullName}");
        return candidate;
    }

    public async Task<(bool Success, string? Error)> UpdateCandidateAsync(
        Candidate candidate,
        IFormFile? resumeFile,
        IFormFile? avatarFile,
        CancellationToken ct = default)
    {
        var existing = await context.Candidates.FirstOrDefaultAsync(c => c.Id == candidate.Id && !c.IsDeleted, ct);
        if (existing is null)
        {
            return (false, "Кандидата не знайдено.");
        }

        existing.FullName = candidate.FullName;
        existing.Email = candidate.Email;
        existing.Phone = candidate.Phone;
        existing.Skills = candidate.Skills;
        existing.ResumeSummary = candidate.ResumeSummary;
        existing.ExperienceYears = candidate.ExperienceYears;

        if (resumeFile is not null && resumeFile.Length > 0)
        {
            var oldResume = existing.ResumeFilePath;
            var newResume = await fileStorage.SaveResumeFileAsync(resumeFile);
            if (!string.IsNullOrWhiteSpace(newResume))
            {
                fileStorage.DeleteStoredResume(oldResume);
                existing.ResumeFilePath = newResume;
                var fullPath = fileStorage.GetPhysicalResumePath(newResume);
                var parsed = resumeIntelligence.ParseResumeFile(fullPath);
                if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
                {
                    existing.ResumeSummary = parsed.Summary;
                }
            }
        }

        if (avatarFile is not null && avatarFile.Length > 0)
        {
            var oldAvatar = existing.AvatarUrl;
            var newAvatar = await fileStorage.SaveAvatarFileAsync(avatarFile);
            if (!string.IsNullOrWhiteSpace(newAvatar))
            {
                fileStorage.DeleteStoredAvatar(oldAvatar);
                existing.AvatarUrl = newAvatar;
            }
        }

        await context.SaveChangesAsync(ct);
        await auditService.LogAsync("UpdateCandidate", "Candidate", candidate.Id.ToString(), $"Оновлено дані кандидата {candidate.FullName}");
        return (true, null);
    }

    public async Task<bool> SoftDeleteCandidateAsync(int id, CancellationToken ct = default)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
        if (candidate is null) return false;

        candidate.IsDeleted = true;
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("SoftDeleteCandidate", "Candidate", id.ToString(), $"Приховано кандидата {candidate.FullName}");
        return true;
    }

    public async Task<bool> RestoreCandidateAsync(int id, CancellationToken ct = default)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted, ct);
        if (candidate is null) return false;

        candidate.IsDeleted = false;
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("RestoreCandidate", "Candidate", id.ToString(), $"Відновлено кандидата {candidate.FullName}");
        return true;
    }

    public async Task<(bool Success, string? Error, string? NewResumePath)> UploadResumeAsync(
        int id,
        IFormFile resumeFile,
        CancellationToken ct = default)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, ct);
        if (candidate is null)
        {
            return (false, "Кандидата не знайдено.", null);
        }

        var oldResumePath = candidate.ResumeFilePath;
        var uploadedResumePath = await fileStorage.SaveResumeFileAsync(resumeFile);
        if (string.IsNullOrWhiteSpace(uploadedResumePath))
        {
            return (false, "Файл резюме не вибрано.", null);
        }

        fileStorage.DeleteStoredResume(oldResumePath);
        candidate.ResumeFilePath = uploadedResumePath;

        var fullPath = fileStorage.GetPhysicalResumePath(uploadedResumePath);
        var parsed = resumeIntelligence.ParseResumeFile(fullPath);
        if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
        {
            candidate.ResumeSummary = parsed.Summary;
        }

        await context.SaveChangesAsync(ct);
        await auditService.LogAsync("UploadResume", "Candidate", id.ToString(), $"Оновлено резюме кандидата {candidate.FullName}");
        return (true, null, uploadedResumePath);
    }

    public async Task<(bool Success, string? Error, string Note, string Timestamp, string Author)> AddNoteAsync(
        int candidateId,
        int? applicationId,
        string note,
        string author,
        CancellationToken ct = default)
    {
        var candidate = await context.Candidates
            .Include(c => c.Applications)
            .FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, ct);

        if (candidate is null)
        {
            return (false, "Кандидата не знайдено.", "", "", "");
        }

        var timestamp = DateTime.Now.ToString("dd.MM HH:mm");
        var newEntry = $"[{timestamp} {author}]: {note.Trim()}";

        var application = applicationId.HasValue
            ? candidate.Applications.FirstOrDefault(a => a.Id == applicationId.Value)
            : candidate.Applications.OrderByDescending(a => a.AppliedAt).FirstOrDefault();

        if (application is not null)
        {
            application.RecruiterComment = string.IsNullOrWhiteSpace(application.RecruiterComment)
                ? newEntry
                : $"{newEntry}\n{application.RecruiterComment}";
        }
        else
        {
            candidate.ResumeSummary = string.IsNullOrWhiteSpace(candidate.ResumeSummary)
                ? newEntry
                : $"{newEntry}\n{candidate.ResumeSummary}";
        }

        await context.SaveChangesAsync(ct);
        return (true, null, newEntry, timestamp, author);
    }

    public async Task<(bool Success, string? Error, CandidateCommunication? Communication)> SendMessageAsync(
        int candidateId,
        string subject,
        string body,
        string channel,
        string? templateType,
        string senderName,
        CancellationToken ct = default)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(c => c.Id == candidateId && !c.IsDeleted, ct);
        if (candidate is null)
        {
            return (false, "Кандидата не знайдено.", null);
        }

        if (string.IsNullOrWhiteSpace(candidate.Email))
        {
            return (false, "У кандидата відсутня електронна пошта.", null);
        }

        var recipientEmail = candidate.Email;
        var recipientName = candidate.FullName;
        var emailSubject = subject.Trim();
        var emailBody = body.Trim();

        var outboxMessage = new EmailOutboxMessage
        {
            RecipientEmail = recipientEmail,
            RecipientName = recipientName,
            Subject = emailSubject,
            Body = emailBody,
            TemplateType = templateType,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        context.EmailOutboxMessages.Add(outboxMessage);

        var communication = new CandidateCommunication
        {
            CandidateId = candidate.Id,
            SenderName = senderName,
            RecipientEmail = candidate.Email,
            Subject = emailSubject,
            Body = emailBody,
            Channel = string.IsNullOrWhiteSpace(channel) ? "Email" : channel,
            SentAt = DateTime.UtcNow,
            Status = "Sent"
        };
        context.CandidateCommunications.Add(communication);

        context.AppNotifications.Add(new AppNotification
        {
            Title = $"Надіслано листа кандидату {candidate.FullName}",
            Message = $"Тема: {emailSubject}",
            TargetUrl = $"/Candidates/Details/{candidate.Id}",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync(ct);

        await emailQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, token) =>
        {
            var service = serviceProvider.GetRequiredService<IEmailNotificationService>();
            var queueLogger = serviceProvider.GetRequiredService<ILogger<CandidateService>>();
            try
            {
                await service.SendCandidateMessageAsync(recipientEmail, recipientName, emailSubject, emailBody);
            }
            catch (Exception ex)
            {
                queueLogger.LogWarning(ex, "Background email sending encountered warning for {Email}", recipientEmail);
            }
        });

        await auditService.LogAsync("SendOutreachEmail", "Candidate", candidate.Id.ToString(), $"Надіслано лист кандидату {candidate.FullName}: {emailSubject}");
        logger.LogInformation("Outreach email queued and recorded for candidate {CandidateId} by {Sender}", candidate.Id, senderName);

        return (true, null, communication);
    }

    private static IQueryable<Candidate> ApplyFilters(IQueryable<Candidate> query, string? search, int? minExperience)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(candidate =>
                candidate.FullName.Contains(search) ||
                candidate.Email.Contains(search) ||
                candidate.Skills.Contains(search));
        }

        if (minExperience.HasValue)
        {
            query = query.Where(candidate => candidate.ExperienceYears >= minExperience.Value);
        }

        return query;
    }
}
