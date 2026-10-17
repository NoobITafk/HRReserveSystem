namespace HRReserveSystem.Services;

public interface IEmailNotificationService
{
    Task SendInterviewScheduledAsync(int interviewId);

    Task SendInterviewUpdatedAsync(int interviewId);

    Task SendCandidateMessageAsync(string toEmail, string candidateName, string subject, string body);
}
