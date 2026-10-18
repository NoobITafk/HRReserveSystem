using HRReserveSystem.Data;
using HRReserveSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class DashboardService(ApplicationDbContext context) : IDashboardService
{
    public async Task<DashboardViewModel> GetDashboardDataAsync(CancellationToken ct = default)
    {
        var applicationStatusCounts = await context.Applications
            .AsNoTracking()
            .GroupBy(application => application.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, ct);

        var vacancyStatusCounts = await context.Vacancies
            .AsNoTracking()
            .Where(vacancy => !vacancy.IsArchived)
            .GroupBy(vacancy => vacancy.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, ct);

        var softSkillScores = await context.SoftSkillAssessments
            .AsNoTracking()
            .Select(assessment => (assessment.Communication + assessment.Teamwork + assessment.Responsibility + assessment.StressResistance + assessment.Leadership) / 5.0)
            .ToListAsync(ct);

        // Compute weekly deltas (this 7-day window vs previous 7-day window)
        var now = DateTime.UtcNow;
        var thisWindowStart = now.AddDays(-7);
        var prevWindowStart = now.AddDays(-14);
        var prevWindowEnd = thisWindowStart;

        var thisWeekCandidates = await context.Candidates.CountAsync(c => !c.IsDeleted && c.CreatedAt >= thisWindowStart, ct);
        var prevWeekCandidates = await context.Candidates.CountAsync(c => !c.IsDeleted && c.CreatedAt >= prevWindowStart && c.CreatedAt < prevWindowEnd, ct);
        var candidateDelta = thisWeekCandidates - prevWeekCandidates;

        var thisWeekVacancies = await context.Vacancies.CountAsync(v => !v.IsArchived && v.CreatedAt >= thisWindowStart, ct);
        var prevWeekVacancies = await context.Vacancies.CountAsync(v => !v.IsArchived && v.CreatedAt >= prevWindowStart && v.CreatedAt < prevWindowEnd, ct);
        var vacancyDelta = thisWeekVacancies - prevWeekVacancies;

        var thisWeekApplications = await context.Applications.CountAsync(a => a.AppliedAt >= thisWindowStart, ct);
        var prevWeekApplications = await context.Applications.CountAsync(a => a.AppliedAt >= prevWindowStart && a.AppliedAt < prevWindowEnd, ct);
        var applicationDelta = thisWeekApplications - prevWeekApplications;

        var thisWeekInterviews = await context.Interviews.CountAsync(i => i.InterviewDate >= thisWindowStart && i.InterviewDate < now, ct);
        var prevWeekInterviews = await context.Interviews.CountAsync(i => i.InterviewDate >= prevWindowStart && i.InterviewDate < prevWindowEnd, ct);
        var interviewDelta = thisWeekInterviews - prevWeekInterviews;

        var thisWeekAccepted = await context.Applications.CountAsync(a => a.Status == "Hired" && a.AppliedAt >= thisWindowStart, ct);
        var prevWeekAccepted = await context.Applications.CountAsync(a => a.Status == "Hired" && a.AppliedAt >= prevWindowStart && a.AppliedAt < prevWindowEnd, ct);
        var acceptedDelta = thisWeekAccepted - prevWeekAccepted;

        var thisWeekRejected = await context.Applications.CountAsync(a => a.Status == "Rejected" && a.AppliedAt >= thisWindowStart, ct);
        var prevWeekRejected = await context.Applications.CountAsync(a => a.Status == "Rejected" && a.AppliedAt >= prevWindowStart && a.AppliedAt < prevWindowEnd, ct);
        var rejectedDelta = thisWeekRejected - prevWeekRejected;

        var dashboard = new DashboardViewModel
        {
            CandidateCount = await context.Candidates.CountAsync(candidate => !candidate.IsDeleted, ct),
            VacancyCount = await context.Vacancies.CountAsync(vacancy => !vacancy.IsArchived, ct),
            ApplicationCount = await context.Applications.CountAsync(ct),
            InterviewCount = await context.Interviews.CountAsync(ct),
            RecruiterCount = await context.Recruiters.CountAsync(ct),
            AcceptedCandidateCount = await context.Applications
                .Where(application => application.Status == "Hired")
                .Select(application => application.CandidateId)
                .Distinct()
                .CountAsync(ct),
            RejectedCandidateCount = await context.Applications
                .Where(application => application.Status == "Rejected")
                .Select(application => application.CandidateId)
                .Distinct()
                .CountAsync(ct),
            AverageSoftSkillScore = softSkillScores.Count == 0 ? 0 : Math.Round(softSkillScores.Average(), 1),
            RecentCandidates = await context.Candidates
                .AsNoTracking()
                .Where(candidate => !candidate.IsDeleted)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .Take(5)
                .ToListAsync(ct),
            RecentVacancies = await context.Vacancies
                .AsNoTracking()
                .Where(vacancy => !vacancy.IsArchived)
                .OrderByDescending(vacancy => vacancy.CreatedAt)
                .Take(5)
                .ToListAsync(ct),
            UpcomingInterviews = await context.Interviews
                .AsNoTracking()
                .Include(interview => interview.Application)
                    .ThenInclude(application => application!.Candidate)
                .Include(interview => interview.Application)
                    .ThenInclude(application => application!.Vacancy)
                .Include(interview => interview.Recruiter)
                .OrderByDescending(interview => interview.InterviewDate)
                .Take(6)
                .ToListAsync(ct),
            FeedbackCount = await context.InterviewFeedbacks.CountAsync(ct),
            RecentFeedbacks = await context.InterviewFeedbacks
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
                .ToListAsync(ct),
            RecentSoftSkillAssessments = await context.SoftSkillAssessments
                .AsNoTracking()
                .Include(s => s.Candidate)
                .OrderByDescending(s => s.Id)
                .Take(4)
                .ToListAsync(ct),
            ApplicationStatusCounts = applicationStatusCounts,
            VacancyStatusCounts = vacancyStatusCounts,
            CandidateDelta = candidateDelta,
            VacancyDelta = vacancyDelta,
            ApplicationDelta = applicationDelta,
            InterviewDelta = interviewDelta,
            AcceptedDelta = acceptedDelta,
            RejectedDelta = rejectedDelta
        };

        return dashboard;
    }
}
