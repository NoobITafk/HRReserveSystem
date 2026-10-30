using HRReserveSystem.Models;
using HRReserveSystem.Services;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers;

public class AccountController(
    IStaffAccountService staffAccountService,
    DemoUserService demoUserService,
    IdentityRecruiterSyncService identitySync,
    IPasswordHasher<Recruiter> passwordHasher,
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager,
    IAuditService auditService) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
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

        if (await staffAccountService.IsLoginTakenAsync(model.Login))
        {
            ModelState.AddModelError(nameof(RegisterViewModel.Login), "Користувач із таким логіном вже зареєстрований.");
        }

        if (await staffAccountService.IsEmailTakenAsync(model.Email))
        {
            ModelState.AddModelError(nameof(RegisterViewModel.Email), "Користувач із такою електронною поштою вже існує.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await using var transaction = await staffAccountService.BeginTransactionAsync();
        var recruiter = await staffAccountService.RegisterStaffAsync(model);

        var syncErrors = await identitySync.SyncRecruiterAsync(recruiter);
        if (syncErrors.Count > 0)
        {
            await transaction.RollbackAsync();
            foreach (var error in syncErrors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(model);
        }

        await transaction.CommitAsync();

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
        var recruiter = await staffAccountService.GetCurrentRecruiterAsync(User);
        if (recruiter is null)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(await staffAccountService.BuildProfileViewModelAsync(recruiter));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var recruiter = await staffAccountService.GetCurrentRecruiterAsync(User);
        if (recruiter is null)
        {
            return RedirectToAction("Index", "Home");
        }

        var normalizedEmail = (model.Email ?? string.Empty).Trim().ToLower();
        if (!string.IsNullOrWhiteSpace(normalizedEmail) &&
            await staffAccountService.IsEmailTakenAsync(normalizedEmail, recruiter.Id))
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
            var fullModel = await staffAccountService.BuildProfileViewModelAsync(recruiter);
            fullModel.FullName = model.FullName ?? string.Empty;
            fullModel.Email = model.Email ?? string.Empty;
            return View(fullModel);
        }

        string? avatarUrl = null;
        if (model.AvatarFile is not null && model.AvatarFile.Length > 0)
        {
            avatarUrl = await staffAccountService.SaveStaffAvatarAsync(model.AvatarFile, recruiter.Id);
        }

        await staffAccountService.UpdateProfileAsync(
            recruiter,
            model.FullName ?? string.Empty,
            model.Email ?? string.Empty,
            wantsPasswordChange ? model.NewPassword : null,
            avatarUrl);

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
}
