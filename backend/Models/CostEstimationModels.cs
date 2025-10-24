using System;
using System.Collections.Generic;

namespace backend.Models;

public class CostEstimationConfiguration
{
    public Dictionary<string, decimal> RoleMonthlySalaries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, decimal> RoleDefaultHeadcount { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RateCardDefinition> RateCards { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DefaultRateCardKey { get; set; } = "default";
    public decimal DefaultWorstCaseBufferPercent { get; set; } = 30m;
    public decimal DefaultAnnualInterestRatePercent { get; set; } = 30m;
    public decimal DefaultClientPaymentDelayMonths { get; set; } = 1m;
    public decimal DefaultOverheadPercent { get; set; } = 30m;
    public decimal DefaultOperationalCostPercent { get; set; } = 10m;
    public decimal DefaultPphPercent { get; set; } = 1m;
    public decimal DefaultExternalCommissionPercent { get; set; } = 0m;
    public CommissionMode DefaultExternalCommissionMode { get; set; } = CommissionMode.Percentage;
    public decimal DefaultMultiplier { get; set; } = 1m;
    public decimal DefaultDiscountPercent { get; set; } = 0m;
    public List<CommissionBracket> SalesCommissionBrackets { get; set; } = new();
    public List<CommissionBracket> CostCommissionBrackets { get; set; } = new();
}

public class RateCardDefinition
{
    public Dictionary<string, decimal> RoleRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DisplayName { get; set; } = string.Empty;
}

public class CommissionBracket
{
    public decimal UpperBound { get; set; }
    public decimal RatePercent { get; set; }
}

public enum CommissionMode
{
    ManualAmount,
    Percentage
}

public class CostEstimationInputs
{
    public decimal WorstCaseBufferPercent { get; set; }
    public Dictionary<string, decimal> RoleHeadcounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal WarrantyAnalystResources { get; set; }
    public decimal WarrantyDeveloperResources { get; set; }
    public int WarrantyDurationMonths { get; set; }
    public decimal AnnualInterestRatePercent { get; set; }
    public decimal ClientPaymentDelayMonths { get; set; }
    public decimal OverheadPercent { get; set; }
    public CommissionMode ExternalCommissionMode { get; set; }
    public decimal ExternalCommissionPercentage { get; set; }
    public decimal ExternalCommissionAmount { get; set; }
    public decimal PphPercent { get; set; }
    public decimal OperationalCostPercent { get; set; }
    public decimal Multiplier { get; set; }
    public decimal DiscountPercent { get; set; }
    public string RateCardKey { get; set; } = string.Empty;
}

public class RoleCostRow
{
    public string Role { get; set; } = string.Empty;
    public decimal Resources { get; set; }
    public decimal MonthlySalary { get; set; }
    public decimal BestCaseMonths { get; set; }
    public decimal WorstCaseMonths { get; set; }
    public decimal TotalCost { get; set; }
}

public class WarrantyCostSummary
{
    public decimal AnalystResources { get; set; }
    public decimal DeveloperResources { get; set; }
    public int DurationMonths { get; set; }
    public decimal AnalystMonthlySalary { get; set; }
    public decimal DeveloperMonthlySalary { get; set; }
    public decimal TotalCost { get; set; }
}

public class CostComponentSummary
{
    public decimal FinancingCost { get; set; }
    public decimal OverheadCost { get; set; }
    public decimal ExternalCommission { get; set; }
    public decimal OperationalCost { get; set; }
    public decimal PphCost { get; set; }
    public decimal SalesCommission { get; set; }
    public decimal CostCommission { get; set; }
}

public class RevenueRow
{
    public string Role { get; set; } = string.Empty;
    public decimal ManDays { get; set; }
    public decimal RatePerDay { get; set; }
    public decimal MandaysPrice { get; set; }
}

public class RevenueSummary
{
    public List<RevenueRow> Rows { get; set; } = new();
    public decimal NilaiProject { get; set; }
    public decimal Multiplier { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal PriceAfterMultiplier { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PriceAfterDiscount { get; set; }
}

public class ProfitabilitySummary
{
    public decimal TotalCost { get; set; }
    public decimal ProfitAmount { get; set; }
    public decimal ProfitPercent { get; set; }
}

public class CostEstimationResult
{
    public int AssessmentId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public CostEstimationInputs Inputs { get; set; } = new();
    public List<RoleCostRow> RoleCosts { get; set; } = new();
    public decimal TotalSalaries { get; set; }
    public decimal ProjectDurationMonths { get; set; }
    public WarrantyCostSummary Warranty { get; set; } = new();
    public CostComponentSummary Components { get; set; } = new();
    public RevenueSummary Revenue { get; set; } = new();
    public ProfitabilitySummary Profitability { get; set; } = new();
}

public class CostEstimationRequest
{
    public CostEstimationInputs? Inputs { get; set; }
}

public class GoalSeekRequest
{
    public CostEstimationInputs? Inputs { get; set; }
    public string TargetField { get; set; } = string.Empty;
    public decimal TargetValue { get; set; }
    public string AdjustableField { get; set; } = string.Empty;
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
}

public class GoalSeekResponse
{
    public CostEstimationInputs Inputs { get; set; } = new();
    public CostEstimationResult Result { get; set; } = new();
    public int Iterations { get; set; }
    public bool Converged { get; set; }
}
