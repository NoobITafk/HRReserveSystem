using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter")]
public class CandidatesController(
    ICandidateService candidateService,
    ICandidateExportService exportService,
    IAuditService auditService) : Controller
{
    private const long MaxResumeFileSize = 5 * 1024 * 1024;
    private const long MaxAvatarFileSize = 5 * 1024 * 1024;
    private static readonly string[] AllowedResumeExtensions = [".pdf", ".doc", ".docx"];
    private static readonly string[] AllowedAvatarExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<IActionResult> Index(string? search, int? minExperience, bool showHidden = false, int page = 1)
    {
        var (candidates, totalItems, totalPages) = await candidateService.GetCandidatesAsync(search, minExperience, showHidden, page, 25);

        ViewData["CurrentFilter"] = search;
        ViewData["MinExperience"] = minExperience;
        ViewData["ShowHidden"] = showHidden;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;

        return View(candidates);
    }

    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ExportPolicy")]
    public async Task<IActionResult> ExportCsv(string? search, int? minExperience, bool showHidden = false)
    {
        var candidates = await candidateService.GetCandidatesForExportAsync(search, minExperience, showHidden);
        await auditService.LogAsync("ExportCsv", "Candidate", null, $"Експорт бази кандидатів ({candidates.Count} осіб)");

        var fileBytes = exportService.BuildCandidatesCsv(candidates);
        var fileName = $"candidates-{DateTime.Today:yyyy-MM-dd}.csv";
        return File(fileBytes, "text/csv; charset=utf-8", fileName);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var candidate = await candidateService.GetCandidateDetailsAsync(id.Value);
        if (candidate is null) return NotFound();

        ViewBag.Interviews = await candidateService.GetCandidateInterviewsAsync(id.Value);
        ViewBag.ParsedResume = candidateService.ParseResume(candidate.ResumeFilePath);

        return View(candidate);
    }

    public async Task<IActionResult> DownloadResume(int id)
    {
        var candidate = await candidateService.GetCandidateDetailsAsync(id);
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.ResumeFilePath))
        {
            return NotFound();
        }

        var presignedUrl = await candidateService.GetPresignedResumeUrlAsync(candidate.ResumeFilePath);
        if (!string.IsNullOrWhiteSpace(presignedUrl) && !presignedUrl.StartsWith("/Candidates/"))
        {
            await auditService.LogAsync("DownloadResumePresigned", "Candidate", candidate.Id.ToString(), $"Видано pre-signed посилання на резюме {candidate.FullName}");
            return Redirect(presignedUrl);
        }

        var (stream, contentType, downloadName) = await candidateService.GetResumeFileStreamAsync(candidate.ResumeFilePath, candidate.FullName);
        if (stream is null)
        {
            return NotFound();
        }

        await auditService.LogAsync("DownloadResume", "Candidate", candidate.Id.ToString(), $"Скачано резюме кандидата {candidate.FullName}");
        return File(stream, contentType, downloadName);
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

        if (await candidateService.EmailExistsAsync(candidate.Email))
        {
            ModelState.AddModelError(nameof(Candidate.Email), "Кандидат із такою електронною поштою вже існує.");
            return View(candidate);
        }

        await candidateService.CreateCandidateAsync(candidate, resumeFile, avatarFile);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadResume(int id, IFormFile? resumeFile)
    {
        ValidateResumeFile(resumeFile, requireFile: true);
        if (!ModelState.IsValid)
        {
            var resumeError = ModelState.TryGetValue(nameof(Candidate.ResumeFilePath), out var entry)
                ? entry.Errors.FirstOrDefault()?.ErrorMessage
                : null;
            TempData["ResumeError"] = resumeError ?? "Файл резюме не вибрано.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var (success, error, _) = await candidateService.UploadResumeAsync(id, resumeFile!);
        if (!success)
        {
            TempData["ResumeError"] = error ?? "Помилка завантаження файлу.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["ResumeMessage"] = "Резюме кандидата оновлено та проаналізовано.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var candidate = await candidateService.GetCandidateDetailsAsync(id.Value);
        return candidate is null ? NotFound() : View(candidate);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,FullName,Email,Phone,Skills,ResumeSummary,ExperienceYears")] Candidate candidate,
        IFormFile? resumeFile,
        IFormFile? avatarFile)
    {
        if (id != candidate.Id) return NotFound();
        ValidateResumeFile(resumeFile);
        ValidateAvatarFile(avatarFile);

        if (!ModelState.IsValid)
        {
            var existing = await candidateService.GetCandidateDetailsAsync(id);
            if (existing != null)
            {
                candidate.ResumeFilePath = existing.ResumeFilePath;
                candidate.AvatarUrl = existing.AvatarUrl;
                candidate.CreatedAt = existing.CreatedAt;
            }
            return View(candidate);
        }

        if (await candidateService.EmailExistsAsync(candidate.Email, candidate.Id))
        {
            ModelState.AddModelError(nameof(Candidate.Email), "Кандидат із такою електронною поштою вже існує.");
            return View(candidate);
        }

        var (success, _) = await candidateService.UpdateCandidateAsync(candidate, resumeFile, avatarFile);
        if (!success) return NotFound();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var candidate = await candidateService.GetCandidateDetailsAsync(id.Value);
        return candidate is null ? NotFound() : View(candidate);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await candidateService.SoftDeleteCandidateAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        await candidateService.RestoreCandidateAsync(id);
        return RedirectToAction(nameof(Index), new { showHidden = true });
    }

    [HttpGet]
    public async Task<IActionResult> QuickViewJson(int id)
    {
        var data = await candidateService.GetQuickViewDataAsync(id);
        return data is null ? NotFound(new { success = false, message = "Кандидата не знайдено." }) : Json(data);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessageAjax([FromBody] SendCandidateMessageRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { success = false, message = "Тема та текст повідомлення обов'язкові для заповнення." });
        }

        var senderName = User.Identity?.Name ?? "Рекрутер";
        var (success, error, communication) = await candidateService.SendMessageAsync(
            request.CandidateId,
            request.Subject,
            request.Body,
            request.Channel,
            request.TemplateType,
            senderName);

        if (!success)
        {
            return NotFound(new { success = false, message = error ?? "Помилка надсилання листа." });
        }

        return Json(new
        {
            success = true,
            message = "Лист успішно надіслано та збережено в історії комунікацій.",
            communication = new
            {
                id = communication!.Id,
                senderName = communication.SenderName,
                recipientEmail = communication.RecipientEmail,
                subject = communication.Subject,
                body = communication.Body,
                channel = communication.Channel,
                sentAt = communication.SentAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                status = communication.Status
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNoteAjax([FromBody] AddCandidateNoteRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Note))
        {
            return BadRequest(new { success = false, message = "Текст нотатки не може бути порожнім." });
        }

        var author = User.Identity?.Name ?? "Рекрутер";
        var (success, error, note, timestamp, noteAuthor) = await candidateService.AddNoteAsync(
            request.CandidateId,
            request.ApplicationId,
            request.Note,
            author);

        if (!success)
        {
            return NotFound(new { success = false, message = error ?? "Кандидата не знайдено." });
        }

        return Json(new
        {
            success = true,
            note = note,
            timestamp = timestamp,
            author = noteAuthor,
            rawNote = request.Note.Trim()
        });
    }

    [HttpGet]
    public IActionResult QuickImport() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult QuickImportParse([FromBody] QuickImportParseRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RawText))
        {
            return BadRequest(new { success = false, message = "Вставте текст резюме або профілю." });
        }

        var text = request.RawText;
        var emailMatch = System.Text.RegularExpressions.Regex.Match(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        var email = emailMatch.Success ? emailMatch.Value : "";

        var phoneMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:\+?380|0)\d{9}|(?:\+?\d{1,3}[\s-]?)?\(?\d{3}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}");
        var phone = phoneMatch.Success ? phoneMatch.Value.Trim() : "";

        var expMatch = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s*(?:роки|років|року|years|year|р\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var experienceYears = expMatch.Success && int.TryParse(expMatch.Groups[1].Value, out var exp) ? exp : 0;

        var knownSkills = new[]
        {
            "C#", ".NET", "ASP.NET", "Entity Framework", "SQL", "PostgreSQL", "MSSQL", "MySQL",
            "JavaScript", "TypeScript", "React", "Angular", "Vue", "Node.js", "Docker",
            "Kubernetes", "AWS", "Azure", "Git", "REST API", "Microservices", "CI/CD",
            "Redis", "HTML", "CSS", "Python", "Java", "Go", "Figma", "Jira", "Linux"
        };

        var matchedSkills = new List<string>();
        foreach (var skill in knownSkills)
        {
            var pattern = $@"(?<!\w){System.Text.RegularExpressions.Regex.Escape(skill)}(?!\w)";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                matchedSkills.Add(skill);
            }
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fullName = "";
        foreach (var line in lines.Take(5))
        {
            if (!line.Contains('@') && !line.Contains("http") && !line.Contains("+") && line.Length >= 4 && line.Length <= 60 && line.Split(' ').Length >= 2)
            {
                fullName = line;
                break;
            }
        }

        return Json(new
        {
            success = true,
            fullName = fullName,
            email = email,
            phone = phone,
            experienceYears = experienceYears,
            skills = string.Join(", ", matchedSkills),
            summary = text.Length > 500 ? text.Substring(0, 500) + "..." : text
        });
    }

    private void ValidateResumeFile(IFormFile? resumeFile, bool requireFile = false)
    {
        if (resumeFile is null || resumeFile.Length == 0)
        {
            if (requireFile)
            {
                ModelState.AddModelError(nameof(Candidate.ResumeFilePath), "Оберіть файл резюме.");
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
        if (avatarFile is null || avatarFile.Length == 0) return;

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
}

public record AddCandidateNoteRequest(int CandidateId, int? ApplicationId, string Note);
public record QuickImportParseRequest(string RawText);
public record SendCandidateMessageRequest(int CandidateId, string Subject, string Body, string Channel, string? TemplateType);
