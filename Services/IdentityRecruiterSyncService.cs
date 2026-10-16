using System.Security.Claims;
using HRReserveSystem.Models;
using Microsoft.AspNetCore.Identity;

namespace HRReserveSystem.Services;

public class IdentityRecruiterSyncService(
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager)
{
    public const string RecruiterIdClaim = "RecruiterId";

    public async Task SyncRecruitersAsync(IEnumerable<Recruiter> recruiters)
    {
        foreach (var recruiter in recruiters)
        {
            var errors = await SyncRecruiterAsync(recruiter);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", errors));
            }
        }
    }

    public async Task<IReadOnlyList<string>> SyncRecruiterAsync(Recruiter recruiter)
    {
        var errors = new List<string>();
        await EnsureRoleAsync(recruiter.Role, errors);

        if (string.IsNullOrWhiteSpace(recruiter.PasswordHash))
        {
            errors.Add($"Для користувача {recruiter.Login} не задано хеш пароля.");
            return errors;
        }

        var user = await userManager.FindByIdAsync(recruiter.Id.ToString());
        if (user is null)
        {
            user = new IdentityUser
            {
                Id = recruiter.Id.ToString(),
                UserName = recruiter.Login,
                Email = recruiter.Email,
                EmailConfirmed = true,
                PasswordHash = recruiter.PasswordHash
            };

            AddErrors(await userManager.CreateAsync(user), errors);
        }
        else
        {
            user.UserName = recruiter.Login;
            user.Email = recruiter.Email;
            user.EmailConfirmed = true;
            user.PasswordHash = recruiter.PasswordHash;
            user.SecurityStamp = Guid.NewGuid().ToString();
            AddErrors(await userManager.UpdateAsync(user), errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        await SyncRoleAsync(user, recruiter.Role, errors);
        await SyncClaimsAsync(user, recruiter, errors);

        return errors;
    }

    public async Task DeleteRecruiterAsync(int recruiterId)
    {
        var user = await userManager.FindByIdAsync(recruiterId.ToString());
        if (user is not null)
        {
            await userManager.DeleteAsync(user);
        }
    }

    private async Task EnsureRoleAsync(string role, List<string> errors)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            AddErrors(await roleManager.CreateAsync(new IdentityRole(role)), errors);
        }
    }

    private async Task SyncRoleAsync(IdentityUser user, string role, List<string> errors)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            AddErrors(await userManager.RemoveFromRolesAsync(user, currentRoles), errors);
        }

        AddErrors(await userManager.AddToRoleAsync(user, role), errors);
    }

    private async Task SyncClaimsAsync(IdentityUser user, Recruiter recruiter, List<string> errors)
    {
        var currentClaims = await userManager.GetClaimsAsync(user);
        var claimsToRemove = currentClaims
            .Where(claim => claim.Type is "DisplayName" or "AvatarUrl" or RecruiterIdClaim)
            .ToList();

        if (claimsToRemove.Count > 0)
        {
            AddErrors(await userManager.RemoveClaimsAsync(user, claimsToRemove), errors);
        }

        var claimsToAdd = new List<Claim>
        {
            new("DisplayName", recruiter.FullName),
            new(RecruiterIdClaim, recruiter.Id.ToString())
        };

        if (!string.IsNullOrWhiteSpace(recruiter.AvatarUrl))
        {
            claimsToAdd.Add(new("AvatarUrl", recruiter.AvatarUrl));
        }

        AddErrors(await userManager.AddClaimsAsync(user, claimsToAdd), errors);
    }

    private static void AddErrors(IdentityResult result, List<string> errors)
    {
        if (result.Succeeded)
        {
            return;
        }

        errors.AddRange(result.Errors.Select(error => error.Description));
    }
}
