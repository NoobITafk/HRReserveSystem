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
}
