using System.Net;
using System.Net.Mail;
using System.Text;
using HRReserveSystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRReserveSystem.Services;

public class EmailNotificationService(
    ApplicationDbContext context,
    IWebHostEnvironment environment,
    IOptions<EmailOptions> options,
    ILogger<EmailNotificationService> logger) : IEmailNotificationService
{
    private readonly EmailOptions emailOptions = options.Value;

    public Task SendInterviewScheduledAsync(int interviewId)
    {
        return SendInterviewNotificationAsync(interviewId, "Заплановано співбесіду");
    }

    public Task SendInterviewUpdatedAsync(int interviewId)
    {
        return SendInterviewNotificationAsync(interviewId, "Оновлено співбесіду");
    }

    public async Task SendCandidateMessageAsync(string toEmail, string candidateName, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return;
        }

        var recipients = new[] { toEmail };
        if (!string.IsNullOrWhiteSpace(emailOptions.RedirectAllTo))
        {
            recipients = [emailOptions.RedirectAllTo];
        }

        await DeliverAsync(recipients, subject, body);
    }

    private async Task SendInterviewNotificationAsync(int interviewId, string subjectPrefix)
    {
        var interview = await context.Interviews
            .AsNoTracking()
            .Include(item => item.Application)
                .ThenInclude(application => application!.Candidate)
            .Include(item => item.Application)
                .ThenInclude(application => application!.Vacancy)
            .Include(item => item.Recruiter)
            .FirstOrDefaultAsync(item => item.Id == interviewId);

        if (interview is null)
        {
            return;
        }

        var candidate = interview.Application?.Candidate;
        var vacancy = interview.Application?.Vacancy;
        var responsible = interview.Recruiter;
        var responsibleLabel = responsible is null
            ? "Не призначено"
            : string.IsNullOrWhiteSpace(responsible.Role)
                ? responsible.FullName
                : $"{responsible.FullName} ({responsible.Role})";
        var recipients = new[] { candidate?.Email, responsible?.Email }
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0)
        {
            return;
        }

        var originalRecipients = recipients;
        if (!string.IsNullOrWhiteSpace(emailOptions.RedirectAllTo))
        {
            recipients = [emailOptions.RedirectAllTo];
        }

        var subject = $"{subjectPrefix}: {candidate?.FullName ?? "кандидат"}";
        var body = new StringBuilder()
            .AppendLine(subject)
            .AppendLine()
            .AppendLine($"Оригінальні отримувачі: {string.Join(", ", originalRecipients)}")
            .AppendLine()
            .AppendLine($"Кандидат: {candidate?.FullName ?? "Не вказано"}")
            .AppendLine($"Вакансія: {vacancy?.Title ?? "Не вказано"}")
            .AppendLine($"Дата і час: {interview.InterviewDate:g}")
            .AppendLine($"Тип: {interview.InterviewType}")
            .AppendLine($"Результат: {interview.Result}")
            .AppendLine($"Відповідальний: {responsibleLabel}")
            .AppendLine()
            .AppendLine($"Коментар/опис: {interview.Notes ?? "Немає"}")
            .ToString();

        await DeliverAsync(recipients, subject, body);
    }

    private async Task DeliverAsync(IReadOnlyCollection<string> recipients, string subject, string body)
    {
        var outboxReason = "SMTP is disabled or host is empty.";

        if (emailOptions.Enabled && !string.IsNullOrWhiteSpace(emailOptions.Host))
        {
            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(emailOptions.FromEmail, emailOptions.FromName),
                    Subject = subject,
                    Body = body,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };

                foreach (var recipient in recipients)
                {
                    message.To.Add(recipient);
                }

                using var smtp = new SmtpClient(emailOptions.Host, emailOptions.Port)
                {
                    EnableSsl = emailOptions.UseSsl
                };

                if (!string.IsNullOrWhiteSpace(emailOptions.UserName))
                {
                    smtp.Credentials = new NetworkCredential(emailOptions.UserName, emailOptions.Password);
                }

                await smtp.SendMailAsync(message);
                return;
            }
            catch (Exception exception)
            {
                outboxReason = $"SMTP delivery failed: {exception.GetType().Name}: {exception.Message}";
                logger.LogWarning(exception, "SMTP delivery failed. Message will be saved to local outbox.");
            }
        }

        await SaveToOutboxAsync(recipients, subject, body, outboxReason);
    }

    private async Task SaveToOutboxAsync(IReadOnlyCollection<string> recipients, string subject, string body, string reason)
    {
        var outboxPath = Path.Combine(environment.ContentRootPath, emailOptions.OutboxPath);
        Directory.CreateDirectory(outboxPath);

        var safeName = string.Concat(subject.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safeName[..Math.Min(safeName.Length, 60)]}.txt";
        var content = new StringBuilder()
            .AppendLine($"To: {string.Join(", ", recipients)}")
            .AppendLine($"Subject: {subject}")
            .AppendLine($"CreatedUtc: {DateTime.UtcNow:O}")
            .AppendLine($"OutboxReason: {reason}")
            .AppendLine()
            .Append(body)
            .ToString();

        await File.WriteAllTextAsync(Path.Combine(outboxPath, fileName), content, Encoding.UTF8);
    }
}
