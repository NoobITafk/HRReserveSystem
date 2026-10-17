using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface IInterviewService
{
    Task<List<Interview>> GetInterviewsAsync(string? interviewType, string? result, CancellationToken ct = default);
    Task<List<Interview>> GetCalendarInterviewsAsync(CancellationToken ct = default);
    Task<Interview?> GetInterviewDetailsAsync(int id, CancellationToken ct = default);
    Task<Interview> ScheduleInterviewAsync(Interview interview, CancellationToken ct = default);
    Task<(bool Success, string? Error)> UpdateInterviewAsync(Interview interview, CancellationToken ct = default);
    Task<bool> DeleteInterviewAsync(int id, CancellationToken ct = default);
    string GenerateIcsContent(Interview interview);
    string GetGoogleCalendarUrl(Interview interview);
}
