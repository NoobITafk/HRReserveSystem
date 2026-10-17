namespace HRReserveSystem.Models;

public class PipelineStage
{
    public int Id { get; set; }

    public int? VacancyId { get; set; }

    public Vacancy? Vacancy { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public int Order { get; set; }

    public string ColorBadge { get; set; } = "badge-soft-slate";

    public bool IsTerminal { get; set; }

    public bool IsDefault { get; set; }
}
