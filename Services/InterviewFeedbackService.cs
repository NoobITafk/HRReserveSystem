using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class InterviewFeedbackService(
    ApplicationDbContext context,
    IAuditService auditService,
    ILogger<InterviewFeedbackService> logger) : IInterviewFeedbackService
{
    public async Task<List<InterviewFeedback>> GetFeedbacksAsync(CancellationToken ct = default)
    {
        return await context.InterviewFeedbacks
            .AsNoTracking()
            .Include(feedback => feedback.Recruiter)
            .Include(feedback => feedback.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Candidate)
            .Include(feedback => feedback.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Vacancy)
            .OrderByDescending(feedback => feedback.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<InterviewFeedback?> GetFeedbackDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.InterviewFeedbacks
            .AsNoTracking()
            .Include(item => item.Recruiter)
            .Include(item => item.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Candidate)
            .Include(item => item.Interview)
                .ThenInclude(interview => interview!.Application)
                    .ThenInclude(application => application!.Vacancy)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
    }

    public async Task<(InterviewFeedback? Feedback, bool Forbidden)> GetFeedbackForEditAsync(
        int id,
        int? currentRecruiterId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        var feedback = await context.InterviewFeedbacks
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        if (feedback is null)
        {
            return (null, false);
        }

        // IDOR Defense: only the creator or an Admin can edit
        if (!isAdmin && feedback.RecruiterId != currentRecruiterId)
        {
            logger.LogWarning("IDOR attempt blocked: Recruiter {CurrentRecruiterId} tried to access feedback {FeedbackId} created by {OwnerId}",
                currentRecruiterId, id, feedback.RecruiterId);
            return (null, true);
        }

        return (feedback, false);
    }

    public async Task<(bool Success, string? Error)> CreateFeedbackAsync(
        InterviewFeedback feedback,
        int? currentRecruiterId,
        bool isInterviewer,
        CancellationToken ct = default)
    {
        if (isInterviewer)
        {
            feedback.RecruiterId = currentRecruiterId;
        }

        feedback.CreatedAt = DateTime.UtcNow;
        context.Add(feedback);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            "Create",
            "InterviewFeedback",
            feedback.Id.ToString(),
            $"Створено оцінку інтерв'ю #{feedback.InterviewId} з балом {feedback.Score}");

        return (true, null);
    }

    public async Task<(bool Success, string? Error, bool Forbidden)> UpdateFeedbackAsync(
        InterviewFeedback feedback,
        int? currentRecruiterId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        var existing = await context.InterviewFeedbacks.FirstOrDefaultAsync(f => f.Id == feedback.Id, ct);
        if (existing is null)
        {
            return (false, "Відгук не знайдено", false);
        }

        // IDOR Defense
        if (!isAdmin && existing.RecruiterId != currentRecruiterId)
        {
            logger.LogWarning("IDOR attempt blocked on update: Recruiter {CurrentRecruiterId} tried to modify feedback {FeedbackId} owned by {OwnerId}",
                currentRecruiterId, feedback.Id, existing.RecruiterId);
            return (false, "Ви не маєте права змінювати оцінку іншого інтерв'юера", true);
        }

        existing.Comment = feedback.Comment;
        existing.Score = feedback.Score;
        existing.Recommendation = feedback.Recommendation;
        if (isAdmin && feedback.RecruiterId.HasValue)
        {
            existing.RecruiterId = feedback.RecruiterId;
        }

        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            "Update",
            "InterviewFeedback",
            feedback.Id.ToString(),
            $"Оновлено оцінку #{feedback.Id}: бал {feedback.Score}, рекомендація {feedback.Recommendation}");

        return (true, null, false);
    }

    public async Task<(bool Success, string? Error, bool Forbidden)> DeleteFeedbackAsync(
        int id,
        int? currentRecruiterId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        var feedback = await context.InterviewFeedbacks.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feedback is null)
        {
            return (false, "Відгук не знайдено", false);
        }

        // IDOR Defense
        if (!isAdmin && feedback.RecruiterId != currentRecruiterId)
        {
            logger.LogWarning("IDOR attempt blocked on delete: Recruiter {CurrentRecruiterId} tried to delete feedback {FeedbackId} owned by {OwnerId}",
                currentRecruiterId, id, feedback.RecruiterId);
            return (false, "Ви не маєте права видаляти оцінку іншого інтерв'юера", true);
        }

        context.InterviewFeedbacks.Remove(feedback);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            "Delete",
            "InterviewFeedback",
            id.ToString(),
            $"Видалено оцінку #{id}");

        return (true, null, false);
    }

    public async Task<List<(int Id, string Label)>> GetInterviewsDropdownAsync(CancellationToken ct = default)
    {
        var interviews = await context.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a!.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a!.Vacancy)
            .OrderByDescending(i => i.InterviewDate)
            .ToListAsync(ct);

        return interviews.Select(i => (i.Id, $"#{i.Id} - {i.Application?.Candidate?.FullName} ({i.Application?.Vacancy?.Title})")).ToList();
    }

    public async Task<List<Recruiter>> GetRecruitersDropdownAsync(CancellationToken ct = default)
    {
        return await context.Recruiters
            .AsNoTracking()
            .OrderBy(r => r.FullName)
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
