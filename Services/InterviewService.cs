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
    public async Task<List<Interview>> GetInterviewsAsync(string? interviewType, string? result, CancellationToken ct = default)
    {
        IQueryable<Interview> query = context.Interviews
            .AsNoTracking()
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Candidate)
            .Include(interview => interview.Application)
                .ThenInclude(application => application!.Vacancy)
            .Include(interview => interview.Recruiter);

        if (!string.IsNullOrWhiteSpace(interviewType))
        {
            query = query.Where(interview => interview.InterviewType == interviewType);
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            query = query.Where(interview => interview.Result == result);
        }

        return await query
            .OrderByDescending(interview => interview.InterviewDate)
            .ToListAsync(ct);
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
}
