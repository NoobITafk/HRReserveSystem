using System.Security.Claims;
using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class StaffAccountService(
    ApplicationDbContext context,
    IPasswordHasher<Recruiter> passwordHasher) : IStaffAccountService
{
    public async Task<bool> IsLoginTakenAsync(string login, CancellationToken ct = default)
    {
        var normalizedLogin = login.Trim().ToLower();
        return await context.Recruiters.AnyAsync(r => r.Login.ToLower() == normalizedLogin, ct);
    }

    public async Task<bool> IsEmailTakenAsync(string email, int? excludeRecruiterId = null, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLower();
        return await context.Recruiters.AnyAsync(r =>
            (!excludeRecruiterId.HasValue || r.Id != excludeRecruiterId.Value) &&
            r.Email.ToLower() == normalizedEmail, ct);
    }

    public async Task<Recruiter> RegisterStaffAsync(RegisterViewModel model, CancellationToken ct = default)
    {
        var selectedRole = RegisterViewModel.AllowedRegistrationRoles.Contains(model.Role) ? model.Role : "Recruiter";

        var recruiter = new Recruiter
        {
            FullName = model.FullName.Trim(),
            Email = model.Email.Trim(),
            Login = model.Login.Trim(),
            Role = selectedRole,
            CreatedAt = DateTime.UtcNow
        };
        recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, model.Password);

        context.Recruiters.Add(recruiter);
        await context.SaveChangesAsync(ct);
        return recruiter;
    }

    public async Task<Recruiter?> GetCurrentRecruiterAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var recruiterIdClaim = user.FindFirstValue(IdentityRecruiterSyncService.RecruiterIdClaim)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (int.TryParse(recruiterIdClaim, out var recruiterId))
        {
            var byId = await context.Recruiters.FirstOrDefaultAsync(r => r.Id == recruiterId, ct);
            if (byId is not null)
            {
                return byId;
            }
        }

        var userName = user.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            return await context.Recruiters.FirstOrDefaultAsync(r => r.Login == userName || r.Email == userName, ct);
        }

        return null;
    }

    public async Task<ProfileViewModel> BuildProfileViewModelAsync(Recruiter recruiter, CancellationToken ct = default)
    {
        var model = new ProfileViewModel
        {
            Id = recruiter.Id,
            FullName = recruiter.FullName,
            Email = recruiter.Email,
            Login = recruiter.Login,
            Role = recruiter.Role,
            AvatarUrl = recruiter.AvatarUrl,
            CreatedAt = recruiter.CreatedAt,
            InterviewsCount = await context.Interviews.CountAsync(i => i.RecruiterId == recruiter.Id, ct),
            FeedbacksCount = await context.InterviewFeedbacks.CountAsync(f => f.RecruiterId == recruiter.Id, ct),
            CandidatesCount = await context.Candidates.CountAsync(c => !c.IsDeleted, ct),
            VacanciesCount = await context.Vacancies.CountAsync(v => v.Status == "Open", ct)
        };

        var feedbacks = await context.InterviewFeedbacks
            .Include(f => f.Interview)
                .ThenInclude(i => i!.Application)
                    .ThenInclude(a => a!.Candidate)
            .Include(f => f.Interview)
                .ThenInclude(i => i!.Application)
                    .ThenInclude(a => a!.Vacancy)
            .Where(f => f.RecruiterId == recruiter.Id)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        if (feedbacks.Count > 0)
        {
            model.AverageFeedbackScore = Math.Round(feedbacks.Average(f => f.Score), 1);
            model.MyRecentFeedbacks = feedbacks.Take(10).ToList();
        }

        model.MyUpcomingInterviews = await context.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a!.Candidate)
            .Include(i => i.Application)
                .ThenInclude(a => a!.Vacancy)
            .Include(i => i.Feedbacks)
            .Where(i => i.RecruiterId == recruiter.Id && i.InterviewDate >= DateTime.UtcNow.Date)
            .OrderBy(i => i.InterviewDate)
            .Take(10)
            .ToListAsync(ct);

        return model;
    }

    public async Task UpdateProfileAsync(
        Recruiter recruiter,
        string fullName,
        string email,
        string? newPassword,
        string? avatarRelativeUrl,
        CancellationToken ct = default)
    {
        recruiter.FullName = fullName.Trim();
        recruiter.Email = email.Trim();

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, newPassword);
        }

        if (!string.IsNullOrWhiteSpace(avatarRelativeUrl))
        {
            recruiter.AvatarUrl = avatarRelativeUrl;
        }

        context.Recruiters.Update(recruiter);
        await context.SaveChangesAsync(ct);
    }
}
