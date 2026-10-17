using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface IInterviewService
{
    Task<(List<Interview> Items, int TotalItems, int TotalPages)> GetInterviewsAsync(string? interviewType, string? result, int page = 1, int pageSize = 25, CancellationToken ct = default);
    Task<List<Interview>> GetCalendarInterviewsAsync(CancellationToken ct = default);
    Task<Interview?> GetInterviewDetailsAsync(int id, CancellationToken ct = default);
    Task<Interview> ScheduleInterviewAsync(Interview interview, CancellationToken ct = default);
    Task<(bool Success, string? Error)> UpdateInterviewAsync(Interview interview, CancellationToken ct = default);
    Task<bool> DeleteInterviewAsync(int id, CancellationToken ct = default);
    string GenerateIcsContent(Interview interview);
    string GetGoogleCalendarUrl(Interview interview);
    Task<List<(int Id, string Label)>> GetApplicationsDropdownAsync(CancellationToken ct = default);
    Task<List<Recruiter>> GetRecruitersDropdownAsync(CancellationToken ct = default);
    Task<int?> GetRecruiterIdForUserAsync(string? recruiterIdClaim, string? userIdClaim, CancellationToken ct = default);
}
