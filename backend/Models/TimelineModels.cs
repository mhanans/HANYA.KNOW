using System;
using System.Collections.Generic;
using System.Linq;

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
    public TimelineEstimatorRawInput? RawInputData { get; set; }
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
    public decimal MonthlySalary { get; set; }
    public decimal RatePerDay { get; set; }
}

public class PresalesActivity
{
    public string ActivityName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } = 1;
}

public class ItemActivityMapping
{
    public string SectionName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ActivityName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public class EstimationColumnRoleMapping
{
    public string EstimationColumn { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}

public class TeamType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MinManDays { get; set; }
    public int MaxManDays { get; set; }
    public List<TeamTypeRole> Roles { get; set; } = new();
}

public class TeamTypeRole
{
    public int Id { get; set; }
    public int TeamTypeId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public double Headcount { get; set; }
}

public class PresalesConfiguration
{
    public List<PresalesRole> Roles { get; set; } = new();
    public List<PresalesActivity> Activities { get; set; } = new();
    public List<ItemActivityMapping> ItemActivities { get; set; } = new();
    public List<EstimationColumnRoleMapping> EstimationColumnRoles { get; set; } = new();
    public List<TeamType> TeamTypes { get; set; } = new();
}

public class TimelineEstimatorRawInput
{
    public Dictionary<string, double> ActivityManDays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> RoleManDays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double TotalRoleManDays { get; set; }
    public Dictionary<string, int> DurationsPerRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TeamType? SelectedTeamType { get; set; }
    public int DurationAnchor { get; set; }
}

public class TimelineEstimationDetails
{
    public TimelineEstimationRecord EstimationResult { get; set; } = new();
    public TimelineEstimatorRawInput? RawInput { get; set; }
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
    public int Version { get; set; }
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

public static class PresalesRoleFormatter
{
    private static readonly string[] DisplaySeparators = { " – ", " — ", " - " };

    public static string BuildLabel(string? roleName, string? expectedLevel)
    {
        var name = (roleName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var level = (expectedLevel ?? string.Empty).Trim();
        return string.IsNullOrEmpty(level) ? name : $"{name} – {level}";
    }

    public static IEnumerable<string> EnumerateLookupKeys(string? roleName, string? expectedLevel)
    {
        var name = (roleName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            yield break;
        }

        var level = (expectedLevel ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(level))
        {
            yield return $"{name} – {level}";
            yield return $"{name} {level}";
            yield return $"{name}::{level}";
        }

        yield return name;
    }

    public static IEnumerable<string> EnumerateLookupKeysFromLabel(string? label)
    {
        var value = (label ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value))
        {
            yield break;
        }

        yield return value;

        var separatorInfo = DisplaySeparators
            .Select(separator => new { Separator = separator, Index = value.IndexOf(separator, StringComparison.Ordinal) })
            .Where(entry => entry.Index > 0)
            .OrderBy(entry => entry.Index)
            .FirstOrDefault();

        if (separatorInfo == null)
        {
            yield break;
        }

        var baseName = value[..separatorInfo.Index].Trim();
        var level = value[(separatorInfo.Index + separatorInfo.Separator.Length)..].Trim();

        if (!string.IsNullOrEmpty(baseName) && !string.IsNullOrEmpty(level))
        {
            foreach (var key in EnumerateLookupKeys(baseName, level))
            {
                yield return key;
            }
        }
        else if (!string.IsNullOrEmpty(baseName))
        {
            yield return baseName;
        }
    }

    public static string ExtractBaseRole(string? label)
    {
        var value = (label ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        foreach (var separator in DisplaySeparators)
        {
            var index = value.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
            {
                return value[..index].Trim();
            }
        }

        return value;
    }
}
