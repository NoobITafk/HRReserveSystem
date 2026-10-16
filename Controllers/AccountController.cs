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
    ResumeIntelligenceService resumeIntelligence,
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

        var selectedRole = RegisterViewModel.AllowedRegistrationRoles.Contains(model.Role) ? model.Role : "Candidate";

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

        // If registering as a Candidate, also create or link Candidate profile
        if (selectedRole == "Candidate")
        {
            var existingCandidate = await context.Candidates.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            if (existingCandidate is null)
            {
                var candidate = new Candidate
                {
                    FullName = recruiter.FullName,
                    Email = recruiter.Email,
                    Skills = "Спеціаліст (профіль створюється)",
                    CreatedAt = DateTime.UtcNow
                };
                context.Candidates.Add(candidate);
            }
        }

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

        TempData["SuccessMessage"] = selectedRole == "Candidate"
            ? $"Вітаємо, {recruiter.FullName}! Ваш особистий кабінет кандидата готовий. Завантажте резюме та заповніть портфоліо."
            : $"Вітаємо у системі, {recruiter.FullName}! Ваш кабінет готовий до роботи.";

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction(selectedRole == "Candidate" ? "Profile" : "Index", selectedRole == "Candidate" ? "Account" : "Home");
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
            // Re-populate candidate collections for view on validation error
            var fullModel = await BuildProfileViewModelAsync(recruiter);
            fullModel.FullName = model.FullName ?? string.Empty;
            fullModel.Email = model.Email ?? string.Empty;
            fullModel.Phone = model.Phone;
            fullModel.Skills = model.Skills;
            fullModel.ExperienceYears = model.ExperienceYears;
            fullModel.ResumeSummary = model.ResumeSummary;
            return View(fullModel);
        }

        recruiter.FullName = (model.FullName ?? string.Empty).Trim();
        recruiter.Email = (model.Email ?? string.Empty).Trim();

        if (wantsPasswordChange && !string.IsNullOrWhiteSpace(model.NewPassword))
        {
            recruiter.PasswordHash = passwordHasher.HashPassword(recruiter, model.NewPassword);
        }

        // Link with Candidate
        var candidate = await context.Candidates
            .FirstOrDefaultAsync(c => c.Email.ToLower() == recruiter.Email.ToLower() && !c.IsDeleted);

        if (candidate is null && recruiter.Role == "Candidate")
        {
            candidate = new Candidate
            {
                FullName = recruiter.FullName,
                Email = recruiter.Email,
                Skills = model.Skills ?? "Спеціаліст",
                CreatedAt = DateTime.UtcNow
            };
            context.Candidates.Add(candidate);
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

                if (candidate is not null)
                {
                    candidate.AvatarUrl = relativeUrl;
                }
            }
        }

        // Handle Resume File Upload
        if (model.ResumeFile is not null && model.ResumeFile.Length > 0 && candidate is not null)
        {
            var allowedExts = new[] { ".pdf", ".doc", ".docx" };
            var ext = Path.GetExtension(model.ResumeFile.FileName).ToLowerInvariant();
            if (allowedExts.Contains(ext) && model.ResumeFile.Length <= 10 * 1024 * 1024)
            {
                var resumesDir = Path.Combine(environment.WebRootPath, "uploads", "resumes");
                Directory.CreateDirectory(resumesDir);
                var fileName = $"resume_{candidate.Id}_{Guid.NewGuid():N}{ext}";
                var savePath = Path.Combine(resumesDir, fileName);
                using (var stream = new FileStream(savePath, FileMode.Create))
                {
                    await model.ResumeFile.CopyToAsync(stream);
                }
                var relativePath = $"/uploads/resumes/{fileName}";
                candidate.ResumeFilePath = relativePath;

                // Run resume intelligence extraction
                var parsed = resumeIntelligence.ParseResumeFile(savePath);
                if (parsed.Success)
                {
                    if (!string.IsNullOrWhiteSpace(parsed.Summary))
                    {
                        candidate.ResumeSummary = parsed.Summary;
                    }
                    if (parsed.DetectedSkills.Count > 0 && (string.IsNullOrWhiteSpace(candidate.Skills) || candidate.Skills.Contains("профіль створюється")))
                    {
                        candidate.Skills = string.Join(", ", parsed.DetectedSkills);
                    }
                    if (parsed.DetectedExperienceYears.HasValue && candidate.ExperienceYears == 0)
                    {
                        candidate.ExperienceYears = parsed.DetectedExperienceYears.Value;
                    }
                    if (!string.IsNullOrWhiteSpace(parsed.DetectedPhone) && string.IsNullOrWhiteSpace(candidate.Phone))
                    {
                        candidate.Phone = parsed.DetectedPhone;
                    }
                }
            }
        }

        // Update Candidate editable profile fields
        if (candidate is not null)
        {
            if (!string.IsNullOrWhiteSpace(model.Phone)) candidate.Phone = model.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(model.Skills)) candidate.Skills = model.Skills.Trim();
            if (model.ExperienceYears >= 0) candidate.ExperienceYears = model.ExperienceYears;
            if (!string.IsNullOrWhiteSpace(model.ResumeSummary)) candidate.ResumeSummary = model.ResumeSummary.Trim();
            candidate.FullName = recruiter.FullName;
            candidate.Email = recruiter.Email;
        }

        await context.SaveChangesAsync();
        await identitySync.SyncRecruiterAsync(recruiter);

        var identityUser = await userManager.FindByIdAsync(recruiter.Id.ToString());
        if (identityUser is not null)
        {
            await signInManager.RefreshSignInAsync(identityUser);
        }

        TempData["SuccessMessage"] = wantsPasswordChange
            ? "Профіль, портфоліо та новий пароль успішно збережено."
            : "Дані профілю та портфоліо успішно оновлено.";

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
            FeedbacksCount = await context.InterviewFeedbacks.CountAsync(f => f.RecruiterId == recruiter.Id)
        };

        // Link with Candidate
        var candidate = await context.Candidates
            .Include(c => c.SoftSkillAssessments)
            .Include(c => c.Applications)
                .ThenInclude(a => a.Vacancy)
            .FirstOrDefaultAsync(c => c.Email.ToLower() == recruiter.Email.ToLower() && !c.IsDeleted);

        // If user is Candidate and no Candidate record exists yet, create one
        if (candidate is null && recruiter.Role == "Candidate")
        {
            candidate = new Candidate
            {
                FullName = recruiter.FullName,
                Email = recruiter.Email,
                Skills = "C#, .NET, SQL, Git",
                CreatedAt = DateTime.UtcNow,
                AvatarUrl = recruiter.AvatarUrl
            };
            context.Candidates.Add(candidate);
            await context.SaveChangesAsync();
        }

        if (candidate is not null)
        {
            model.IsCandidate = true;
            model.CandidateId = candidate.Id;
            model.Phone = candidate.Phone;
            model.Skills = candidate.Skills;
            model.ExperienceYears = candidate.ExperienceYears;
            model.ResumeFilePath = candidate.ResumeFilePath;
            model.ResumeSummary = candidate.ResumeSummary;
            model.AvatarUrl ??= candidate.AvatarUrl;

            // Load candidate soft skill assessments (most recent)
            model.CandidateSoftSkills = candidate.SoftSkillAssessments
                .OrderByDescending(s => s.Id)
                .FirstOrDefault();

            // Load applications
            model.CandidateApplications = candidate.Applications
                .OrderByDescending(a => a.AppliedAt)
                .ToList();

            // Load candidate interviews with feedback
            model.CandidateInterviews = await context.Interviews
                .Include(i => i.Application)
                    .ThenInclude(a => a!.Vacancy)
                .Include(i => i.Recruiter)
                .Include(i => i.Feedbacks)
                .Where(i => i.Application != null && i.Application.CandidateId == candidate.Id)
                .OrderByDescending(i => i.InterviewDate)
                .ToListAsync();

            // If resume file exists on disk, parse it for intelligence insights
            if (!string.IsNullOrWhiteSpace(candidate.ResumeFilePath))
            {
                var fullPath = Path.Combine(environment.WebRootPath, candidate.ResumeFilePath.TrimStart('/', '\\'));
                if (System.IO.File.Exists(fullPath))
                {
                    model.ParsedResume = resumeIntelligence.ParseResumeFile(fullPath);
                }
            }
        }

        return model;
    }
}
