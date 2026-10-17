using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface ISoftSkillAssessmentService
{
    Task<List<SoftSkillAssessment>> GetAssessmentsAsync(CancellationToken ct = default);
    Task<SoftSkillAssessment?> GetAssessmentDetailsAsync(int id, CancellationToken ct = default);
    Task<(SoftSkillAssessment? Assessment, bool Forbidden)> GetAssessmentForEditAsync(int id, int? currentRecruiterId, bool isAdmin, CancellationToken ct = default);
    Task<(bool Success, string? Error)> CreateAssessmentAsync(SoftSkillAssessment assessment, int? currentRecruiterId, bool isInterviewer, CancellationToken ct = default);
    Task<(bool Success, string? Error, bool Forbidden)> UpdateAssessmentAsync(SoftSkillAssessment assessment, int? currentRecruiterId, bool isAdmin, CancellationToken ct = default);
    Task<(bool Success, string? Error, bool Forbidden)> DeleteAssessmentAsync(int id, int? currentRecruiterId, bool isAdmin, CancellationToken ct = default);
    Task<List<Candidate>> GetCandidatesDropdownAsync(CancellationToken ct = default);
    Task<int?> GetRecruiterIdForUserAsync(string? recruiterIdClaim, string? userIdClaim, CancellationToken ct = default);
}
