using HRReserveSystem.Models;
using HRReserveSystem.ViewModels;

namespace HRReserveSystem.Services;

public interface IRecruiterService
{
    Task<List<Recruiter>> GetRecruitersAsync(CancellationToken ct = default);
    Task<Recruiter?> GetRecruiterDetailsAsync(int id, CancellationToken ct = default);
    Task<RecruiterFormViewModel?> GetRecruiterForEditAsync(int id, CancellationToken ct = default);
    Task<(bool Success, List<string> Errors)> CreateRecruiterAsync(RecruiterFormViewModel model, CancellationToken ct = default);
    Task<(bool Success, List<string> Errors)> UpdateRecruiterAsync(RecruiterFormViewModel model, CancellationToken ct = default);
    Task<bool> DeleteRecruiterAsync(int id, CancellationToken ct = default);
}
