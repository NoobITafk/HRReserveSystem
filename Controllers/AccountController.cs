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
    UserManager<IdentityUser> userManager,
    IAuditService auditService,
    IWebHostEnvironment environment) : Controller
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
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("LoginPolicy")]
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
            await auditService.LogAsync("LoginFailed", "Account", null, $"Невдала спроба входу для логіна: {model.Login}");
            ModelState.AddModelError(string.Empty, "Невірний логін або пароль. Перевірте введені дані.");
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
        await auditService.LogAsync("LoginSuccess", "Account", recruiter.Id.ToString(), $"Успішний вхід користувача {recruiter.FullName} ({recruiter.Role})");

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

        await auditService.LogAsync("StaffRegistered", "Recruiter", recruiter.Id.ToString(), $"Зареєстровано нового співробітника: {recruiter.FullName} ({recruiter.Role})");

        TempData["SuccessMessage"] = $"Вітаємо у системі, {recruiter.FullName}! Ваш обліковий запис успішно створено.";

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
            var fullModel = await BuildProfileViewModelAsync(recruiter);
            fullModel.FullName = model.FullName ?? string.Empty;
            fullModel.Email = model.Email ?? string.Empty;
            return View(fullModel);
        }

        recruiter.FullName = (model.FullName ?? string.Empty).Trim();
        recruiter.Email = (model.Email ?? string.Empty).Trim();

        if (wantsPasswordChange && !string.IsNullOrWhiteSpace(model.NewPassword))
        {
            recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, model.NewPassword);
        }

        // Handle Avatar File Upload
        if (model.AvatarFile is not null && model.AvatarFile.Length > 0)
        {
            var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(model.AvatarFile.FileName).ToLowerInvariant();
            if (allowedExts.Contains(ext) && model.AvatarFile.Length <= 5 * 1024 * 1024)
            {
                var avatarsDir = Path.Combine(environment.WebRootPath, "uploads", "avatars");
                Directory.CreateDirectory(avatarsDir);
                var fileName = $"avatar_{recruiter.Id}_{Guid.NewGuid():N}{ext}";
                var savePath = Path.Combine(avatarsDir, fileName);
                using (var stream = new FileStream(savePath, FileMode.Create))
                {
                    await model.AvatarFile.CopyToAsync(stream);
                }
                var relativeUrl = $"/uploads/avatars/{fileName}";
                recruiter.AvatarUrl = relativeUrl;
            }
        }

        await context.SaveChangesAsync();
        await identitySync.SyncRecruiterAsync(recruiter);

        var identityUser = await userManager.FindByIdAsync(recruiter.Id.ToString());
        if (identityUser is not null)
        {
            await signInManager.RefreshSignInAsync(identityUser);
        }

        await auditService.LogAsync(
            wantsPasswordChange ? "ChangePassword" : "UpdateProfile",
            "Account",
            recruiter.Id.ToString(),
            wantsPasswordChange ? $"Користувач {recruiter.FullName} змінив пароль" : $"Користувач {recruiter.FullName} оновив дані профілю");

        TempData["SuccessMessage"] = wantsPasswordChange
            ? "Профіль та новий пароль успішно збережено."
            : "Дані вашого профілю успішно оновлено.";

        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userName = User.Identity?.Name ?? "Користувач";
        await auditService.LogAsync("Logout", "Account", null, $"Користувач {userName} вийшов із системи");
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
        var model = new ProfileViewModel
        {
            Id = recruiter.Id,
            FullName = recruiter.FullName,
            Email = recruiter.Email,
            Login = recruiter.Login,
            Role = recruiter.Role,
            AvatarUrl = recruiter.AvatarUrl,
            CreatedAt = recruiter.CreatedAt,
            InterviewsCount = await context.Interviews.CountAsync(i => i.RecruiterId == recruiter.Id),
            FeedbacksCount = await context.InterviewFeedbacks.CountAsync(f => f.RecruiterId == recruiter.Id),
            CandidatesCount = await context.Candidates.CountAsync(c => !c.IsDeleted),
            VacanciesCount = await context.Vacancies.CountAsync(v => v.Status == "Open")
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
            .ToListAsync();

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
            .ToListAsync();

        return model;
    }
}
