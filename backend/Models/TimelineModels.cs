using System;
using System.Collections.Generic;

namespace backend.Models;

public class TimelineAssessmentSummary
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastModifiedAt { get; set; }
    public bool HasTimeline { get; set; }
    public DateTime? TimelineGeneratedAt { get; set; }
    public bool HasTimelineEstimation { get; set; }
    public DateTime? TimelineEstimationGeneratedAt { get; set; }
    public string? TimelineEstimationScale { get; set; }
}

public class TimelineGenerationRequest
{
    public int AssessmentId { get; set; }
}

public class TimelineEstimationRequest
{
    public int AssessmentId { get; set; }
}

public class TimelineEstimationRecord
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string ProjectScale { get; set; } = string.Empty;
    public int TotalDurationDays { get; set; }
    public List<TimelinePhaseEstimate> Phases { get; set; } = new();
    public List<TimelineRoleEstimate> Roles { get; set; } = new();
    public string SequencingNotes { get; set; } = string.Empty;
}

public class TimelinePhaseEstimate
{
    public string PhaseName { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string SequenceType { get; set; } = "Serial";
}

public class TimelineRoleEstimate
{
    public string Role { get; set; } = string.Empty;
    public double EstimatedHeadcount { get; set; }
    public double TotalManDays { get; set; }
}

public class PresalesRole
{
    public string RoleName { get; set; } = string.Empty;
    public string ExpectedLevel { get; set; } = string.Empty;
    public decimal CostPerDay { get; set; }
}

public class PresalesActivity
{
    public string ActivityName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } = 1;
}

public class ItemActivityMapping
{
    public string ItemName { get; set; } = string.Empty;
    public string ActivityName { get; set; } = string.Empty;
}

public class EstimationColumnRoleMapping
{
    public string EstimationColumn { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}

public class PresalesConfiguration
{
    public List<PresalesRole> Roles { get; set; } = new();
    public List<PresalesActivity> Activities { get; set; } = new();
    public List<ItemActivityMapping> ItemActivities { get; set; } = new();
    public List<EstimationColumnRoleMapping> EstimationColumnRoles { get; set; } = new();
}

public class TimelineDetail
{
    public string TaskName { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public double ManDays { get; set; }
    public int StartDay { get; set; }
    public int DurationDays { get; set; }
}

public class TimelineActivity
{
    public string ActivityName { get; set; } = string.Empty;
    public List<TimelineDetail> Details { get; set; } = new();
}

public class TimelineResourceAllocationEntry
{
    public string Role { get; set; } = string.Empty;
    public double TotalManDays { get; set; }
    public List<double> DailyEffort { get; set; } = new();
}

public class TimelineRecord
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int TotalDurationDays { get; set; }
    public List<TimelineActivity> Activities { get; set; } = new();
    public List<TimelineResourceAllocationEntry> ResourceAllocation { get; set; } = new();
}

public class TimelineGenerationAttempt
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string RawResponse { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool Success { get; set; }
}
