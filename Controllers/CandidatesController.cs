using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter")]
public class CandidatesController(
    ApplicationDbContext context,
    IWebHostEnvironment environment,
    ResumeIntelligenceService resumeIntelligence) : Controller
{
    private const long MaxResumeFileSize = 5 * 1024 * 1024;
    private const long MaxAvatarFileSize = 5 * 1024 * 1024;
    private static readonly string[] AllowedResumeExtensions = [".pdf", ".doc", ".docx"];
    private static readonly string[] AllowedAvatarExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<IActionResult> Index(string? search, int? minExperience, bool showHidden = false)
    {
        var candidates = ApplyFilters(context.Candidates.AsNoTracking().Where(candidate => candidate.IsDeleted == showHidden), search, minExperience);

        ViewData["CurrentFilter"] = search;
        ViewData["MinExperience"] = minExperience;
        ViewData["ShowHidden"] = showHidden;

        return View(await candidates
            .OrderBy(candidate => candidate.FullName)
            .ToListAsync());
    }

    public async Task<IActionResult> ExportCsv(string? search, int? minExperience, bool showHidden = false)
    {
        var candidates = await ApplyFilters(context.Candidates.AsNoTracking().Where(candidate => candidate.IsDeleted == showHidden), search, minExperience)
            .OrderBy(candidate => candidate.FullName)
            .ToListAsync();

        var csv = new StringBuilder();
        AppendCsvRow(csv, "Id", "FullName", "Email", "Phone", "ExperienceYears", "Skills", "ResumeFilePath", "ResumeSummary", "CreatedAt");

        foreach (var candidate in candidates)
        {
            AppendCsvRow(
                csv,
                candidate.Id,
                candidate.FullName,
                candidate.Email,
                candidate.Phone,
                candidate.ExperienceYears,
                candidate.Skills,
                candidate.ResumeFilePath,
                candidate.ResumeSummary,
                candidate.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(csv.ToString());
        var fileBytes = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, fileBytes, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, fileBytes, preamble.Length, content.Length);

        var fileName = $"candidates-{DateTime.Today:yyyy-MM-dd}.csv";
        return File(fileBytes, "text/csv; charset=utf-8", fileName);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var candidate = await context.Candidates
            .AsNoTracking()
            .Include(item => item.Applications)
                .ThenInclude(application => application.Vacancy)
            .Include(item => item.SoftSkillAssessments)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (candidate is null)
        {
            return NotFound();
        }

        ViewBag.Interviews = await context.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a!.Vacancy)
            .Include(i => i.Recruiter)
            .Include(i => i.Feedbacks)
            .Where(i => i.Application != null && i.Application.CandidateId == candidate.Id)
            .OrderByDescending(i => i.InterviewDate)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(candidate.ResumeFilePath))
        {
            var fullPath = Path.Combine(environment.WebRootPath, candidate.ResumeFilePath.TrimStart('/', '\\'));
            if (System.IO.File.Exists(fullPath))
            {
                ViewBag.ParsedResume = resumeIntelligence.ParseResumeFile(fullPath);
            }
        }

        return View(candidate);
    }

    public async Task<IActionResult> DownloadResume(int id)
    {
        var candidate = await context.Candidates
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted);

        if (candidate is null || string.IsNullOrWhiteSpace(candidate.ResumeFilePath))
        {
            return NotFound();
        }

        if (!candidate.ResumeFilePath.StartsWith("/uploads/resumes/", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var fileName = Path.GetFileName(candidate.ResumeFilePath);
        var physicalPath = Path.Combine(environment.WebRootPath, "uploads", "resumes", fileName);
        if (!System.IO.File.Exists(physicalPath))
        {
            return NotFound();
        }

        return PhysicalFile(physicalPath, GetResumeContentType(fileName), fileName);
    }

    public IActionResult Create()
    {
        return View(new Candidate());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("FullName,Email,Phone,Skills,ResumeSummary,ExperienceYears")] Candidate candidate,
        IFormFile? resumeFile,
        IFormFile? avatarFile)
    {
        ValidateResumeFile(resumeFile);
        ValidateAvatarFile(avatarFile);

        if (!ModelState.IsValid)
        {
            return View(candidate);
        }

        if (await CandidateEmailExists(candidate.Email))
        {
            ModelState.AddModelError(nameof(Candidate.Email), "Кандидат із такою електронною поштою вже існує.");
            return View(candidate);
        }

        var uploadedResumePath = await SaveResumeFileAsync(resumeFile);
        if (!string.IsNullOrWhiteSpace(uploadedResumePath))
        {
            candidate.ResumeFilePath = uploadedResumePath;
            var fullPath = Path.Combine(environment.WebRootPath, uploadedResumePath.TrimStart('/', '\\'));
            var parsed = resumeIntelligence.ParseResumeFile(fullPath);
            if (parsed.Success && string.IsNullOrWhiteSpace(candidate.ResumeSummary) && !string.IsNullOrWhiteSpace(parsed.Summary))
            {
                candidate.ResumeSummary = parsed.Summary;
            }
        }

        var uploadedAvatarPath = await SaveAvatarFileAsync(avatarFile);
        if (!string.IsNullOrWhiteSpace(uploadedAvatarPath))
        {
            candidate.AvatarUrl = uploadedAvatarPath;
        }

        candidate.CreatedAt = DateTime.UtcNow;
        context.Add(candidate);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadResume(int id, IFormFile? resumeFile)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted);
        if (candidate is null)
        {
            return NotFound();
        }

        ValidateResumeFile(resumeFile, requireFile: true);
        if (!ModelState.IsValid)
        {
            var resumeError = ModelState.TryGetValue(nameof(Candidate.ResumeFilePath), out var entry)
                ? entry.Errors.FirstOrDefault()?.ErrorMessage
                : null;
            TempData["ResumeError"] = resumeError ?? "Файл резюме не вибрано.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var oldResumePath = candidate.ResumeFilePath;
        var uploadedResumePath = await SaveResumeFileAsync(resumeFile);
        if (string.IsNullOrWhiteSpace(uploadedResumePath))
        {
            TempData["ResumeError"] = "Файл резюме не вибрано.";
            return RedirectToAction(nameof(Details), new { id });
        }

        DeleteStoredResume(oldResumePath);
        candidate.ResumeFilePath = uploadedResumePath;

        var fullPath = Path.Combine(environment.WebRootPath, uploadedResumePath.TrimStart('/', '\\'));
        var parsed = resumeIntelligence.ParseResumeFile(fullPath);
        if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
        {
            candidate.ResumeSummary = parsed.Summary;
        }

        await context.SaveChangesAsync();

        TempData["ResumeMessage"] = "Резюме кандидата оновлено та проаналізовано.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var candidate = await context.Candidates.FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted);
        return candidate is null ? NotFound() : View(candidate);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,FullName,Email,Phone,Skills,ResumeSummary,ExperienceYears")] Candidate candidate,
        IFormFile? resumeFile,
        IFormFile? avatarFile,
        bool removeResume = false,
        bool removeAvatar = false)
    {
        if (id != candidate.Id)
        {
            return NotFound();
        }

        var existingCandidate = await context.Candidates.FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted);
        if (existingCandidate is null)
        {
            return NotFound();
        }

        ValidateResumeFile(resumeFile);
        ValidateAvatarFile(avatarFile);

        if (!ModelState.IsValid)
        {
            candidate.ResumeFilePath = existingCandidate.ResumeFilePath;
            candidate.AvatarUrl = existingCandidate.AvatarUrl;
            candidate.CreatedAt = existingCandidate.CreatedAt;
            return View(candidate);
        }

        if (await CandidateEmailExists(candidate.Email, candidate.Id))
        {
            ModelState.AddModelError(nameof(Candidate.Email), "Кандидат із такою електронною поштою вже існує.");
            candidate.ResumeFilePath = existingCandidate.ResumeFilePath;
            candidate.AvatarUrl = existingCandidate.AvatarUrl;
            candidate.CreatedAt = existingCandidate.CreatedAt;
            return View(candidate);
        }

        try
        {
            var oldResumePath = existingCandidate.ResumeFilePath;
            var oldAvatarPath = existingCandidate.AvatarUrl;

            existingCandidate.FullName = candidate.FullName;
            existingCandidate.Email = candidate.Email;
            existingCandidate.Phone = candidate.Phone;
            existingCandidate.Skills = candidate.Skills;
            existingCandidate.ResumeSummary = candidate.ResumeSummary;
            existingCandidate.ExperienceYears = candidate.ExperienceYears;

            if (removeResume)
            {
                DeleteStoredResume(oldResumePath);
                existingCandidate.ResumeFilePath = null;
            }

            var uploadedResumePath = await SaveResumeFileAsync(resumeFile);
            if (!string.IsNullOrWhiteSpace(uploadedResumePath))
            {
                DeleteStoredResume(oldResumePath);
                existingCandidate.ResumeFilePath = uploadedResumePath;
                var fullPath = Path.Combine(environment.WebRootPath, uploadedResumePath.TrimStart('/', '\\'));
                var parsed = resumeIntelligence.ParseResumeFile(fullPath);
                if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
                {
                    existingCandidate.ResumeSummary = parsed.Summary;
                }
            }

            if (removeAvatar)
            {
                DeleteStoredAvatar(oldAvatarPath);
                existingCandidate.AvatarUrl = null;
            }

            var uploadedAvatarPath = await SaveAvatarFileAsync(avatarFile);
            if (!string.IsNullOrWhiteSpace(uploadedAvatarPath))
            {
                DeleteStoredAvatar(oldAvatarPath);
                existingCandidate.AvatarUrl = uploadedAvatarPath;
            }

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await CandidateExists(candidate.Id))
            {
                return NotFound();
            }

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var candidate = await context.Candidates
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted);

        return candidate is null ? NotFound() : View(candidate);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted);

        if (candidate is not null)
        {
            candidate.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(item => item.Id == id && item.IsDeleted);

        if (candidate is not null)
        {
            candidate.IsDeleted = false;
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index), new { showHidden = true });
    }

    private async Task<bool> CandidateExists(int id)
    {
        return await context.Candidates.AnyAsync(item => item.Id == id && !item.IsDeleted);
    }

    private async Task<bool> CandidateEmailExists(string email, int? excludedCandidateId = null)
    {
        var normalizedEmail = email.Trim().ToLower();
        return await context.Candidates.AnyAsync(item =>
            item.Email.ToLower() == normalizedEmail &&
            (!excludedCandidateId.HasValue || item.Id != excludedCandidateId.Value));
    }

    private static IQueryable<Candidate> ApplyFilters(IQueryable<Candidate> candidates, string? search, int? minExperience)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            candidates = candidates.Where(candidate =>
                candidate.FullName.Contains(search) ||
                candidate.Email.Contains(search) ||
                candidate.Skills.Contains(search));
        }

        if (minExperience.HasValue)
        {
            candidates = candidates.Where(candidate => candidate.ExperienceYears >= minExperience.Value);
        }

        return candidates;
    }

    private static void AppendCsvRow(StringBuilder builder, params object?[] values)
    {
        builder.AppendLine(string.Join(",", values.Select(EscapeCsv)));
    }

    private static string EscapeCsv(object? value)
    {
        var text = value?.ToString() ?? string.Empty;

        return text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private void ValidateResumeFile(IFormFile? resumeFile, bool requireFile = false)
    {
        if (resumeFile is null || resumeFile.Length == 0)
        {
            if (requireFile)
            {
                ModelState.AddModelError(nameof(Candidate.ResumeFilePath), "Файл резюме не вибрано.");
            }

            return;
        }

        var extension = Path.GetExtension(resumeFile.FileName).ToLowerInvariant();
        if (!AllowedResumeExtensions.Contains(extension))
        {
            ModelState.AddModelError(nameof(Candidate.ResumeFilePath), "Дозволені формати резюме: PDF, DOC, DOCX.");
        }

        if (resumeFile.Length > MaxResumeFileSize)
        {
            ModelState.AddModelError(nameof(Candidate.ResumeFilePath), "Файл резюме має бути не більшим за 5 MB.");
        }
    }

    private void ValidateAvatarFile(IFormFile? avatarFile)
    {
        if (avatarFile is null || avatarFile.Length == 0)
        {
            return;
        }

        var extension = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
        if (!AllowedAvatarExtensions.Contains(extension))
        {
            ModelState.AddModelError(nameof(Candidate.AvatarUrl), "Дозволені формати фото: JPG, JPEG, PNG, WEBP.");
        }

        if (avatarFile.Length > MaxAvatarFileSize)
        {
            ModelState.AddModelError(nameof(Candidate.AvatarUrl), "Файл фото має бути не більшим за 5 MB.");
        }
    }

    private async Task<string?> SaveResumeFileAsync(IFormFile? resumeFile)
    {
        if (resumeFile is null || resumeFile.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(resumeFile.FileName).ToLowerInvariant();
        var uploadsPath = Path.Combine(environment.WebRootPath, "uploads", "resumes");
        Directory.CreateDirectory(uploadsPath);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadsPath, fileName);

        await using var stream = System.IO.File.Create(physicalPath);
        await resumeFile.CopyToAsync(stream);

        return $"/uploads/resumes/{fileName}";
    }

    private async Task<string?> SaveAvatarFileAsync(IFormFile? avatarFile)
    {
        if (avatarFile is null || avatarFile.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
        var uploadsPath = Path.Combine(environment.WebRootPath, "uploads", "avatars");
        Directory.CreateDirectory(uploadsPath);

        var fileName = $"candidate_{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadsPath, fileName);

        await using var stream = System.IO.File.Create(physicalPath);
        await avatarFile.CopyToAsync(stream);

        return $"/uploads/avatars/{fileName}";
    }

    private static string GetResumeContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }

    private void DeleteStoredResume(string? resumePath)
    {
        if (string.IsNullOrWhiteSpace(resumePath) || !resumePath.StartsWith("/uploads/resumes/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(resumePath);
        var physicalPath = Path.Combine(environment.WebRootPath, "uploads", "resumes", fileName);
        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }

    private void DeleteStoredAvatar(string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath) || !avatarPath.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(avatarPath);
        var physicalPath = Path.Combine(environment.WebRootPath, "uploads", "avatars", fileName);
        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }
}
