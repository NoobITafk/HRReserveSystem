using HRReserveSystem.Models;
using System.Text;

namespace HRReserveSystem.Services;

public class CandidateExportService : ICandidateExportService
{
    public byte[] BuildCandidatesCsv(IEnumerable<Candidate> candidates)
    {
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

        return fileBytes;
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
}
