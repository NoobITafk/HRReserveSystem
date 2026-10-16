using System.Security.Claims;
using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

public class AccountController(
    ApplicationDbContext context,
    DemoUserService demoUserService,
    IdentityRecruiterSyncService identitySync,
    IPasswordHasher<Recruiter> passwordHasher,
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager) : Controller
{
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ViewData["DemoUsers"] = await demoUserService.GetDemoUsersAsync();
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["DemoUsers"] = await demoUserService.GetDemoUsersAsync();

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var recruiter = await demoUserService.ValidateUserAsync(model.Login, model.Password);
        if (recruiter is null)
        {
            ModelState.AddModelError(string.Empty, "Невірний логін або пароль. Перевірте введені дані або скористайтесь демо-доступом.");
            return View(model);
        }

        var user = await userManager.FindByIdAsync(recruiter.Id.ToString());
        if (user is null)
        {
            var identityErrors = await identitySync.SyncRecruiterAsync(recruiter);
            if (identityErrors.Count > 0)
            {
                ModelState.AddModelError(string.Empty, "Користувача не вдалося синхронізувати з Identity.");
                return View(model);
            }

            user = await userManager.FindByIdAsync(recruiter.Id.ToString());
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "Користувача не знайдено в Identity.");
                return View(model);
            }
        }

        await signInManager.SignInAsync(user, model.RememberMe);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new RegisterViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var normalizedLogin = model.Login.Trim().ToLower();
        var normalizedEmail = model.Email.Trim().ToLower();

        if (await context.Recruiters.AnyAsync(r => r.Login.ToLower() == normalizedLogin))
        {
            ModelState.AddModelError(nameof(RegisterViewModel.Login), "Користувач із таким логіном вже зареєстрований.");
        }

        if (await context.Recruiters.AnyAsync(r => r.Email.ToLower() == normalizedEmail))
        {
            ModelState.AddModelError(nameof(RegisterViewModel.Email), "Користувач із такою електронною поштою вже існує.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var recruiter = new Recruiter
        {
            FullName = model.FullName.Trim(),
            Email = model.Email.Trim(),
            Login = model.Login.Trim(),
            Role = RegisterViewModel.AllowedRegistrationRoles.Contains(model.Role) ? model.Role : "Recruiter",
            CreatedAt = DateTime.UtcNow
        };
        recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, model.Password);

        context.Recruiters.Add(recruiter);
        await context.SaveChangesAsync();

        var syncErrors = await identitySync.SyncRecruiterAsync(recruiter);
        if (syncErrors.Count > 0)
        {
            foreach (var error in syncErrors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(model);
        }

        var identityUser = await userManager.FindByIdAsync(recruiter.Id.ToString());
        if (identityUser is not null)
        {
            await signInManager.SignInAsync(identityUser, isPersistent: false);
        }

        TempData["SuccessMessage"] = $"Вітаємо у системі, {recruiter.FullName}! Ваш кабінет готовий до роботи.";

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var recruiter = await GetCurrentRecruiterAsync();
        if (recruiter is null)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(await BuildProfileViewModelAsync(recruiter));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var recruiter = await GetCurrentRecruiterAsync();
        if (recruiter is null)
        {
            return RedirectToAction("Index", "Home");
        }

        model.Login = recruiter.Login;
        model.Role = recruiter.Role;
        model.CreatedAt = recruiter.CreatedAt;
        model.InterviewsCount = await context.Interviews.CountAsync(i => i.RecruiterId == recruiter.Id);
        model.FeedbacksCount = await context.InterviewFeedbacks.CountAsync(f => f.RecruiterId == recruiter.Id);

        var normalizedEmail = (model.Email ?? string.Empty).Trim().ToLower();
        if (!string.IsNullOrWhiteSpace(normalizedEmail) &&
            await context.Recruiters.AnyAsync(r => r.Id != recruiter.Id && r.Email.ToLower() == normalizedEmail))
        {
            ModelState.AddModelError(nameof(ProfileViewModel.Email), "Ця електронна пошта вже використовується іншим користувачем.");
        }

        var wantsPasswordChange = !string.IsNullOrWhiteSpace(model.NewPassword) || !string.IsNullOrWhiteSpace(model.CurrentPassword);
        if (wantsPasswordChange)
        {
            if (string.IsNullOrWhiteSpace(model.CurrentPassword))
            {
                ModelState.AddModelError(nameof(ProfileViewModel.CurrentPassword), "Введіть поточний пароль для підтвердження зміни.");
            }
            else
            {
                var verifyResult = passwordHasher.VerifyHashedPassword(recruiter, recruiter.PasswordHash, model.CurrentPassword);
                if (verifyResult == PasswordVerificationResult.Failed)
                {
                    ModelState.AddModelError(nameof(ProfileViewModel.CurrentPassword), "Поточний пароль вказано невірно.");
                }
            }

            if (string.IsNullOrWhiteSpace(model.NewPassword))
            {
                ModelState.AddModelError(nameof(ProfileViewModel.NewPassword), "Введіть новий пароль.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        recruiter.FullName = (model.FullName ?? string.Empty).Trim();
        recruiter.Email = (model.Email ?? string.Empty).Trim();

        if (wantsPasswordChange && !string.IsNullOrWhiteSpace(model.NewPassword))
        {
            recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, model.NewPassword);
        }

        await context.SaveChangesAsync();
        await identitySync.SyncRecruiterAsync(recruiter);

        var identityUser = await userManager.FindByIdAsync(recruiter.Id.ToString());
        if (identityUser is not null)
        {
            await signInManager.RefreshSignInAsync(identityUser);
        }

        TempData["SuccessMessage"] = wantsPasswordChange
            ? "Профіль та пароль успішно оновлено."
            : "Дані профілю успішно збережено.";

        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private async Task<Recruiter?> GetCurrentRecruiterAsync()
    {
        var recruiterIdClaim = User.FindFirstValue(IdentityRecruiterSyncService.RecruiterIdClaim)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (int.TryParse(recruiterIdClaim, out var recruiterId))
        {
            var byId = await context.Recruiters.FirstOrDefaultAsync(r => r.Id == recruiterId);
            if (byId is not null)
            {
                return byId;
            }
        }

        var userName = User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            return await context.Recruiters.FirstOrDefaultAsync(r => r.Login == userName);
        }

        return null;
    }

    private async Task<ProfileViewModel> BuildProfileViewModelAsync(Recruiter recruiter)
    {
        return new ProfileViewModel
        {
            Id = recruiter.Id,
            FullName = recruiter.FullName,
            Email = recruiter.Email,
            Login = recruiter.Login,
            Role = recruiter.Role,
            CreatedAt = recruiter.CreatedAt,
            InterviewsCount = await context.Interviews.CountAsync(i => i.RecruiterId == recruiter.Id),
            FeedbacksCount = await context.InterviewFeedbacks.CountAsync(f => f.RecruiterId == recruiter.Id)
        };
    }
}
