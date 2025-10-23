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
}

public class TimelineGenerationRequest
{
    public int AssessmentId { get; set; }
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

public class TaskActivityMapping
{
    public string TaskKey { get; set; } = string.Empty;
    public string ActivityName { get; set; } = string.Empty;
}

public class TaskRoleMapping
{
    public string TaskKey { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public double AllocationPercentage { get; set; }
}

public class PresalesConfiguration
{
    public List<PresalesRole> Roles { get; set; } = new();
    public List<PresalesActivity> Activities { get; set; } = new();
    public List<TaskActivityMapping> TaskActivities { get; set; } = new();
    public List<TaskRoleMapping> TaskRoles { get; set; } = new();
}

public class TimelineDetailRecord
{
    public string TaskKey { get; set; } = string.Empty;
    public string DetailName { get; set; } = string.Empty;
    public double ManDays { get; set; }
    public int StartDayIndex { get; set; }
    public int DurationDays { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class TimelineActivityRecord
{
    public string ActivityName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public List<TimelineDetailRecord> Details { get; set; } = new();
}

public class TimelineManpowerSummary
{
    public string RoleName { get; set; } = string.Empty;
    public string ExpectedLevel { get; set; } = string.Empty;
    public double ManDays { get; set; }
    public decimal CostPerDay { get; set; }
    public decimal TotalCost { get; set; }
}

public class TimelineResourceAllocation
{
    public string RoleName { get; set; } = string.Empty;
    public string ExpectedLevel { get; set; } = string.Empty;
    public List<double> DailyAllocation { get; set; } = new();
}

public class TimelineRecord
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public List<DateTime> WorkingDays { get; set; } = new();
    public List<TimelineActivityRecord> Activities { get; set; } = new();
    public List<TimelineManpowerSummary> ManpowerSummary { get; set; } = new();
    public List<TimelineResourceAllocation> ResourceAllocations { get; set; } = new();
    public decimal TotalCost { get; set; }
}
