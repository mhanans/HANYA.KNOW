using System;
using backend.Configuration;

namespace backend.Services.Estimation;

public static class EstimationNormalizer
{
    public static double Clamp(double value, EffectiveEstimationPolicy policy)
    {
        return Math.Max(policy.HardMinPerItemHours, Math.Min(policy.HardMaxPerItemHours, value));
    }

    public static double Round(double value, EffectiveEstimationPolicy policy)
    {
        var step = Math.Max(0.1, policy.RoundToNearestHours);
        return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }

    public static double ApplyReferenceShrinkage(double raw, double? referenceMedian, EffectiveEstimationPolicy policy)
    {
        if (referenceMedian is null || referenceMedian <= 0)
        {
            return raw;
        }

        var capped = Math.Min(raw, referenceMedian.Value * policy.ReferenceMedianCapMultiplier);
        var shrinkTarget = referenceMedian.Value * policy.GlobalShrinkageToMedian;
        return Math.Min(capped, shrinkTarget);
    }
}
