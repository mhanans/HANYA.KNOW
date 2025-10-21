using System;

namespace backend.Models;

public class ProjectTemplate
{
    public int? Id { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public List<string> EstimationColumns { get; set; } = new();
    public List<TemplateSection> Sections { get; set; } = new();
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
    public Dictionary<string, double?> Estimates { get; set; } = new();
}

public class ProjectAssessmentSummary
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}
