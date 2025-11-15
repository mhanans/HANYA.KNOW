using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using backend.Models;

namespace backend.Services;

public class DetailedTask
{
    public string ActivityGroup { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public double ManDays { get; set; }
}

public static class AssessmentTaskAggregator
{
    private static readonly Dictionary<string, string> DirectSectionPhaseMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Project Preparation"] = "Project Preparation",
        ["Project Closing (Project)"] = "Project Closing (Project)",
        ["Project Closing (Application)"] = "Project Closing (Application)",
        ["Post Go Live"] = "Post Go Live"
    };

    private static readonly Dictionary<string, string> LogicalPhaseMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Requirement & Documentation"] = "Analysis & Design",
        ["Requirement & Documentation (Creating SRS, FSD, and Disccussion)"] = "Analysis & Design",
        ["BE Development"] = "Development",
        ["FE Development"] = "Development",
        ["Architect Setup"] = "Architecture & Setup",
        ["Architect POC/ Research"] = "Architecture & Setup",
        ["SIT (Manual by QA)"] = "Testing & QA",
        ["SIT (with UFT)"] = "Testing & QA",
        ["UAT (With User)"] = "Testing & QA",
        ["Unit Test (Manual by QA)"] = "Testing & QA",
        ["UT (xUnit)"] = "Testing & QA",
        ["Code Review"] = "Testing & QA"
    };

    private static readonly object ColumnDiagnosticsLock = new();
    private static bool _hasLoggedColumnDiagnostics;

    public static Dictionary<string, double> AggregateEstimationColumnEffort(ProjectAssessment assessment)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (assessment?.Sections == null) return result;

        double totalHoursTracked = 0;

        foreach (var section in assessment.Sections)
        {
            if (section?.Items == null) continue;

            foreach (var item in section.Items)
            {
                if (item == null || !item.IsNeeded || item.Estimates == null) continue;

                foreach (var estimate in item.Estimates)
                {
                    var columnName = estimate.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(columnName)) continue;

                    if (TryExtractHours(estimate.Value, out double hours) && hours > 0)
                    {
                        totalHoursTracked += hours;
                        var manDays = hours / 8.0;
                        result[columnName] = result.TryGetValue(columnName, out var existing)
                            ? existing + manDays
                            : manDays;
                    }
                }
            }
        }

        Console.WriteLine($"[CRITICAL DEBUG] AGGREGATION FINISHED. Total Hours: {totalHoursTracked}. Total Man-Days: {result.Values.Sum()}.");
        return result;
    }

    private readonly record struct ItemEffortDetail(
        string SectionName,
        int SectionIndex,
        string ItemName,
        int ItemIndex,
        double ManDays);

    private static IEnumerable<ItemEffortDetail> EnumerateItemEffort(ProjectAssessment assessment)
    {
        if (assessment?.Sections == null)
        {
            yield break;
        }

        for (var sectionIndex = 0; sectionIndex < assessment.Sections.Count; sectionIndex++)
        {
            var section = assessment.Sections[sectionIndex];
            if (section == null)
            {
                continue;
            }

            var sectionName = section.SectionName?.Trim() ?? string.Empty;
            var items = section.Items ?? new List<AssessmentItem>();
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                if (item == null || !item.IsNeeded)
                {
                    continue;
                }

                var itemName = item.ItemName?.Trim() ?? string.Empty;
                double totalHours = 0;
                foreach (var estimate in item.Estimates ?? new Dictionary<string, double?>())
                {
                    if (!TryExtractHours(estimate.Value, out var hours) || hours <= 0)
                    {
                        continue;
                    }

                    totalHours += hours;
                }

                if (totalHours <= 0)
                {
                    continue;
                }

                var manDays = totalHours / 8d;
                if (manDays <= 0)
                {
                    continue;
                }

                yield return new ItemEffortDetail(sectionName, sectionIndex, itemName, itemIndex, manDays);
            }
        }
    }

    private static string BuildMappingKey(string sectionName, string itemName)
    {
        return $"{sectionName}\0{itemName}";
    }

    public static Dictionary<string, double> AggregateItemEffort(ProjectAssessment assessment)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var detail in EnumerateItemEffort(assessment))
        {
            if (string.IsNullOrWhiteSpace(detail.ItemName))
            {
                continue;
            }

            if (result.TryGetValue(detail.ItemName, out var existing))
            {
                result[detail.ItemName] = existing + detail.ManDays;
            }
            else
            {
                result[detail.ItemName] = detail.ManDays;
            }
        }

        return result;
    }

    public static Dictionary<string, double> CalculateActivityManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (assessment?.Sections == null) return result;

        foreach (var section in assessment.Sections)
        {
            var sectionName = section?.SectionName?.Trim() ?? string.Empty;
            foreach (var item in section?.Items ?? Enumerable.Empty<AssessmentItem>())
            {
                if (item == null || !item.IsNeeded || item.Estimates == null) continue;
                foreach (var estimate in item.Estimates)
                {
                    if (!TryExtractHours(estimate.Value, out var hours) || hours <= 0)
                    {
                        continue;
                    }

                    var manDays = hours / 8.0;
                    var columnName = estimate.Key?.Trim() ?? string.Empty;
                    var activityName = ResolveActivityName(sectionName, item.ItemName, columnName, configuration);

                    result[activityName] = result.TryGetValue(activityName, out var current) ? current + manDays : manDays;
                }
            }
        }
        return result;
    }

    public static List<DetailedTask> GetDetailedTasks(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var tasks = new List<DetailedTask>();
        if (assessment?.Sections == null)
        {
            return tasks;
        }

        var effectiveConfiguration = configuration ?? new PresalesConfiguration();

        var columnRoleLookup = (effectiveConfiguration.EstimationColumnRoles ?? new List<EstimationColumnRoleMapping>())
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.EstimationColumn) &&
                !string.IsNullOrWhiteSpace(mapping.RoleName))
            .ToLookup(
                mapping => mapping.EstimationColumn!.Trim(),
                mapping => mapping.RoleName!.Trim(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var section in assessment.Sections)
        {
            if (section?.Items == null)
            {
                continue;
            }

            var sectionName = section.SectionName?.Trim() ?? string.Empty;
            foreach (var item in section.Items)
            {
                if (item == null || !item.IsNeeded || item.Estimates == null)
                {
                    continue;
                }

                double totalHours = 0;
                var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var estimate in item.Estimates)
                {
                    var columnName = estimate.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        continue;
                    }

                    if (!TryExtractHours(estimate.Value, out var hours) || hours <= 0)
                    {
                        continue;
                    }

                    totalHours += hours;

                    foreach (var role in columnRoleLookup[columnName])
                    {
                        if (!string.IsNullOrWhiteSpace(role))
                        {
                            roles.Add(role);
                        }
                    }
                }

                if (totalHours <= 0)
                {
                    continue;
                }

                var firstColumn = item.Estimates.Keys
                    .Select(key => key?.Trim())
                    .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                    ?? string.Empty;

                var activityGroup = ResolveActivityName(
                    sectionName,
                    item.ItemName ?? string.Empty,
                    firstColumn,
                    effectiveConfiguration);

                tasks.Add(new DetailedTask
                {
                    ActivityGroup = activityGroup,
                    TaskName = item.ItemName?.Trim() ?? string.Empty,
                    Actor = roles.Count > 0
                        ? string.Join(", ", roles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase))
                        : "Unassigned",
                    ManDays = totalHours / 8.0
                });
            }
        }

        return tasks;
    }

    public static string ResolveActivityName(string sectionName, string itemName, string columnName, PresalesConfiguration configuration)
    {
        var mappings = configuration?.ItemActivities ?? new List<ItemActivityMapping>();
        var mapping = mappings.FirstOrDefault(m =>
            string.Equals(m.SectionName, sectionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.ItemName, itemName, StringComparison.OrdinalIgnoreCase));
        if (mapping != null && !string.IsNullOrWhiteSpace(mapping.ActivityName))
        {
            return mapping.ActivityName;
        }

        if (DirectSectionPhaseMappings.TryGetValue(sectionName, out var directPhase))
        {
            return directPhase;
        }

        if (!string.IsNullOrWhiteSpace(columnName) && LogicalPhaseMapping.TryGetValue(columnName, out var logicalPhase))
        {
            return logicalPhase;
        }

        if (!string.IsNullOrWhiteSpace(sectionName))
        {
            return sectionName;
        }

        return "Uncategorized";
    }

    private static readonly string[] JsonHoursPropertyCandidates =
    {
        "hours",
        "hrs",
        "manhours",
        "manhour",
        "manHours",
        "value",
        "amount",
        "totalHours"
    };

    internal static bool TryExtractHours(object? rawValue, out double hours)
    {
        hours = 0;
        if (rawValue == null)
        {
            return false;
        }

        switch (rawValue)
        {
            case double d when double.IsFinite(d):
                hours = d;
                return true;
            case float f when float.IsFinite(f):
                hours = f;
                return true;
            case decimal dec:
                hours = (double)dec;
                return true;
            case long l:
                hours = l;
                return true;
            case int i:
                hours = i;
                return true;
            case JsonElement element:
                return TryExtractHoursFromJson(element, out hours);
            case JsonDocument document:
                return TryExtractHoursFromJson(document.RootElement, out hours);
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                hours = parsed;
                return double.IsFinite(hours);
            default:
                try
                {
                    if (rawValue is IConvertible convertible)
                    {
                        hours = convertible.ToDouble(CultureInfo.InvariantCulture);
                        return double.IsFinite(hours);
                    }
                }
                catch
                {
                    // ignored
                }

                return false;
        }
    }

    private static bool TryExtractHoursFromJson(JsonElement element, out double hours)
    {
        hours = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out hours) && double.IsFinite(hours);
            case JsonValueKind.String:
                var text = element.GetString();
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out hours) && double.IsFinite(hours);
            case JsonValueKind.Object:
                foreach (var candidate in JsonHoursPropertyCandidates)
                {
                    if (element.TryGetProperty(candidate, out var property) &&
                        TryExtractHoursFromJson(property, out hours))
                    {
                        return true;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryExtractHoursFromJson(property.Value, out hours))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                double total = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (TryExtractHoursFromJson(item, out var value))
                    {
                        total += value;
                    }
                }

                if (total > 0)
                {
                    hours = total;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    public static Dictionary<string, double> CalculateRoleManDays(
        ProjectAssessment assessment,
        PresalesConfiguration configuration)
    {
        var columnEffort = AggregateEstimationColumnEffort(assessment);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var columnRoleLookup = (configuration?.EstimationColumnRoles ?? Enumerable.Empty<EstimationColumnRoleMapping>())
            .Select(mapping => new
            {
                Column = mapping.EstimationColumn?.Trim(),
                Role = mapping.RoleName?.Trim()
            })
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.Column) &&
                !string.IsNullOrWhiteSpace(mapping.Role))
            .GroupBy(mapping => mapping.Column!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => entry.Role!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var uniqueColumns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmappedColumns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (columnName, manDays) in columnEffort)
        {
            var normalizedColumn = columnName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedColumn))
            {
                continue;
            }

            uniqueColumns.Add(normalizedColumn);

            if (!columnRoleLookup.TryGetValue(normalizedColumn, out var roles) || roles.Count == 0)
            {
                unmappedColumns.Add(normalizedColumn);

                const string unspecified = "Unassigned";
                result[unspecified] = result.TryGetValue(unspecified, out var existing)
                    ? existing + manDays
                    : manDays;
                continue;
            }

            var share = manDays / roles.Count;
            if (share <= 0)
            {
                continue;
            }

            foreach (var role in roles)
            {
                result[role] = result.TryGetValue(role, out var roleManDays)
                    ? roleManDays + share
                    : share;
            }
        }

        LogColumnDiagnostics(uniqueColumns, unmappedColumns);

        return result;
    }

    public static Dictionary<string, (double ManDays, string Actor)> GetEffortByDetail(
        ProjectAssessment assessment,
        PresalesConfiguration config)
    {
        var itemEffort = new Dictionary<string, ItemEffortAccumulator>(StringComparer.OrdinalIgnoreCase);
        if (assessment?.Sections == null)
        {
            return new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);
        }

        var columnRoleLookup = (config?.EstimationColumnRoles ?? Enumerable.Empty<EstimationColumnRoleMapping>())
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping?.EstimationColumn) &&
                !string.IsNullOrWhiteSpace(mapping?.RoleName))
            .ToLookup(
                mapping => mapping!.EstimationColumn!.Trim(),
                mapping => mapping!.RoleName!.Trim(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var section in assessment.Sections)
        {
            if (section?.Items == null)
            {
                continue;
            }

            foreach (var item in section.Items)
            {
                if (item == null || !item.IsNeeded || item.Estimates == null)
                {
                    continue;
                }

                var detailName = item.ItemName?.Trim();
                if (string.IsNullOrWhiteSpace(detailName))
                {
                    continue;
                }

                foreach (var estimate in item.Estimates)
                {
                    if (!TryExtractHours(estimate.Value, out var hours) || hours <= 0)
                    {
                        continue;
                    }

                    if (!itemEffort.TryGetValue(detailName, out var accumulator))
                    {
                        accumulator = new ItemEffortAccumulator();
                        itemEffort[detailName] = accumulator;
                    }

                    accumulator.TotalHours += hours;
                    var columnName = estimate.Key?.Trim() ?? string.Empty;
                    foreach (var role in columnRoleLookup[columnName])
                    {
                        if (!string.IsNullOrWhiteSpace(role))
                        {
                            accumulator.Roles.Add(role);
                        }
                    }
                }
            }
        }

        return itemEffort.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var orderedRoles = kvp.Value.Roles
                    .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var actor = orderedRoles.Count > 0
                    ? string.Join(", ", orderedRoles)
                    : "Unassigned";
                return (kvp.Value.TotalHours / 8.0, actor);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static void LogColumnDiagnostics(
        IReadOnlyCollection<string> uniqueColumns,
        IReadOnlyCollection<string> unmappedColumns)
    {
        if (uniqueColumns.Count == 0)
        {
            return;
        }

        lock (ColumnDiagnosticsLock)
        {
            if (_hasLoggedColumnDiagnostics)
            {
                return;
            }

            var sortedColumns = uniqueColumns
                .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Console.WriteLine(
                "[AssessmentTaskAggregator] Unique estimation columns detected: " +
                string.Join(", ", sortedColumns));

            if (unmappedColumns.Count > 0)
            {
                var sortedUnmapped = unmappedColumns
                    .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Console.WriteLine(
                    "[AssessmentTaskAggregator] Estimation columns without role mappings: " +
                    string.Join(", ", sortedUnmapped));
            }

            _hasLoggedColumnDiagnostics = true;
        }
    }
    public class GanttTask
    {
        public string ActivityGroup { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public double ManDays { get; set; }
    }

    public static List<GanttTask> GetGanttTasks(ProjectAssessment assessment, PresalesConfiguration configuration)
    {
        var tasks = new List<GanttTask>();
        if (assessment?.Sections == null)
        {
            return tasks;
        }

        var columnRoleLookup = (configuration?.EstimationColumnRoles ?? Enumerable.Empty<EstimationColumnRoleMapping>())
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping?.EstimationColumn) &&
                !string.IsNullOrWhiteSpace(mapping?.RoleName))
            .ToLookup(
                mapping => mapping!.EstimationColumn!.Trim(),
                mapping => mapping!.RoleName!.Trim(),
                StringComparer.OrdinalIgnoreCase);

        var itemEffort = new Dictionary<string, ItemEffortAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in assessment.Sections)
        {
            if (section?.Items == null)
            {
                continue;
            }

            foreach (var item in section.Items)
            {
                if (item == null || !item.IsNeeded || item.Estimates == null)
                {
                    continue;
                }

                var itemName = item.ItemName?.Trim();
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                foreach (var estimate in item.Estimates)
                {
                    if (!TryExtractHours(estimate.Value, out var hours) || hours <= 0)
                    {
                        continue;
                    }

                    if (!itemEffort.TryGetValue(itemName, out var accumulator))
                    {
                        accumulator = new ItemEffortAccumulator();
                        itemEffort[itemName] = accumulator;
                    }

                    accumulator.TotalHours += hours;

                    var columnName = estimate.Key?.Trim() ?? string.Empty;
                    foreach (var role in columnRoleLookup[columnName])
                    {
                        if (!string.IsNullOrWhiteSpace(role))
                        {
                            accumulator.Roles.Add(role);
                        }
                    }
                }
            }
        }

        var itemActivityMapping = (configuration?.ItemActivities ?? Enumerable.Empty<ItemActivityMapping>())
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping?.ItemName) &&
                !string.IsNullOrWhiteSpace(mapping?.ActivityName))
            .ToDictionary(
                mapping => mapping!.ItemName!.Trim(),
                mapping => mapping!.ActivityName!.Trim(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var (itemName, accumulator) in itemEffort)
        {
            var hasActivity = itemActivityMapping.TryGetValue(itemName, out var activityName);
            var orderedRoles = accumulator.Roles
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToList();

            tasks.Add(new GanttTask
            {
                ActivityGroup = hasActivity && !string.IsNullOrWhiteSpace(activityName)
                    ? activityName
                    : "Uncategorized",
                Detail = itemName,
                Actor = orderedRoles.Count > 0 ? string.Join(", ", orderedRoles) : "Unassigned",
                ManDays = accumulator.TotalHours / 8.0
            });
        }

        return tasks;
    }

    private sealed class ItemEffortAccumulator
    {
        public double TotalHours { get; set; }
        public HashSet<string> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public static class GanttTaskAggregator
{
    public class GanttTask
    {
        public string ActivityGroup { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public double ManDays { get; set; }
    }

    public static List<GanttTask> GetGanttTasks(ProjectAssessment assessment, PresalesConfiguration configuration)
    {
        var tasks = new List<GanttTask>();
        if (assessment?.Sections == null)
        {
            return tasks;
        }
        var columnRoleLookup = (configuration?.EstimationColumnRoles ?? new List<EstimationColumnRoleMapping>())
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping?.EstimationColumn) &&
                !string.IsNullOrWhiteSpace(mapping?.RoleName))
            .ToLookup(
                mapping => mapping!.EstimationColumn!.Trim(),
                mapping => mapping!.RoleName!.Trim(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var section in assessment.Sections ?? Enumerable.Empty<AssessmentSection>())
        {
            foreach (var item in section?.Items ?? Enumerable.Empty<AssessmentItem>())
            {
                if (item == null || !item.IsNeeded || item.Estimates == null) continue;

                var activityGroup = AssessmentTaskAggregator.ResolveActivityName(
                    section?.SectionName ?? string.Empty,
                    item.ItemName ?? string.Empty,
                    string.Empty,
                    configuration);

                foreach (var estimate in item.Estimates)
                {
                    if (!AssessmentTaskAggregator.TryExtractHours(estimate.Value, out var hours) || hours <= 0)
                    {
                        continue;
                    }

                    var detailName = estimate.Key?.Trim() ?? string.Empty;
                    var roles = columnRoleLookup[detailName]
                        .Where(role => !string.IsNullOrWhiteSpace(role))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    tasks.Add(new GanttTask
                    {
                        ActivityGroup = string.IsNullOrWhiteSpace(activityGroup) ? "Uncategorized" : activityGroup,
                        Detail = string.IsNullOrWhiteSpace(detailName) ? item.ItemName ?? "Task" : detailName,
                        Actor = roles.Count > 0 ? string.Join(", ", roles) : "Unassigned",
                        ManDays = hours / 8.0
                    });
                }
            }
        }

        return tasks;
    }
}
