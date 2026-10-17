using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface ICandidateApplicationService
{
    Task<(bool Success, string? Error, Candidate Candidate, Application Application)> ApplyFromCareersAsync(
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
        CancellationToken ct = default);

    Task<(bool Success, string? Error)> MoveApplicationStageAsync(
        int applicationId,
        string targetStatus,
        int? targetStageId,
        CancellationToken ct = default);
    Task<(List<Application> Items, int TotalItems, int TotalPages)> GetApplicationsAsync(string? status, int page = 1, int pageSize = 25, CancellationToken ct = default);
    Task<Application?> GetApplicationDetailsAsync(int id, CancellationToken ct = default);
    Task<Application> CreateApplicationAsync(Application application, CancellationToken ct = default);
    Task<(bool Success, string? Error)> UpdateApplicationAsync(Application application, CancellationToken ct = default);
    Task<bool> DeleteApplicationAsync(int id, CancellationToken ct = default);
    Task<(List<Vacancy> Vacancies, List<PipelineStage> Stages, List<Application> Applications)> GetKanbanDataAsync(int? vacancyId, CancellationToken ct = default);
}
