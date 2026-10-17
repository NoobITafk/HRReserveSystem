using System;

namespace HRReserveSystem.Models;

public class CandidateCommunication
{
    public int Id { get; set; }

    public int CandidateId { get; set; }

    public Candidate? Candidate { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string RecipientEmail { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string Channel { get; set; } = "Email";

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public string Status { get; set; } = "Sent";
}
