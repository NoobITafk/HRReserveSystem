using HRReserveSystem.Models;

namespace HRReserveSystem.ViewModels;

public class DashboardViewModel
{
    public int CandidateCount { get; set; }

    public int VacancyCount { get; set; }

    public int ApplicationCount { get; set; }

    public int InterviewCount { get; set; }

    public int RecruiterCount { get; set; }

    public int AcceptedCandidateCount { get; set; }

    public int RejectedCandidateCount { get; set; }

    // Delta values for dashboard (e.g. change compared to previous 7-day window)
    public int CandidateDelta { get; set; }

    public int VacancyDelta { get; set; }

    public int ApplicationDelta { get; set; }

    public int InterviewDelta { get; set; }

    public int AcceptedDelta { get; set; }

    public int RejectedDelta { get; set; }

    public double AverageSoftSkillScore { get; set; }

    public IReadOnlyList<Candidate> RecentCandidates { get; set; } = [];

    public IReadOnlyList<Vacancy> RecentVacancies { get; set; } = [];

    public IReadOnlyList<Interview> UpcomingInterviews { get; set; } = [];

    public int FeedbackCount { get; set; }

    public IReadOnlyList<InterviewFeedback> RecentFeedbacks { get; set; } = [];

    public IReadOnlyList<SoftSkillAssessment> RecentSoftSkillAssessments { get; set; } = [];

    public IReadOnlyDictionary<string, int> ApplicationStatusCounts { get; set; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> VacancyStatusCounts { get; set; } = new Dictionary<string, int>();
}
