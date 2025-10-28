using System;
using System.Collections.Generic;

namespace backend.Configuration;

public sealed class EffectiveEstimationPolicy
{
    public Dictionary<string, (double xs, double s, double m, double l, double xl)> BaseHoursByCategory { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["New UI"] = (4, 8, 16, 32, 56),
            ["New Interface"] = (6, 12, 24, 48, 80),
            ["New Backgrounder"] = (6, 12, 24, 48, 80),
            ["Adjust Existing UI"] = (2, 4, 8, 16, 28),
            ["Adjust Existing Logic"] = (2, 4, 8, 16, 28)
        };

    public double CrudCreateMultiplier { get; set; } = 1.0;
    public double CrudReadMultiplier { get; set; } = 0.7;
    public double CrudUpdateMultiplier { get; set; } = 0.9;
    public double CrudDeleteMultiplier { get; set; } = 0.6;

    public double PerFieldHours { get; set; } = 0.15;
    public double PerIntegrationHours { get; set; } = 6;
    public double FileUploadHours { get; set; } = 2;
    public double AuthRolesHours { get; set; } = 3;
    public double WorkflowStepHours { get; set; } = 1.5;

    public double ReferenceMedianCapMultiplier { get; set; } = 1.10;
    public double GlobalShrinkageToMedian { get; set; } = 0.9;
    public double HardMaxPerItemHours { get; set; } = 80;
    public double HardMinPerItemHours { get; set; } = 1;

    public bool CapAdjustCategoriesToMaxM { get; set; } = true;
    public double JustificationScoreThreshold { get; set; } = 0.7;

    public double RoundToNearestHours { get; set; } = 0.5;
}
