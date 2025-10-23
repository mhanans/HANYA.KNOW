using System;
using System.Text.Json.Serialization;
using backend.Models.Serialization;

namespace backend.Models;

public class ProjectTemplate
{
    public int? Id { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public List<string> EstimationColumns { get; set; } = new();
    public List<TemplateSection> Sections { get; set; } = new();
}

public class AssessmentJob
{
    public int Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Step { get; set; } = 1;
    public string ScopeDocumentPath { get; set; } = string.Empty;
    public string ScopeDocumentMimeType { get; set; } = string.Empty;
    public string OriginalTemplateJson { get; set; } = string.Empty;
    public string? ReferenceAssessmentsJson { get; set; }
    public string? RawGenerationResponse { get; set; }
    public string? GeneratedItemsJson { get; set; }
    public string? RawEstimationResponse { get; set; }
    public string? FinalAnalysisJson { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    public void SyncStepWithStatus()
    {
        Step = GetStepForStatus(Status);
    }

    public static int GetStepForStatus(JobStatus status)
    {
        return status switch
        {
            JobStatus.Pending => 2,
            JobStatus.GenerationInProgress => 3,
            JobStatus.GenerationComplete => 4,
            JobStatus.FailedGeneration => 5,
            JobStatus.EstimationInProgress => 6,
            JobStatus.EstimationComplete => 7,
            JobStatus.FailedEstimation => 8,
            JobStatus.Complete => 9,
            _ => 1
        };
    }
}

public class AssessmentJobSummary
{
    public int Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Step { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
}

[JsonConverter(typeof(JobStatusJsonConverter))]
public enum JobStatus
{
    Pending,
    GenerationInProgress,
    GenerationComplete,
    EstimationInProgress,
    EstimationComplete,
    Complete,
    FailedGeneration,
    FailedEstimation
}

public class TemplateSection
{
    public string SectionName { get; set; } = string.Empty;
    public string Type { get; set; } = "Project-Level";
    public List<TemplateItem> Items { get; set; } = new();
}

public class TemplateItem
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemDetail { get; set; } = string.Empty;
    public string Category { get; set; } = "New UI";
}

public class ProjectTemplateMetadata
{
    public int Id { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class ProjectAssessment
{
    public int? Id { get; set; }
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public int Step { get; set; } = 1;
    public List<AssessmentSection> Sections { get; set; } = new();
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}

public class AssessmentSection
{
    public string SectionName { get; set; } = string.Empty;
    public List<AssessmentItem> Items { get; set; } = new();
}

public class AssessmentItem
{
    public string ItemId { get; set; } = string.Empty;
    public bool IsNeeded { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string ItemDetail { get; set; } = string.Empty;
    public string Category { get; set; } = "New UI";
    public Dictionary<string, double?> Estimates { get; set; } = new();
}

public class ProjectAssessmentSummary
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public int Step { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}

public class SimilarAssessmentReference
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public double TotalHours { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}
