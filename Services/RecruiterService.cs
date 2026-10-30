using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class RecruiterService(
    ApplicationDbContext context,
    IdentityRecruiterSyncService identitySync,
    IPasswordHasher<Recruiter> passwordHasher,
    IAuditService auditService) : IRecruiterService
{
    public async Task<List<Recruiter>> GetRecruitersAsync(CancellationToken ct = default)
    {
        return await context.Recruiters
            .AsNoTracking()
            .OrderBy(r => r.FullName)
            .ToListAsync(ct);
    }

    public async Task<Recruiter?> GetRecruiterDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Recruiters
            .AsNoTracking()
            .Include(r => r.Interviews)
            .Include(r => r.Feedbacks)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<RecruiterFormViewModel?> GetRecruiterForEditAsync(int id, CancellationToken ct = default)
    {
        var recruiter = await context.Recruiters.FindAsync([id], ct);
        if (recruiter is null) return null;

        return new RecruiterFormViewModel
        {
            Id = recruiter.Id,
            FullName = recruiter.FullName,
            Email = recruiter.Email,
            Login = recruiter.Login,
            Role = recruiter.Role,
            CreatedAt = recruiter.CreatedAt
        };
    }

    public async Task<(bool Success, List<string> Errors)> CreateRecruiterAsync(RecruiterFormViewModel model, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var normalizedEmail = model.Email.Trim().ToLower();
        var normalizedLogin = model.Login.Trim().ToLower();

        if (await context.Recruiters.AnyAsync(r => r.Email.ToLower() == normalizedEmail, ct))
        {
            errors.Add("Рекрутер із такою електронною поштою вже існує.");
        }

        if (await context.Recruiters.AnyAsync(r => r.Login.ToLower() == normalizedLogin, ct))
        {
            errors.Add("Рекрутер із таким логіном уже існує.");
        }

        if (errors.Count > 0)
        {
            return (false, errors);
        }

        var recruiter = new Recruiter
        {
            FullName = model.FullName,
            Email = model.Email,
            Login = model.Login,
            Role = model.Role,
            CreatedAt = DateTime.UtcNow
        };
        recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, model.Password ?? string.Empty);

        context.Recruiters.Add(recruiter);
        await context.SaveChangesAsync(ct);

        var identityErrors = await identitySync.SyncRecruiterAsync(recruiter);
        if (identityErrors.Count > 0)
        {
            return (false, identityErrors.ToList());
        }

        await auditService.LogAsync(
            "Create",
            "Recruiter",
            recruiter.Id.ToString(),
            $"Створено співробітника {recruiter.FullName} ({recruiter.Role})");

        return (true, errors);
    }

    public async Task<(bool Success, List<string> Errors)> UpdateRecruiterAsync(RecruiterFormViewModel model, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var normalizedEmail = model.Email.Trim().ToLower();
        var normalizedLogin = model.Login.Trim().ToLower();

        if (await context.Recruiters.AnyAsync(r => r.Email.ToLower() == normalizedEmail && r.Id != model.Id, ct))
        {
            errors.Add("Рекрутер із такою електронною поштою вже існує.");
        }

        if (await context.Recruiters.AnyAsync(r => r.Login.ToLower() == normalizedLogin && r.Id != model.Id, ct))
        {
            errors.Add("Рекрутер із таким логіном уже існує.");
        }

        if (errors.Count > 0)
        {
            return (false, errors);
        }

        var recruiter = await context.Recruiters.FindAsync([model.Id], ct);
        if (recruiter is null)
        {
            return (false, ["Співробітника не знайдено."]);
        }

        recruiter.FullName = model.FullName;
        recruiter.Email = model.Email;
        recruiter.Login = model.Login;
        recruiter.Role = model.Role;

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, model.Password);
        }

        await context.SaveChangesAsync(ct);

        var identityErrors = await identitySync.SyncRecruiterAsync(recruiter);
        if (identityErrors.Count > 0)
        {
            return (false, identityErrors.ToList());
        }

        await auditService.LogAsync(
            "Update",
            "Recruiter",
            recruiter.Id.ToString(),
            $"Оновлено дані співробітника {recruiter.FullName} ({recruiter.Role})");

        return (true, errors);
    }

    public async Task<bool> DeleteRecruiterAsync(int id, CancellationToken ct = default)
    {
        var recruiter = await context.Recruiters.FindAsync([id], ct);
        if (recruiter is null) return false;

        context.Recruiters.Remove(recruiter);
        await context.SaveChangesAsync(ct);
        await identitySync.DeleteRecruiterAsync(id);

        await auditService.LogAsync(
            "Delete",
            "Recruiter",
            id.ToString(),
            $"Видалено співробітника #{id}");

        return true;
    }
}
