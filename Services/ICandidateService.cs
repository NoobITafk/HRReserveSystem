using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface ICandidateService
{
    Task<(List<Candidate> Items, int TotalItems, int TotalPages)> GetCandidatesAsync(string? search, int? minExperience, bool showHidden = false, int page = 1, int pageSize = 25, CancellationToken ct = default);
    Task<List<Candidate>> GetCandidatesForExportAsync(string? search, int? minExperience, bool showHidden = false, CancellationToken ct = default);
    Task<Candidate?> GetCandidateDetailsAsync(int id, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, int? excludeId = null, CancellationToken ct = default);
    Task<Candidate> CreateCandidateAsync(Candidate candidate, IFormFile? resumeFile, IFormFile? avatarFile, CancellationToken ct = default);
    Task<(bool Success, string? Error)> UpdateCandidateAsync(Candidate candidate, IFormFile? resumeFile, IFormFile? avatarFile, CancellationToken ct = default);
    Task<bool> SoftDeleteCandidateAsync(int id, CancellationToken ct = default);
    Task<bool> RestoreCandidateAsync(int id, CancellationToken ct = default);
    Task<(bool Success, string? Error, string? NewResumePath)> UploadResumeAsync(int id, IFormFile resumeFile, CancellationToken ct = default);
    Task<(bool Success, string? Error, string Note, string Timestamp, string Author)> AddNoteAsync(int candidateId, int? applicationId, string note, string author, CancellationToken ct = default);
    Task<(bool Success, string? Error, CandidateCommunication? Communication)> SendMessageAsync(int candidateId, string subject, string body, string channel, string? templateType, string senderName, CancellationToken ct = default);
}
