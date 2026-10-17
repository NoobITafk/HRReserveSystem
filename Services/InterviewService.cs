using System.Text;
using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class InterviewService(
    ApplicationDbContext context,
    IEmailNotificationService emailService,
    IAuditService auditService,
    ILogger<InterviewService> logger) : IInterviewService
{
    public async Task<(List<Interview> Items, int TotalItems, int TotalPages)> GetInterviewsAsync(string? interviewType, string? result, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        IQueryable<Interview> query = context.Interviews
            .AsNoTracking()
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Candidate)
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Vacancy)
            .Include(interview => interview.Recruiter);

        query = query.Where(interview => interview.Application == null || interview.Application.Candidate == null || !interview.Application.Candidate.IsDeleted);

        if (!string.IsNullOrWhiteSpace(interviewType))
        {
            query = query.Where(interview => interview.InterviewType == interviewType);
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            query = query.Where(interview => interview.Result == result);
        }

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(interview => interview.InterviewDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalItems, totalPages);
    }

    public async Task<List<Interview>> GetCalendarInterviewsAsync(CancellationToken ct = default)
    {
        return await context.Interviews
            .AsNoTracking()
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Candidate)
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Vacancy)
            .Include(interview => interview.Recruiter)
            .Where(interview => interview.Application == null || interview.Application.Candidate == null || !interview.Application.Candidate.IsDeleted)
            .OrderBy(interview => interview.InterviewDate)
            .ToListAsync(ct);
    }

    public async Task<Interview?> GetInterviewDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Interviews
            .AsNoTracking()
            .Include(item => item.Application)
                .ThenInclude(application => application!.Candidate)
            .Include(item => item.Application)
                .ThenInclude(application => application!.Vacancy)
            .Include(item => item.Recruiter)
            .Include(item => item.Feedbacks)
                .ThenInclude(feedback => feedback.Recruiter)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
    }

    public async Task<Interview> ScheduleInterviewAsync(Interview interview, CancellationToken ct = default)
    {
        context.Interviews.Add(interview);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            "ScheduleInterview",
            "Interview",
            interview.Id.ToString(),
            $"Заплановано співбесіду #{interview.Id} на {interview.InterviewDate:dd.MM.yyyy HH:mm}");

        try
        {
            await emailService.SendInterviewScheduledAsync(interview.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send interview scheduled notification for #{Id}", interview.Id);
        }

        return interview;
    }

    public async Task<(bool Success, string? Error)> UpdateInterviewAsync(Interview interview, CancellationToken ct = default)
    {
        var existing = await context.Interviews.FirstOrDefaultAsync(i => i.Id == interview.Id, ct);
        if (existing is null)
        {
            return (false, "Співбесіду не знайдено.");
        }

        existing.ApplicationId = interview.ApplicationId;
        existing.RecruiterId = interview.RecruiterId;
        existing.InterviewDate = interview.InterviewDate;
        existing.InterviewType = interview.InterviewType;
        existing.Result = interview.Result;
        existing.Notes = interview.Notes;

        await context.SaveChangesAsync(ct);
        await auditService.LogAsync("UpdateInterview", "Interview", interview.Id.ToString(), $"Оновлено дані співбесіди #{interview.Id}");

        try
        {
            await emailService.SendInterviewUpdatedAsync(interview.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send interview update notification for #{Id}", interview.Id);
        }

        return (true, null);
    }

    public async Task<bool> DeleteInterviewAsync(int id, CancellationToken ct = default)
    {
        var interview = await context.Interviews.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (interview is null)
        {
            return false;
        }

        context.Interviews.Remove(interview);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("DeleteInterview", "Interview", id.ToString(), $"Видалено співбесіду #{id}");
        return true;
    }

    public string GenerateIcsContent(Interview interview)
    {
        var start = DateTime.SpecifyKind(interview.InterviewDate, DateTimeKind.Local).ToUniversalTime();
        var end = start.AddHours(1);
        var uid = $"interview-{interview.Id}@hrreservesystem";
        var summary = $"Співбесіда: {interview.Application?.Candidate?.FullName ?? "Кандидат"} - {interview.Application?.Vacancy?.Title ?? "HR"}";
        var description = $"Тип: {interview.InterviewType}\\nКандидат: {interview.Application?.Candidate?.FullName}\\nEmail: {interview.Application?.Candidate?.Email}\\nІнтерв'юер: {interview.Recruiter?.FullName}";

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//HRReserveSystem//Calendar//UK");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:REQUEST");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}");
        sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd\\THHmmss\\Z}");
        sb.AppendLine($"DTSTART:{start:yyyyMMdd\\THHmmss\\Z}");
        sb.AppendLine($"DTEND:{end:yyyyMMdd\\THHmmss\\Z}");
        sb.AppendLine($"SUMMARY:{summary}");
        sb.AppendLine($"DESCRIPTION:{description}");
        sb.AppendLine("LOCATION:Онлайн / Офіс");
        sb.AppendLine("STATUS:CONFIRMED");
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

        return sb.ToString();
    }

    public string GetGoogleCalendarUrl(Interview interview)
    {
        var start = DateTime.SpecifyKind(interview.InterviewDate, DateTimeKind.Local).ToUniversalTime();
        var end = start.AddHours(1);
        var summary = $"Співбесіда: {interview.Application?.Candidate?.FullName ?? "Кандидат"} - {interview.Application?.Vacancy?.Title ?? "HR"}";
        var description = $"Тип: {interview.InterviewType}\nКандидат: {interview.Application?.Candidate?.FullName}\nEmail: {interview.Application?.Candidate?.Email}\nІнтерв'юер: {interview.Recruiter?.FullName}";

        return "https://calendar.google.com/calendar/render?action=TEMPLATE"
            + $"&text={Uri.EscapeDataString(summary)}"
            + $"&dates={start:yyyyMMdd\\THHmmss\\Z}/{end:yyyyMMdd\\THHmmss\\Z}"
            + $"&details={Uri.EscapeDataString(description)}"
            + $"&location={Uri.EscapeDataString("HRReserveSystem")}";
    }

    public async Task<List<(int Id, string Label)>> GetApplicationsDropdownAsync(CancellationToken ct = default)
    {
        var applications = await context.Applications
            .AsNoTracking()
            .Include(application => application.Candidate)
            .Include(application => application.Vacancy)
            .Where(application => application.Candidate != null && !application.Candidate.IsDeleted)
            .Where(application => application.Vacancy != null && !application.Vacancy.IsArchived)
            .OrderByDescending(application => application.AppliedAt)
            .ToListAsync(ct);

        return applications.Select(a => (a.Id, $"{a.Candidate?.FullName} - {a.Vacancy?.Title}")).ToList();
    }

    public async Task<List<Recruiter>> GetRecruitersDropdownAsync(CancellationToken ct = default)
    {
        return await context.Recruiters
            .AsNoTracking()
            .OrderBy(recruiter => recruiter.FullName)
            .ToListAsync(ct);
    }

    public async Task<int?> GetRecruiterIdForUserAsync(string? recruiterIdClaim, string? userIdClaim, CancellationToken ct = default)
    {
        var candidateValue = recruiterIdClaim ?? userIdClaim;
        if (!int.TryParse(candidateValue, out var id))
        {
            return null;
        }

        var exists = await context.Recruiters.AnyAsync(r => r.Id == id, ct);
        return exists ? id : null;
    }
}
