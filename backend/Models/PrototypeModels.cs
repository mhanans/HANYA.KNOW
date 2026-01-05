using System;

namespace backend.Models;

public class PrototypeRecord
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string Status { get; set; } = "Completed";
}
