using System.Diagnostics;
using HRReserveSystem.Data;
using HRReserveSystem.Models;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var applicationStatusCounts = await _context.Applications
            .AsNoTracking()
            .GroupBy(application => application.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);

        var vacancyStatusCounts = await _context.Vacancies
            .AsNoTracking()
            .Where(vacancy => !vacancy.IsArchived)
            .GroupBy(vacancy => vacancy.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count);

        var softSkillScores = await _context.SoftSkillAssessments
            .AsNoTracking()
            .Select(assessment => (assessment.Communication + assessment.Teamwork + assessment.Responsibility + assessment.StressResistance + assessment.Leadership) / 5.0)
            .ToListAsync();

        // Compute weekly deltas (this 7-day window vs previous 7-day window)
        var now = DateTime.UtcNow;
        var thisWindowStart = now.AddDays(-7);
        var prevWindowStart = now.AddDays(-14);
        var prevWindowEnd = thisWindowStart;

        var thisWeekCandidates = await _context.Candidates.CountAsync(c => !c.IsDeleted && c.CreatedAt >= thisWindowStart);
        var prevWeekCandidates = await _context.Candidates.CountAsync(c => !c.IsDeleted && c.CreatedAt >= prevWindowStart && c.CreatedAt < prevWindowEnd);
        var candidateDelta = thisWeekCandidates - prevWeekCandidates;

        var thisWeekVacancies = await _context.Vacancies.CountAsync(v => !v.IsArchived && v.CreatedAt >= thisWindowStart);
        var prevWeekVacancies = await _context.Vacancies.CountAsync(v => !v.IsArchived && v.CreatedAt >= prevWindowStart && v.CreatedAt < prevWindowEnd);
        var vacancyDelta = thisWeekVacancies - prevWeekVacancies;

        var thisWeekApplications = await _context.Applications.CountAsync(a => a.AppliedAt >= thisWindowStart);
        var prevWeekApplications = await _context.Applications.CountAsync(a => a.AppliedAt >= prevWindowStart && a.AppliedAt < prevWindowEnd);
        var applicationDelta = thisWeekApplications - prevWeekApplications;

        var thisWeekInterviews = await _context.Interviews.CountAsync(i => i.InterviewDate >= thisWindowStart && i.InterviewDate < now);
        var prevWeekInterviews = await _context.Interviews.CountAsync(i => i.InterviewDate >= prevWindowStart && i.InterviewDate < prevWindowEnd);
        var interviewDelta = thisWeekInterviews - prevWeekInterviews;

        var thisWeekAccepted = await _context.Applications.CountAsync(a => a.Status == "Hired" && a.AppliedAt >= thisWindowStart);
        var prevWeekAccepted = await _context.Applications.CountAsync(a => a.Status == "Hired" && a.AppliedAt >= prevWindowStart && a.AppliedAt < prevWindowEnd);
        var acceptedDelta = thisWeekAccepted - prevWeekAccepted;

        var thisWeekRejected = await _context.Applications.CountAsync(a => a.Status == "Rejected" && a.AppliedAt >= thisWindowStart);
        var prevWeekRejected = await _context.Applications.CountAsync(a => a.Status == "Rejected" && a.AppliedAt >= prevWindowStart && a.AppliedAt < prevWindowEnd);
        var rejectedDelta = thisWeekRejected - prevWeekRejected;

        var dashboard = new DashboardViewModel
        {
            CandidateCount = await _context.Candidates.CountAsync(candidate => !candidate.IsDeleted),
            VacancyCount = await _context.Vacancies.CountAsync(vacancy => !vacancy.IsArchived),
            ApplicationCount = await _context.Applications.CountAsync(),
            InterviewCount = await _context.Interviews.CountAsync(),
            RecruiterCount = await _context.Recruiters.CountAsync(),
            AcceptedCandidateCount = await _context.Applications
                .Where(application => application.Status == "Hired")
                .Select(application => application.CandidateId)
                .Distinct()
                .CountAsync(),
            RejectedCandidateCount = await _context.Applications
                .Where(application => application.Status == "Rejected")
                .Select(application => application.CandidateId)
                .Distinct()
                .CountAsync(),
            AverageSoftSkillScore = softSkillScores.Count == 0 ? 0 : Math.Round(softSkillScores.Average(), 1),
            RecentCandidates = await _context.Candidates
                .AsNoTracking()
                .Where(candidate => !candidate.IsDeleted)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .Take(5)
                .ToListAsync(),
            RecentVacancies = await _context.Vacancies
                .AsNoTracking()
                .Where(vacancy => !vacancy.IsArchived)
                .OrderByDescending(vacancy => vacancy.CreatedAt)
                .Take(5)
                .ToListAsync(),
            UpcomingInterviews = await _context.Interviews
                .AsNoTracking()
                .Include(interview => interview.Application)
                    .ThenInclude(application => application!.Candidate)
                .Include(interview => interview.Application)
                    .ThenInclude(application => application!.Vacancy)
                .Include(interview => interview.Recruiter)
                .OrderByDescending(interview => interview.InterviewDate)
                .Take(6)
                .ToListAsync(),
            FeedbackCount = await _context.InterviewFeedbacks.CountAsync(),
            RecentFeedbacks = await _context.InterviewFeedbacks
                .AsNoTracking()
                .Include(f => f.Interview)
                    .ThenInclude(i => i!.Application)
                        .ThenInclude(a => a!.Candidate)
                .Include(f => f.Interview)
                    .ThenInclude(i => i!.Application)
                        .ThenInclude(a => a!.Vacancy)
                .Include(f => f.Recruiter)
                .OrderByDescending(f => f.CreatedAt)
                .Take(5)
                .ToListAsync(),
            RecentSoftSkillAssessments = await _context.SoftSkillAssessments
                .AsNoTracking()
                .Include(s => s.Candidate)
                .OrderByDescending(s => s.Id)
                .Take(4)
                .ToListAsync(),
            ApplicationStatusCounts = applicationStatusCounts,
            VacancyStatusCounts = vacancyStatusCounts
        };

        // attach deltas
        dashboard.CandidateDelta = candidateDelta;
        dashboard.VacancyDelta = vacancyDelta;
        dashboard.ApplicationDelta = applicationDelta;
        dashboard.InterviewDelta = interviewDelta;
        dashboard.AcceptedDelta = acceptedDelta;
        dashboard.RejectedDelta = rejectedDelta;

        return View(dashboard);
    }

    [AllowAnonymous]
    public IActionResult About()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult CourseMaterials()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? statusCode = null)
    {
        if (statusCode.HasValue)
        {
            _logger.LogWarning("Request finished with status code {StatusCode}. TraceId: {TraceId}", statusCode, HttpContext.TraceIdentifier);
        }

        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = statusCode
        });
    }
}
