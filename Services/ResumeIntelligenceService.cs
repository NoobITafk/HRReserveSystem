using System.Text;
using System.Text.RegularExpressions;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace HRReserveSystem.Services;

public class ParsedResumeResult
{
    public string RawText { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> DetectedSkills { get; set; } = [];
    public int? DetectedExperienceYears { get; set; }
    public string? DetectedPhone { get; set; }
    public string? DetectedEmail { get; set; }
    public List<string> KeyHighlights { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ResumeIntelligenceService
{
    private static readonly string[] KnownTechSkills =
    [
        "C#", ".NET", "ASP.NET Core", "Entity Framework", "EF Core", "LINQ", "SQL", "PostgreSQL",
        "SQLite", "MySQL", "MS SQL", "Docker", "Kubernetes", "Git", "GitHub", "GitLab",
        "JavaScript", "TypeScript", "React", "Angular", "Vue", "Node.js", "HTML5", "CSS3",
        "Tailwind CSS", "Bootstrap", "REST API", "GraphQL", "Microservices", "RabbitMQ", "Kafka",
        "Redis", "Azure", "AWS", "GCP", "Linux", "CI/CD", "Unit Testing", "xUnit", "NUnit",
        "Playwright", "Selenium", "Python", "Java", "Kotlin", "Go", "Agile", "Scrum", "Jira"
    ];

    public ParsedResumeResult ParseResumeFile(string fullFilePath)
    {
        var result = new ParsedResumeResult();

        if (!File.Exists(fullFilePath))
        {
            result.ErrorMessage = "Файл резюме не знайдено на сервері.";
            return result;
        }

        try
        {
            var ext = Path.GetExtension(fullFilePath).ToLowerInvariant();
            string extractedText;

            if (ext == ".pdf")
            {
                extractedText = ExtractTextFromPdf(fullFilePath);
            }
            else if (ext == ".docx")
            {
                extractedText = ExtractTextFromDocx(fullFilePath);
            }
            else
            {
                // Fallback / plain text reading
                extractedText = File.ReadAllText(fullFilePath, Encoding.UTF8);
            }

            result = ParseText(extractedText);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Не вдалося повністю розібрати вміст файлу: {ex.Message}";
        }

        return result;
    }

    public ParsedResumeResult ParseText(string extractedText)
    {
        var result = new ParsedResumeResult
        {
            RawText = extractedText ?? string.Empty,
            DetectedSkills = DetectSkills(extractedText ?? string.Empty),
            DetectedExperienceYears = DetectExperienceYears(extractedText ?? string.Empty),
            DetectedPhone = DetectPhone(extractedText ?? string.Empty),
            DetectedEmail = DetectEmail(extractedText ?? string.Empty),
            Languages = DetectLanguages(extractedText ?? string.Empty)
        };
        result.KeyHighlights = ExtractHighlights(extractedText ?? string.Empty, result.DetectedSkills);
        result.Summary = GenerateExecutiveSummary(extractedText ?? string.Empty, result);
        result.Success = true;
        return result;
    }

    private static string ExtractTextFromPdf(string pdfPath)
    {
        var sb = new StringBuilder();
        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
            foreach (var page in document.Pages)
            {
                // Extract from page content streams
                var contents = page.Contents;
                if (contents == null) continue;

                foreach (var item in contents.Elements)
                {
                    if (item is PdfDictionary dict && dict.Stream != null)
                    {
                        var text = Encoding.UTF8.GetString(dict.Stream.Value);
                        sb.AppendLine(ExtractReadableStringsFromStream(text));
                    }
                }
            }
        }
        catch
        {
            // If PdfReader throws due to formatting, try reading binary stream strings
            var bytes = File.ReadAllBytes(pdfPath);
            var raw = Encoding.UTF8.GetString(bytes);
            sb.Append(ExtractReadableStringsFromStream(raw));
        }

        var res = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(res) ? "Резюме завантажено у форматі PDF (графічний або сканований документ)." : res;
    }

    private static string ExtractTextFromDocx(string docxPath)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null) return string.Empty;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var xml = reader.ReadToEnd();

            // Replace paragraph & break tags with spaces/newlines
            xml = Regex.Replace(xml, @"<(?:w:p|/w:p|w:br)[^>]*>", " ");
            // Strip remaining xml tags
            var text = Regex.Replace(xml, @"<[^>]+>", "");
            // Normalize spaces
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string ExtractReadableStringsFromStream(string streamContent)
    {
        var sb = new StringBuilder();
        // Match PDF text strings: (text) or [(text)]
        var matches = Regex.Matches(streamContent, @"\(([^()]+)\)");
        foreach (Match m in matches)
        {
            var val = m.Groups[1].Value.Trim();
            if (val.Length > 1 && !val.All(char.IsControl))
            {
                sb.Append(val).Append(' ');
            }
        }

        if (sb.Length > 0)
        {
            return sb.ToString();
        }

        // Match general ascii/cyrillic word sequences
        var wordMatches = Regex.Matches(streamContent, @"[A-Za-zА-Яа-яІіЇїЄє0-9\.\,\-\+\#]{2,}");
        foreach (Match m in wordMatches)
        {
            sb.Append(m.Value).Append(' ');
        }

        return sb.ToString();
    }

    private static List<string> DetectSkills(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in KnownTechSkills)
        {
            var pattern = skill switch
            {
                "C#" => @"(?i)(?:\bC#(?!\w)|csharp)",
                ".NET" => @"(?i)(?:\.NET(?!\w)|\bdotnet\b)",
                _ => $@"\b{Regex.Escape(skill)}\b"
            };

            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                found.Add(skill);
            }
        }

        return found.OrderBy(s => s).ToList();
    }

    private static int? DetectExperienceYears(string text)
    {
        var match = Regex.Match(text, @"(\d{1,2})\s*(?:років|роки|рік|years|year|yr|yrs)\b", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var years) && years is > 0 and <= 50)
        {
            return years;
        }

        // Try date range detection like 2019-2024
        var yearMatches = Regex.Matches(text, @"\b(20\d\d)\s*[-–—]\s*(20\d\d|present|по теперішній час|зараз)\b", RegexOptions.IgnoreCase);
        var totalYears = 0;
        foreach (Match ym in yearMatches)
        {
            if (int.TryParse(ym.Groups[1].Value, out var start))
            {
                var endYear = int.TryParse(ym.Groups[2].Value, out var end) ? end : DateTime.UtcNow.Year;
                if (endYear >= start)
                {
                    totalYears += (endYear - start);
                }
            }
        }

        return totalYears > 0 ? Math.Min(totalYears, 40) : null;
    }

    private static string? DetectPhone(string text)
    {
        var match = Regex.Match(text, @"(?:\+?380|0)\s*\(?\d{2}\)?[\s.-]?\d{3}[\s.-]?\d{2}[\s.-]?\d{2}");
        return match.Success ? match.Value.Trim() : null;
    }

    private static string? DetectEmail(string text)
    {
        var match = Regex.Match(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        return match.Success ? match.Value.Trim() : null;
    }

    private static List<string> DetectLanguages(string text)
    {
        var languages = new List<string>();
        if (Regex.IsMatch(text, @"\b(english|англійська)\b", RegexOptions.IgnoreCase))
        {
            var levelMatch = Regex.Match(text, @"(A1|A2|B1|B2|C1|C2|Intermediate|Upper-Intermediate|Advanced|Fluent)", RegexOptions.IgnoreCase);
            languages.Add(levelMatch.Success ? $"Англійська ({levelMatch.Value})" : "Англійська");
        }
        if (Regex.IsMatch(text, @"\b(ukrainian|українська)\b", RegexOptions.IgnoreCase))
        {
            languages.Add("Українська (Рідна)");
        }
        return languages;
    }

    private static List<string> ExtractHighlights(string text, List<string> detectedSkills)
    {
        var highlights = new List<string>();

        if (detectedSkills.Count > 0)
        {
            highlights.Add($"Ключовий технологічний стек: {string.Join(", ", detectedSkills.Take(7))}");
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var clean = line.Trim();
            if (clean.Length is > 25 and < 180 &&
                (clean.StartsWith("- ") || clean.StartsWith("• ") || clean.StartsWith("* ") ||
                 clean.Contains("розробк", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("досвід", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("керуван", StringComparison.OrdinalIgnoreCase) ||
                 clean.Contains("архітектур", StringComparison.OrdinalIgnoreCase)))
            {
                var formatted = clean.TrimStart('-', '•', '*', ' ');
                if (!highlights.Contains(formatted))
                {
                    highlights.Add(formatted);
                    if (highlights.Count >= 4) break;
                }
            }
        }

        return highlights;
    }

    private static string GenerateExecutiveSummary(string text, ParsedResumeResult result)
    {
        var sb = new StringBuilder();

        if (result.DetectedExperienceYears.HasValue)
        {
            sb.Append($"Спеціаліст із підтвердженим практичним досвідом понад {result.DetectedExperienceYears.Value} р. ");
        }

        if (result.DetectedSkills.Count > 0)
        {
            sb.Append($"Профільні навички включають: {string.Join(", ", result.DetectedSkills.Take(8))}. ");
        }

        if (result.Languages.Count > 0)
        {
            sb.Append($"Володіння мовами: {string.Join(", ", result.Languages)}. ");
        }

        if (sb.Length == 0)
        {
            // First 200 non-empty chars
            var clean = Regex.Replace(text, @"\s+", " ").Trim();
            return clean.Length > 200 ? clean[..200] + "..." : clean;
        }

        return sb.ToString().Trim();
    }

    public QuickImportParsedResult ParseRawResumeText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new QuickImportParsedResult(false, "", "", "", 0, "", "", "Вставте текст резюме або профілю.");
        }

        var text = rawText;
        var emailMatch = Regex.Match(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        var email = emailMatch.Success ? emailMatch.Value : "";

        var phoneMatch = Regex.Match(text, @"(?:\+?380|0)\d{9}|(?:\+?\d{1,3}[\s-]?)?\(?\d{3}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}");
        var phone = phoneMatch.Success ? phoneMatch.Value.Trim() : "";

        var expMatch = Regex.Match(text, @"(\d+)\s*(?:роки|років|року|years|year|р\.)", RegexOptions.IgnoreCase);
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
            var pattern = $@"(?<!\w){Regex.Escape(skill)}(?!\w)";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
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

        return new QuickImportParsedResult(
            true,
            fullName,
            email,
            phone,
            experienceYears,
            string.Join(", ", matchedSkills),
            text.Length > 500 ? text[..500] + "..." : text);
    }
}

public record QuickImportParsedResult(
    bool Success,
    string FullName,
    string Email,
    string Phone,
    int ExperienceYears,
    string Skills,
    string Summary,
    string? Message = null);
