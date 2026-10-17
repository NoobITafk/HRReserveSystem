using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface ICandidateExportService
{
    byte[] BuildCandidatesCsv(IEnumerable<Candidate> candidates);
}
