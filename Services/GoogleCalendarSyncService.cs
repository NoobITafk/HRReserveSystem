using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRReserveSystem.Models;
using Microsoft.Extensions.Options;

namespace HRReserveSystem.Services;

public class GoogleCalendarSyncService(
    HttpClient httpClient,
    IOptions<GoogleCalendarOptions> options,
    ILogger<GoogleCalendarSyncService> logger) : IGoogleCalendarSyncService
{
    private readonly GoogleCalendarOptions _options = options.Value;

    public bool IsConfigured => _options.Enabled &&
                                !string.IsNullOrWhiteSpace(_options.ClientId) &&
                                !string.IsNullOrWhiteSpace(_options.ClientSecret) &&
                                !string.IsNullOrWhiteSpace(_options.RefreshToken);

    public async Task<(bool Success, string? EventId, string? Error)> CreateEventAsync(Interview interview, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogInformation("Google Calendar API is not configured. Skipping automated cloud sync for interview #{Id}", interview.Id);
            return (false, null, "Google Calendar API не налаштовано в конфігурації.");
        }

        try
        {
            var token = await GetAccessTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, null, "Не вдалося отримати OAuth токен Google.");
            }

            var start = DateTime.SpecifyKind(interview.InterviewDate, DateTimeKind.Local).ToUniversalTime();
            var end = start.AddHours(1);

            var eventBody = new
            {
                summary = $"Співбесіда: {interview.Application?.Candidate?.FullName ?? "Кандидат"} - {interview.Application?.Vacancy?.Title ?? "HR"}",
                description = $"Тип зустрічі: {interview.InterviewType}\nКандидат: {interview.Application?.Candidate?.FullName}\nEmail: {interview.Application?.Candidate?.Email}\nІнтерв'юер: {interview.Recruiter?.FullName}\n\nСтворено через HR Reserve System",
                start = new { dateTime = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") },
                end = new { dateTime = end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") },
                location = "Онлайн / Офіс",
                attendees = new[]
                {
                    new { email = interview.Application?.Candidate?.Email },
                    new { email = interview.Recruiter?.Email }
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(eventBody), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(_options.CalendarId)}/events")
            {
                Content = jsonContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Google Calendar event creation failed: {Status} {Error}", response.StatusCode, err);
                return (false, null, $"Помилка Google API: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var eventId = doc.RootElement.GetProperty("id").GetString();
            logger.LogInformation("Successfully created Google Calendar event {EventId} for interview #{InterviewId}", eventId, interview.Id);
            return (true, eventId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync interview #{Id} with Google Calendar", interview.Id);
            return (false, null, ex.Message);
        }
    }

    public async Task<bool> UpdateEventAsync(string eventId, Interview interview, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        try
        {
            var token = await GetAccessTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token)) return false;

            var start = DateTime.SpecifyKind(interview.InterviewDate, DateTimeKind.Local).ToUniversalTime();
            var end = start.AddHours(1);

            var eventBody = new
            {
                summary = $"Співбесіда: {interview.Application?.Candidate?.FullName ?? "Кандидат"} - {interview.Application?.Vacancy?.Title ?? "HR"}",
                description = $"Тип зустрічі: {interview.InterviewType}\nРезультат: {interview.Result}\nПримітки: {interview.Notes}",
                start = new { dateTime = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") },
                end = new { dateTime = end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(eventBody), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(_options.CalendarId)}/events/{Uri.EscapeDataString(eventId)}")
            {
                Content = jsonContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update Google Calendar event {EventId}", eventId);
            return false;
        }
    }

    public async Task<bool> DeleteEventAsync(string eventId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        try
        {
            var token = await GetAccessTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token)) return false;

            using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(_options.CalendarId)}/events/{Uri.EscapeDataString(eventId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete Google Calendar event {EventId}", eventId);
            return false;
        }
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var postData = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = _options.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(postData)
        };

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Failed to refresh Google OAuth token: {Status} {Error}", response.StatusCode, err);
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("access_token").GetString();
    }
}
