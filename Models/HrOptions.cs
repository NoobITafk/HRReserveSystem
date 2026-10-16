namespace HRReserveSystem.Models;

public static class HrOptions
{
    public static readonly string[] ApplicationStatuses =
    [
        "New",
        "Screening",
        "Interview",
        "TestTask",
        "Offer",
        "Hired",
        "Rejected"
    ];

    public static readonly string[] VacancyStatuses =
    [
        "Open",
        "Paused",
        "Closed"
    ];

    public static readonly string[] InterviewTypes =
    [
        "HR",
        "Technical",
        "Final"
    ];

    public static readonly string[] InterviewResults =
    [
        "Pending",
        "Passed",
        "Failed"
    ];

    public static readonly string[] FeedbackRecommendations =
    [
        "Hire",
        "Maybe",
        "Reject"
    ];

    public static readonly string[] RecruiterRoles =
    [
        "Admin",
        "Recruiter",
        "Interviewer"
    ];

    public static string ApplicationStatusBadge(string? status) => status switch
    {
        "New" => "badge-soft badge-soft-slate",
        "Screening" => "badge-soft badge-soft-blue",
        "Interview" => "badge-soft badge-soft-amber",
        "TestTask" => "badge-soft badge-soft-cyan",
        "Offer" => "badge-soft badge-soft-emerald",
        "Hired" => "badge-soft badge-soft-emerald",
        "Rejected" => "badge-soft badge-soft-rose",
        _ => "badge-soft badge-soft-slate"
    };

    public static string ApplicationStatusLabel(string? status) => status switch
    {
        "New" => "Нова",
        "Screening" => "Скринінг",
        "Interview" => "Співбесіда",
        "TestTask" => "Тестове завдання",
        "Offer" => "Оффер",
        "Hired" => "Прийнято",
        "Rejected" => "Відхилено",
        _ => status ?? "Невідомо"
    };

    public static string VacancyStatusBadge(string? status) => status switch
    {
        "Open" => "badge-soft badge-soft-emerald",
        "Paused" => "badge-soft badge-soft-amber",
        "Closed" => "badge-soft badge-soft-slate",
        _ => "badge-soft badge-soft-slate"
    };

    public static string VacancyStatusLabel(string? status) => status switch
    {
        "Open" => "Відкрита",
        "Paused" => "На паузі",
        "Closed" => "Закрита",
        _ => status ?? "Невідомо"
    };

    public static string InterviewTypeLabel(string? type) => type switch
    {
        "HR" => "HR-інтерв'ю",
        "Technical" => "Технічна",
        "Final" => "Фінальна",
        _ => type ?? "Співбесіда"
    };

    public static string InterviewResultBadge(string? result) => result switch
    {
        "Pending" => "badge-soft badge-soft-amber",
        "Passed" => "badge-soft badge-soft-emerald",
        "Failed" => "badge-soft badge-soft-rose",
        _ => "badge-soft badge-soft-slate"
    };

    public static string InterviewResultLabel(string? result) => result switch
    {
        "Pending" => "Очікує",
        "Passed" => "Успішно",
        "Failed" => "Не пройдено",
        _ => result ?? "Очікує"
    };

    public static string FeedbackRecommendationBadge(string? recommendation) => recommendation switch
    {
        "Hire" => "badge-soft badge-soft-emerald",
        "Maybe" => "badge-soft badge-soft-amber",
        "Reject" => "badge-soft badge-soft-rose",
        _ => "badge-soft badge-soft-slate"
    };

    public static string FeedbackRecommendationLabel(string? recommendation) => recommendation switch
    {
        "Hire" => "Рекомендовано до найму",
        "Maybe" => "Кадровий резерв / Під питанням",
        "Reject" => "Не рекомендовано",
        _ => recommendation ?? "Без оцінки"
    };

    public static string RecruiterRoleBadge(string? role) => role switch
    {
        "Admin" => "badge-soft badge-soft-violet",
        "Recruiter" => "badge-soft badge-soft-blue",
        "Interviewer" => "badge-soft badge-soft-cyan",
        _ => "badge-soft badge-soft-slate"
    };

    public static string RecruiterRoleLabel(string? role) => role switch
    {
        "Admin" => "Адміністратор",
        "Recruiter" => "Рекрутер",
        "Interviewer" => "Інтерв'юер",
        _ => role ?? "Користувач"
    };
}
