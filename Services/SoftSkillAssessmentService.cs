using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class SoftSkillAssessmentService(
    ApplicationDbContext context,
    IAuditService auditService,
    ILogger<SoftSkillAssessmentService> logger) : ISoftSkillAssessmentService
{
    public async Task<List<SoftSkillAssessment>> GetAssessmentsAsync(CancellationToken ct = default)
    {
        return await context.SoftSkillAssessments
            .AsNoTracking()
            .Include(assessment => assessment.Candidate)
            .Include(assessment => assessment.Recruiter)
            .OrderBy(assessment => assessment.Candidate != null ? assessment.Candidate.FullName : string.Empty)
            .ToListAsync(ct);
    }

    public async Task<SoftSkillAssessment?> GetAssessmentDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.SoftSkillAssessments
            .AsNoTracking()
            .Include(item => item.Candidate)
            .Include(item => item.Recruiter)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
    }

    public async Task<(SoftSkillAssessment? Assessment, bool Forbidden)> GetAssessmentForEditAsync(
        int id,
        int? currentRecruiterId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        var assessment = await context.SoftSkillAssessments
            .AsNoTracking()
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (assessment is null)
        {
            return (null, false);
        }

        // IDOR Defense: If an assessment has an assigned creator and user is not admin and not the creator
        if (!isAdmin && assessment.RecruiterId.HasValue && assessment.RecruiterId != currentRecruiterId)
        {
            logger.LogWarning("IDOR blocked on SoftSkills: User {CurrentRecruiterId} attempted to access assessment {Id} owned by {OwnerId}",
                currentRecruiterId, id, assessment.RecruiterId);
            return (null, true);
        }

        return (assessment, false);
    }

    public async Task<(bool Success, string? Error)> CreateAssessmentAsync(
        SoftSkillAssessment assessment,
        int? currentRecruiterId,
        bool isInterviewer,
        CancellationToken ct = default)
    {
        if (isInterviewer || assessment.RecruiterId == null)
        {
            assessment.RecruiterId = currentRecruiterId;
        }

        assessment.CreatedAt = DateTime.UtcNow;
        context.Add(assessment);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            "Create",
            "SoftSkillAssessment",
            assessment.Id.ToString(),
            $"Створено оцінку софт-скілів для кандидата #{assessment.CandidateId} із середнім балом {assessment.AverageScore}");

        return (true, null);
    }

    public async Task<(bool Success, string? Error, bool Forbidden)> UpdateAssessmentAsync(
        SoftSkillAssessment assessment,
        int? currentRecruiterId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        var existing = await context.SoftSkillAssessments.FirstOrDefaultAsync(a => a.Id == assessment.Id, ct);
        if (existing is null)
        {
            return (false, "Оцінку не знайдено", false);
        }

        // IDOR Defense
        if (!isAdmin && existing.RecruiterId.HasValue && existing.RecruiterId != currentRecruiterId)
        {
            logger.LogWarning("IDOR blocked on SoftSkills update: User {CurrentRecruiterId} tried to modify assessment {Id} owned by {OwnerId}",
                currentRecruiterId, assessment.Id, existing.RecruiterId);
            return (false, "Ви не маєте права змінювати оцінку іншого інтерв'юера", true);
        }

        existing.Communication = assessment.Communication;
        existing.Teamwork = assessment.Teamwork;
        existing.Responsibility = assessment.Responsibility;
        existing.StressResistance = assessment.StressResistance;
        existing.Leadership = assessment.Leadership;
        existing.OverallComment = assessment.OverallComment;
        if (isAdmin && assessment.RecruiterId.HasValue)
        {
            existing.RecruiterId = assessment.RecruiterId;
        }

        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            "Update",
            "SoftSkillAssessment",
            assessment.Id.ToString(),
            $"Оновлено оцінку софт-скілів #{assessment.Id}: середній бал {existing.AverageScore}");

        return (true, null, false);
    }

    public async Task<(bool Success, string? Error, bool Forbidden)> DeleteAssessmentAsync(
        int id,
        int? currentRecruiterId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        var assessment = await context.SoftSkillAssessments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assessment is null)
        {
            return (false, "Оцінку не знайдено", false);
        }

        // IDOR Defense
        if (!isAdmin && assessment.RecruiterId.HasValue && assessment.RecruiterId != currentRecruiterId)
        {
            logger.LogWarning("IDOR blocked on SoftSkills delete: User {CurrentRecruiterId} tried to delete assessment {Id} owned by {OwnerId}",
                currentRecruiterId, id, assessment.RecruiterId);
            return (false, "Ви не маєте права видаляти оцінку іншого інтерв'юера", true);
        }

        context.SoftSkillAssessments.Remove(assessment);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            "Delete",
            "SoftSkillAssessment",
            id.ToString(),
            $"Видалено оцінку софт-скілів #{id}");

        return (true, null, false);
    }

    public async Task<List<Candidate>> GetCandidatesDropdownAsync(CancellationToken ct = default)
    {
        return await context.Candidates
            .AsNoTracking()
            .Where(candidate => !candidate.IsDeleted)
            .OrderBy(candidate => candidate.FullName)
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
