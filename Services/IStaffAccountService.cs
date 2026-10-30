using System.Security.Claims;
using HRReserveSystem.Models;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRReserveSystem.Services;

public interface IStaffAccountService
{
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
    Task<bool> IsLoginTakenAsync(string login, CancellationToken ct = default);
    Task<bool> IsEmailTakenAsync(string email, int? excludeRecruiterId = null, CancellationToken ct = default);
    Task<Recruiter> RegisterStaffAsync(RegisterViewModel model, CancellationToken ct = default);
    Task<Recruiter?> GetCurrentRecruiterAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<ProfileViewModel> BuildProfileViewModelAsync(Recruiter recruiter, CancellationToken ct = default);
    Task UpdateProfileAsync(Recruiter recruiter, string fullName, string email, string? newPassword, string? avatarRelativeUrl, CancellationToken ct = default);
    Task<string?> SaveStaffAvatarAsync(IFormFile avatarFile, int recruiterId, CancellationToken ct = default);
}
