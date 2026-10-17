using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface IInterviewFeedbackService
{
    Task<List<InterviewFeedback>> GetFeedbacksAsync(CancellationToken ct = default);
    Task<InterviewFeedback?> GetFeedbackDetailsAsync(int id, CancellationToken ct = default);
    Task<(InterviewFeedback? Feedback, bool Forbidden)> GetFeedbackForEditAsync(int id, int? currentRecruiterId, bool isAdmin, CancellationToken ct = default);
    Task<(bool Success, string? Error)> CreateFeedbackAsync(InterviewFeedback feedback, int? currentRecruiterId, bool isInterviewer, CancellationToken ct = default);
    Task<(bool Success, string? Error, bool Forbidden)> UpdateFeedbackAsync(InterviewFeedback feedback, int? currentRecruiterId, bool isAdmin, CancellationToken ct = default);
    Task<(bool Success, string? Error, bool Forbidden)> DeleteFeedbackAsync(int id, int? currentRecruiterId, bool isAdmin, CancellationToken ct = default);
    Task<List<(int Id, string Label)>> GetInterviewsDropdownAsync(CancellationToken ct = default);
    Task<List<Recruiter>> GetRecruitersDropdownAsync(CancellationToken ct = default);
    Task<int?> GetRecruiterIdForUserAsync(string? recruiterIdClaim, string? userIdClaim, CancellationToken ct = default);
}
