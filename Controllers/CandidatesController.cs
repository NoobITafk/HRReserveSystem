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
    ICandidateFileStorageService fileStorage,
    ICandidateExportService exportService,
    ResumeIntelligenceService resumeIntelligence,
    IEmailBackgroundQueue emailQueue,
    IAuditService auditService,
    ILogger<CandidatesController> logger) : Controller
{
    private const long MaxResumeFileSize = 5 * 1024 * 1024;
    private const long MaxAvatarFileSize = 5 * 1024 * 1024;
    private static readonly string[] AllowedResumeExtensions = [".pdf", ".doc", ".docx"];
    private static readonly string[] AllowedAvatarExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<IActionResult> Index(string? search, int? minExperience, bool showHidden = false, int page = 1)
    {
        const int pageSize = 25;
        if (page < 1) page = 1;

        var query = ApplyFilters(context.Candidates.AsNoTracking().Where(candidate => candidate.IsDeleted == showHidden), search, minExperience);

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages) page = totalPages;

        var candidates = await query
            .OrderBy(candidate => candidate.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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
        var candidates = await ApplyFilters(context.Candidates.AsNoTracking().Where(candidate => candidate.IsDeleted == showHidden), search, minExperience)
            .OrderBy(candidate => candidate.FullName)
            .ToListAsync();

        await auditService.LogAsync("ExportCsv", "Candidate", null, $"Експорт бази кандидатів ({candidates.Count} осіб)");

        var fileBytes = exportService.BuildCandidatesCsv(candidates);
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
            .Include(item => item.Communications)
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
            var fullPath = fileStorage.GetPhysicalResumePath(candidate.ResumeFilePath);
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
        var physicalPath = fileStorage.GetPhysicalResumePath(fileName);
        if (!System.IO.File.Exists(physicalPath))
        {
            return NotFound();
        }

        await auditService.LogAsync("DownloadResume", "Candidate", candidate.Id.ToString(), $"Скачано резюме кандидата {candidate.FullName}");

        return PhysicalFile(physicalPath, fileStorage.GetResumeContentType(fileName), fileName);
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

        var uploadedResumePath = await fileStorage.SaveResumeFileAsync(resumeFile);
        if (!string.IsNullOrWhiteSpace(uploadedResumePath))
        {
            candidate.ResumeFilePath = uploadedResumePath;
            var fullPath = fileStorage.GetPhysicalResumePath(uploadedResumePath);
            var parsed = resumeIntelligence.ParseResumeFile(fullPath);
            if (parsed.Success && string.IsNullOrWhiteSpace(candidate.ResumeSummary) && !string.IsNullOrWhiteSpace(parsed.Summary))
            {
                candidate.ResumeSummary = parsed.Summary;
            }
        }

        var uploadedAvatarPath = await fileStorage.SaveAvatarFileAsync(avatarFile);
        if (!string.IsNullOrWhiteSpace(uploadedAvatarPath))
        {
            candidate.AvatarUrl = uploadedAvatarPath;
        }

        candidate.CreatedAt = DateTime.UtcNow;
        context.Add(candidate);
        await context.SaveChangesAsync();
        await auditService.LogAsync("CreateCandidate", "Candidate", candidate.Id.ToString(), $"Створено кандидата {candidate.FullName}");

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
        var uploadedResumePath = await fileStorage.SaveResumeFileAsync(resumeFile);
        if (string.IsNullOrWhiteSpace(uploadedResumePath))
        {
            TempData["ResumeError"] = "Файл резюме не вибрано.";
            return RedirectToAction(nameof(Details), new { id });
        }

        fileStorage.DeleteStoredResume(oldResumePath);
        candidate.ResumeFilePath = uploadedResumePath;

        var fullPath = fileStorage.GetPhysicalResumePath(uploadedResumePath);
        var parsed = resumeIntelligence.ParseResumeFile(fullPath);
        if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
        {
            candidate.ResumeSummary = parsed.Summary;
        }

        await context.SaveChangesAsync();
        await auditService.LogAsync("UploadResume", "Candidate", id.ToString(), $"Оновлено резюме кандидата {candidate.FullName}");

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
                fileStorage.DeleteStoredResume(oldResumePath);
                existingCandidate.ResumeFilePath = null;
            }

            var uploadedResumePath = await fileStorage.SaveResumeFileAsync(resumeFile);
            if (!string.IsNullOrWhiteSpace(uploadedResumePath))
            {
                fileStorage.DeleteStoredResume(oldResumePath);
                existingCandidate.ResumeFilePath = uploadedResumePath;
                var fullPath = fileStorage.GetPhysicalResumePath(uploadedResumePath);
                var parsed = resumeIntelligence.ParseResumeFile(fullPath);
                if (parsed.Success && !string.IsNullOrWhiteSpace(parsed.Summary))
                {
                    existingCandidate.ResumeSummary = parsed.Summary;
                }
            }

            if (removeAvatar)
            {
                fileStorage.DeleteStoredAvatar(oldAvatarPath);
                existingCandidate.AvatarUrl = null;
            }

            var uploadedAvatarPath = await fileStorage.SaveAvatarFileAsync(avatarFile);
            if (!string.IsNullOrWhiteSpace(uploadedAvatarPath))
            {
                fileStorage.DeleteStoredAvatar(oldAvatarPath);
                existingCandidate.AvatarUrl = uploadedAvatarPath;
            }

            await context.SaveChangesAsync();
            await auditService.LogAsync("EditCandidate", "Candidate", existingCandidate.Id.ToString(), $"Відредаговано кандидата {existingCandidate.FullName}");
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
            await auditService.LogAsync("DeleteCandidate", "Candidate", id.ToString(), $"Видалено (переміщено в кошик) кандидата {candidate.FullName}");
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
            await auditService.LogAsync("RestoreCandidate", "Candidate", id.ToString(), $"Відновлено з кошика кандидата {candidate.FullName}");
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

    [HttpGet]
    public async Task<IActionResult> QuickViewJson(int id)
    {
        var candidate = await context.Candidates
            .AsNoTracking()
            .Include(c => c.Applications)
                .ThenInclude(a => a.Vacancy)
            .Include(c => c.SoftSkillAssessments)
            .Include(c => c.Communications)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

        if (candidate is null)
        {
            return NotFound(new { success = false, message = "Кандидата не знайдено." });
        }

        var interviews = await context.Interviews
            .AsNoTracking()
            .Include(i => i.Application)
                .ThenInclude(a => a!.Vacancy)
            .Include(i => i.Recruiter)
            .Where(i => i.Application != null && i.Application.CandidateId == candidate.Id)
            .OrderByDescending(i => i.InterviewDate)
            .Select(i => new
            {
                id = i.Id,
                interviewType = i.InterviewType,
                interviewTypeLabel = HrOptions.InterviewTypeLabel(i.InterviewType),
                interviewDate = i.InterviewDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                notes = i.Notes,
                result = i.Result,
                resultBadge = HrOptions.InterviewResultBadge(i.Result),
                recruiterName = i.Recruiter != null ? i.Recruiter.FullName : "Рекрутер"
            })
            .ToListAsync();

        var latestSoftSkill = candidate.SoftSkillAssessments.LastOrDefault();
        object? softSkillsData = null;
        if (latestSoftSkill is not null)
        {
            var avg = (latestSoftSkill.Communication + latestSoftSkill.Teamwork + latestSoftSkill.Responsibility + latestSoftSkill.StressResistance + latestSoftSkill.Leadership) / 5.0;
            softSkillsData = new
            {
                communication = latestSoftSkill.Communication,
                teamwork = latestSoftSkill.Teamwork,
                responsibility = latestSoftSkill.Responsibility,
                stressResistance = latestSoftSkill.StressResistance,
                leadership = latestSoftSkill.Leadership,
                average = Math.Round(avg, 1)
            };
        }

        var communications = candidate.Communications
            .OrderByDescending(c => c.SentAt)
            .Take(10)
            .Select(c => new
            {
                id = c.Id,
                senderName = c.SenderName,
                recipientEmail = c.RecipientEmail,
                subject = c.Subject,
                body = c.Body,
                channel = c.Channel,
                sentAt = c.SentAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                status = c.Status
            })
            .ToList();

        return Json(new
        {
            success = true,
            id = candidate.Id,
            fullName = candidate.FullName,
            email = candidate.Email,
            phone = candidate.Phone ?? "Не вказано",
            skills = candidate.Skills,
            experienceYears = candidate.ExperienceYears,
            avatarUrl = candidate.AvatarUrl,
            resumeFilePath = candidate.ResumeFilePath,
            resumeSummary = candidate.ResumeSummary,
            hasResume = !string.IsNullOrWhiteSpace(candidate.ResumeFilePath),
            applications = candidate.Applications.Select(a => new
            {
                id = a.Id,
                vacancyId = a.VacancyId,
                vacancyTitle = a.Vacancy?.Title ?? "Загальний резерв",
                status = a.Status,
                statusLabel = HrOptions.ApplicationStatusLabel(a.Status),
                statusBadge = HrOptions.ApplicationStatusBadge(a.Status),
                appliedAt = a.AppliedAt.ToLocalTime().ToString("dd.MM.yyyy"),
                recruiterComment = a.RecruiterComment ?? ""
            }),
            interviews = interviews,
            softSkills = softSkillsData,
            communications = communications
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessageAjax([FromBody] SendCandidateMessageRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { success = false, message = "Тема та текст повідомлення обов'язкові для заповнення." });
        }

        var candidate = await context.Candidates
            .FirstOrDefaultAsync(c => c.Id == request.CandidateId && !c.IsDeleted);

        if (candidate is null)
        {
            return NotFound(new { success = false, message = "Кандидата не знайдено." });
        }

        if (string.IsNullOrWhiteSpace(candidate.Email))
        {
            return BadRequest(new { success = false, message = "У кандидата відсутня електронна пошта." });
        }

        var recipientEmail = candidate.Email;
        var recipientName = candidate.FullName;
        var emailSubject = request.Subject.Trim();
        var emailBody = request.Body.Trim();
        var senderName = User.Identity?.Name ?? "Рекрутер";

        // Transactional Durable Outbox: persist message in DB
        var outboxMessage = new EmailOutboxMessage
        {
            RecipientEmail = recipientEmail,
            RecipientName = recipientName,
            Subject = emailSubject,
            Body = emailBody,
            TemplateType = request.TemplateType,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        context.EmailOutboxMessages.Add(outboxMessage);

        var communication = new CandidateCommunication
        {
            CandidateId = candidate.Id,
            SenderName = senderName,
            RecipientEmail = candidate.Email,
            Subject = emailSubject,
            Body = emailBody,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "Email" : request.Channel,
            SentAt = DateTime.UtcNow,
            Status = "Sent"
        };
        context.CandidateCommunications.Add(communication);

        context.AppNotifications.Add(new AppNotification
        {
            Title = $"Надіслано листа кандидату {candidate.FullName}",
            Message = $"Тема: {emailSubject}",
            TargetUrl = $"/Candidates/Details/{candidate.Id}",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        // Queue in-memory trigger for immediate delivery
        await emailQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, cancellationToken) =>
        {
            var service = serviceProvider.GetRequiredService<IEmailNotificationService>();
            var queueLogger = serviceProvider.GetRequiredService<ILogger<CandidatesController>>();
            try
            {
                await service.SendCandidateMessageAsync(recipientEmail, recipientName, emailSubject, emailBody);
            }
            catch (Exception ex)
            {
                queueLogger.LogWarning(ex, "Background email sending encountered warning for {Email}", recipientEmail);
            }
        });

        await auditService.LogAsync("SendOutreachEmail", "Candidate", candidate.Id.ToString(), $"Надіслано лист кандидату {candidate.FullName}: {emailSubject}");
        logger.LogInformation("Outreach email queued, outboxed, and recorded for candidate {CandidateId} ({Email}) by {Sender}", candidate.Id, candidate.Email, senderName);

        return Json(new
        {
            success = true,
            message = $"Лист для {candidate.FullName} успішно надіслано та збережено в історії комунікацій.",
            communication = new
            {
                id = communication.Id,
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

        var candidate = await context.Candidates
            .Include(c => c.Applications)
            .FirstOrDefaultAsync(c => c.Id == request.CandidateId && !c.IsDeleted);

        if (candidate is null)
        {
            return NotFound(new { success = false, message = "Кандидата не знайдено." });
        }

        var timestamp = DateTime.Now.ToString("dd.MM HH:mm");
        var author = User.Identity?.Name ?? "Рекрутер";
        var newEntry = $"[{timestamp} {author}]: {request.Note.Trim()}";

        var application = request.ApplicationId.HasValue
            ? candidate.Applications.FirstOrDefault(a => a.Id == request.ApplicationId.Value)
            : candidate.Applications.OrderByDescending(a => a.AppliedAt).FirstOrDefault();

        if (application is not null)
        {
            application.RecruiterComment = string.IsNullOrWhiteSpace(application.RecruiterComment)
                ? newEntry
                : $"{newEntry}\n{application.RecruiterComment}";
        }
        else
        {
            candidate.ResumeSummary = string.IsNullOrWhiteSpace(candidate.ResumeSummary)
                ? newEntry
                : $"{newEntry}\n{candidate.ResumeSummary}";
        }

        await context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            note = newEntry,
            timestamp = timestamp,
            author = author,
            rawNote = request.Note.Trim()
        });
    }

    [HttpGet]
    public IActionResult QuickImport()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult QuickImportParse([FromBody] QuickImportParseRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RawText))
        {
            return BadRequest(new { success = false, message = "Вставте текст резюме або профілю." });
        }

        var text = request.RawText;

        // 1. Extract Email
        var emailMatch = System.Text.RegularExpressions.Regex.Match(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        var email = emailMatch.Success ? emailMatch.Value : "";

        // 2. Extract Phone
        var phoneMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:\+?380|0)\d{9}|(?:\+?\d{1,3}[\s-]?)?\(?\d{3}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}");
        var phone = phoneMatch.Success ? phoneMatch.Value.Trim() : "";

        // 3. Extract Experience Years
        var expMatch = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\s*(?:роки|років|року|years|year|р\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var experienceYears = expMatch.Success && int.TryParse(expMatch.Groups[1].Value, out var exp) ? exp : 0;

        // 4. Extract Known Tech Skills
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

        // 5. Extract Full Name (from first valid line)
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
}

public record AddCandidateNoteRequest(int CandidateId, int? ApplicationId, string Note);
public record QuickImportParseRequest(string RawText);
public record SendCandidateMessageRequest(int CandidateId, string Subject, string Body, string Channel, string? TemplateType);
