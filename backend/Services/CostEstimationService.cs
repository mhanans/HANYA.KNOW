using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using backend.Models;
using ClosedXML.Excel;

namespace backend.Services;

public class CostEstimationService
{
    private readonly TimelineStore _timelineStore;
    private readonly CostEstimationConfigurationStore _configurationStore;

    public CostEstimationService(TimelineStore timelineStore, CostEstimationConfigurationStore configurationStore)
    {
        _timelineStore = timelineStore;
        _configurationStore = configurationStore;
    }

    public async Task<CostEstimationResult?> GetAsync(int assessmentId, CostEstimationInputs? overrideInputs, CancellationToken cancellationToken)
    {
        var timeline = await _timelineStore.GetAsync(assessmentId, cancellationToken).ConfigureAwait(false);
        if (timeline == null)
        {
            return null;
        }

        var config = await _configurationStore.GetAsync(cancellationToken).ConfigureAwait(false);
        var inputs = MergeInputs(config, overrideInputs);
        return Calculate(timeline, config, inputs);
    }

    public async Task<GoalSeekResponse?> GoalSeekAsync(int assessmentId, GoalSeekRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return null;
        }

        var timeline = await _timelineStore.GetAsync(assessmentId, cancellationToken).ConfigureAwait(false);
        if (timeline == null)
        {
            return null;
        }

        var config = await _configurationStore.GetAsync(cancellationToken).ConfigureAwait(false);
        var inputs = MergeInputs(config, request.Inputs);

        var adjustable = ResolveAdjustableField(request.AdjustableField);
        var target = ResolveTargetField(request.TargetField);
        if (adjustable == null || target == null)
        {
            return null;
        }

        var min = request.MinValue ?? adjustable.MinValue;
        var max = request.MaxValue ?? adjustable.MaxValue;
        var iterations = 0;
        var converged = false;

        var targetValue = request.TargetValue;
        decimal current = adjustable.Getter(inputs);
        var currentResult = Calculate(timeline, config, inputs);
        var currentValue = target(currentResult);

        if (Math.Abs(currentValue - targetValue) < adjustable.Tolerance)
        {
            return new GoalSeekResponse { Inputs = inputs, Result = currentResult, Converged = true, Iterations = 0 };
        }

        var lowerValue = min;
        var upperValue = max;
        const int maxIterations = 30;

        adjustable.Setter(inputs, lowerValue);
        var lowerResult = Calculate(timeline, config, inputs);
        var lowerEval = target(lowerResult) - targetValue;

        adjustable.Setter(inputs, upperValue);
        var upperResult = Calculate(timeline, config, inputs);
        var upperEval = target(upperResult) - targetValue;

        if (lowerEval == 0)
        {
            converged = true;
            currentResult = lowerResult;
            current = lowerValue;
            iterations = 1;
        }
        else if (upperEval == 0)
        {
            converged = true;
            currentResult = upperResult;
            current = upperValue;
            iterations = 1;
        }
        else if (Math.Sign(lowerEval) == Math.Sign(upperEval))
        {
            // fallback to incremental search
            current = adjustable.Getter(inputs);
            currentResult = Calculate(timeline, config, inputs);
            iterations = 1;
        }
        else
        {
            for (iterations = 0; iterations < maxIterations; iterations++)
            {
                var mid = (lowerValue + upperValue) / 2m;
                adjustable.Setter(inputs, mid);
                var midResult = Calculate(timeline, config, inputs);
                var diff = target(midResult) - targetValue;
                if (Math.Abs(diff) <= adjustable.Tolerance)
                {
                    converged = true;
                    current = mid;
                    currentResult = midResult;
                    break;
                }

                if (Math.Sign(diff) == Math.Sign(lowerEval))
                {
                    lowerValue = mid;
                    lowerEval = diff;
                }
                else
                {
                    upperValue = mid;
                    upperEval = diff;
                }

                current = mid;
                currentResult = midResult;
            }
        }

        adjustable.Setter(inputs, current);
        return new GoalSeekResponse
        {
            Inputs = inputs,
            Result = currentResult,
            Iterations = iterations,
            Converged = converged
        };
    }

    public async Task<byte[]> ExportAsync(int assessmentId, CostEstimationInputs? overrideInputs, CancellationToken cancellationToken)
    {
        var timeline = await _timelineStore.GetAsync(assessmentId, cancellationToken).ConfigureAwait(false);
        if (timeline == null)
        {
            throw new KeyNotFoundException("Timeline not found for assessment");
        }

        var config = await _configurationStore.GetAsync(cancellationToken).ConfigureAwait(false);
        var inputs = MergeInputs(config, overrideInputs);
        var result = Calculate(timeline, config, inputs);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Estimation");

        sheet.Cell("A1").Value = "Project";
        sheet.Cell("B1").Value = result.ProjectName;
        sheet.Cell("A2").Value = "Template";
        sheet.Cell("B2").Value = result.TemplateName;

        sheet.Cell("A4").Value = "Role";
        sheet.Cell("B4").Value = "Resources";
        sheet.Cell("C4").Value = "Salary (IDR)";
        sheet.Cell("D4").Value = "Best Case (mo)";
        sheet.Cell("E4").Value = "Worst Case (mo)";
        sheet.Cell("F4").Value = "Total";

        var row = 5;
        foreach (var rc in result.RoleCosts)
        {
            sheet.Cell(row, 1).Value = rc.Role;
            sheet.Cell(row, 2).Value = rc.Resources;
            sheet.Cell(row, 3).Value = rc.MonthlySalary;
            sheet.Cell(row, 4).Value = rc.BestCaseMonths;
            sheet.Cell(row, 5).FormulaA1 = $"=D{row}*(1+{result.Inputs.WorstCaseBufferPercent}/100)";
            sheet.Cell(row, 6).FormulaA1 = $"=B{row}*C{row}*E{row}";
            row++;
        }

        sheet.Cell(row, 5).Value = "Total Salaries";
        sheet.Cell(row, 6).FormulaA1 = $"=SUM(F5:F{row - 1})";
        row += 2;

        var warrantyStart = row;
        sheet.Cell(row, 1).Value = "Warranty";
        sheet.Cell(row + 1, 1).Value = "Analyst Resources";
        sheet.Cell(row + 1, 2).Value = inputs.WarrantyAnalystResources;
        sheet.Cell(row + 2, 1).Value = "Developer Resources";
        sheet.Cell(row + 2, 2).Value = inputs.WarrantyDeveloperResources;
        sheet.Cell(row + 3, 1).Value = "Duration (months)";
        sheet.Cell(row + 3, 2).Value = inputs.WarrantyDurationMonths;
        sheet.Cell(row + 4, 1).Value = "Total Warranty";
        var analystSalaryValue = result.Warranty.AnalystMonthlySalary.ToString(CultureInfo.InvariantCulture);
        var developerSalaryValue = result.Warranty.DeveloperMonthlySalary.ToString(CultureInfo.InvariantCulture);
        sheet.Cell(row + 4, 2).FormulaA1 = $"=B{row + 1}*B{row + 3}*{analystSalaryValue}+B{row + 2}*B{row + 3}*{developerSalaryValue}";

        row += 6;

        sheet.Cell(row, 1).Value = "Revenue";
        sheet.Cell(row + 1, 1).Value = "Role";
        sheet.Cell(row + 1, 2).Value = "Mandays";
        sheet.Cell(row + 1, 3).Value = "Rate";
        sheet.Cell(row + 1, 4).Value = "Mandays Price";
        var revenueRow = row + 2;
        foreach (var rev in result.Revenue.Rows)
        {
            sheet.Cell(revenueRow, 1).Value = rev.Role;
            sheet.Cell(revenueRow, 2).Value = rev.ManDays;
            sheet.Cell(revenueRow, 3).Value = rev.RatePerDay;
            sheet.Cell(revenueRow, 4).FormulaA1 = $"=B{revenueRow}*C{revenueRow}";
            revenueRow++;
        }

        sheet.Cell(revenueRow, 3).Value = "Nilai Project";
        sheet.Cell(revenueRow, 4).FormulaA1 = $"=SUM(D{row + 2}:D{revenueRow - 1})";
        sheet.Cell(revenueRow + 1, 3).Value = "Multiplier";
        sheet.Cell(revenueRow + 1, 4).Value = inputs.Multiplier;
        sheet.Cell(revenueRow + 2, 3).Value = "Price after Multiplier";
        sheet.Cell(revenueRow + 2, 4).FormulaA1 = $"=D{revenueRow}*D{revenueRow + 1}";
        sheet.Cell(revenueRow + 3, 3).Value = "Discount %";
        sheet.Cell(revenueRow + 3, 4).Value = inputs.DiscountPercent / 100m;
        sheet.Cell(revenueRow + 4, 3).Value = "Discount Amount";
        sheet.Cell(revenueRow + 4, 4).FormulaA1 = $"=D{revenueRow + 2}*D{revenueRow + 3}";
        sheet.Cell(revenueRow + 5, 3).Value = "Price after Discount";
        sheet.Cell(revenueRow + 5, 4).FormulaA1 = $"=D{revenueRow + 2}-D{revenueRow + 4}";

        sheet.Columns().AdjustToContents(8d, 80d);
        sheet.Range("A4:F4").Style.Font.Bold = true;
        sheet.Range(warrantyStart, 1, warrantyStart, 2).Style.Font.Bold = true;

        using var stream = new System.IO.MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public CostEstimationResult Calculate(TimelineRecord timeline, CostEstimationConfiguration configuration, CostEstimationInputs inputs)
    {
        var roleCosts = new List<RoleCostRow>();
        decimal totalSalary = 0m;
        decimal maxDuration = 0m;

        foreach (var allocation in timeline.ResourceAllocation)
        {
            var monthlySalary = configuration.RoleMonthlySalaries.TryGetValue(allocation.Role, out var salary)
                ? salary
                : 0m;

            var resources = ResolveHeadcount(allocation.Role, inputs, configuration);
            var bestCaseMonths = (decimal)allocation.TotalManDays / 20m;
            var worstCaseMonths = bestCaseMonths * (1m + inputs.WorstCaseBufferPercent / 100m);
            var total = resources * monthlySalary * worstCaseMonths;
            totalSalary += total;
            maxDuration = Math.Max(maxDuration, worstCaseMonths);

            if (!string.IsNullOrWhiteSpace(allocation.Role))
            {
                inputs.RoleHeadcounts[allocation.Role] = resources;
            }

            roleCosts.Add(new RoleCostRow
            {
                Role = allocation.Role,
                Resources = Decimal.Round(resources, 2),
                MonthlySalary = monthlySalary,
                BestCaseMonths = Decimal.Round(bestCaseMonths, 4),
                WorstCaseMonths = Decimal.Round(worstCaseMonths, 4),
                TotalCost = Decimal.Round(total, 2)
            });
        }

        var warranty = CalculateWarranty(configuration, inputs);

        var revenue = CalculateRevenue(timeline, configuration, inputs);

        var operationalCost = inputs.OperationalCostPercent / 100m * revenue.PriceAfterDiscount;

        var amountToFinance = totalSalary + warranty.TotalCost + operationalCost;
        var financingDurationYears = (maxDuration + inputs.ClientPaymentDelayMonths) / 12m;
        var financingCost = inputs.AnnualInterestRatePercent / 100m * financingDurationYears * amountToFinance;

        var overheadCost = inputs.OverheadPercent / 100m * (totalSalary + warranty.TotalCost + financingCost);

        var externalCommission = inputs.ExternalCommissionMode == CommissionMode.ManualAmount
            ? inputs.ExternalCommissionAmount
            : inputs.ExternalCommissionPercentage / 100m * revenue.PriceAfterDiscount;

        var pphCost = inputs.PphPercent / 100m * revenue.PriceAfterDiscount;

        var salesCommission = ApplyCommission(configuration.SalesCommissionBrackets, revenue.PriceAfterDiscount);

        var baseCost = totalSalary + warranty.TotalCost + financingCost + overheadCost + externalCommission + operationalCost + pphCost + salesCommission;
        var costCommission = ApplyCommission(configuration.CostCommissionBrackets, baseCost);
        var totalCost = baseCost + costCommission;
        var profitAmount = revenue.PriceAfterDiscount - totalCost;
        var profitPercent = revenue.PriceAfterDiscount == 0 ? 0 : profitAmount / revenue.PriceAfterDiscount * 100m;

        return new CostEstimationResult
        {
            AssessmentId = timeline.AssessmentId,
            ProjectName = timeline.ProjectName,
            TemplateName = timeline.TemplateName,
            Inputs = inputs,
            RoleCosts = roleCosts,
            TotalSalaries = Decimal.Round(totalSalary, 2),
            ProjectDurationMonths = Decimal.Round(maxDuration, 2),
            Warranty = warranty,
            Components = new CostComponentSummary
            {
                FinancingCost = Decimal.Round(financingCost, 2),
                OverheadCost = Decimal.Round(overheadCost, 2),
                ExternalCommission = Decimal.Round(externalCommission, 2),
                OperationalCost = Decimal.Round(operationalCost, 2),
                PphCost = Decimal.Round(pphCost, 2),
                SalesCommission = Decimal.Round(salesCommission, 2),
                CostCommission = Decimal.Round(costCommission, 2)
            },
            Revenue = revenue,
            Profitability = new ProfitabilitySummary
            {
                TotalCost = Decimal.Round(totalCost, 2),
                ProfitAmount = Decimal.Round(profitAmount, 2),
                ProfitPercent = Decimal.Round(profitPercent, 2)
            }
        };
    }

    private static decimal ResolveHeadcount(string role, CostEstimationInputs inputs, CostEstimationConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(role) && inputs.RoleHeadcounts.TryGetValue(role, out var custom) && custom > 0)
        {
            return custom;
        }

        if (!string.IsNullOrWhiteSpace(role) && configuration.RoleDefaultHeadcount.TryGetValue(role, out var defaultValue) && defaultValue > 0)
        {
            return defaultValue;
        }

        return 1m;
    }

    private WarrantyCostSummary CalculateWarranty(CostEstimationConfiguration configuration, CostEstimationInputs inputs)
    {
        configuration.RoleMonthlySalaries.TryGetValue("BA Junior", out var analystSalary);
        configuration.RoleMonthlySalaries.TryGetValue("Dev Junior", out var developerSalary);

        var total = inputs.WarrantyAnalystResources * analystSalary * inputs.WarrantyDurationMonths
                    + inputs.WarrantyDeveloperResources * developerSalary * inputs.WarrantyDurationMonths;

        return new WarrantyCostSummary
        {
            AnalystResources = inputs.WarrantyAnalystResources,
            DeveloperResources = inputs.WarrantyDeveloperResources,
            DurationMonths = inputs.WarrantyDurationMonths,
            AnalystMonthlySalary = analystSalary,
            DeveloperMonthlySalary = developerSalary,
            TotalCost = Decimal.Round(total, 2)
        };
    }

    private RevenueSummary CalculateRevenue(TimelineRecord timeline, CostEstimationConfiguration configuration, CostEstimationInputs inputs)
    {
        var rows = new List<RevenueRow>();
        var rateCardKey = string.IsNullOrWhiteSpace(inputs.RateCardKey) ? configuration.DefaultRateCardKey : inputs.RateCardKey;
        configuration.RateCards.TryGetValue(rateCardKey, out var rateCard);

        decimal nilaiProject = 0m;

        foreach (var allocation in timeline.ResourceAllocation)
        {
            var rate = rateCard?.RoleRates.TryGetValue(allocation.Role, out var r) == true ? r : 0m;
            var price = (decimal)allocation.TotalManDays * rate;
            nilaiProject += price;

            rows.Add(new RevenueRow
            {
                Role = allocation.Role,
                ManDays = (decimal)allocation.TotalManDays,
                RatePerDay = rate,
                MandaysPrice = Decimal.Round(price, 2)
            });
        }

        var priceAfterMultiplier = nilaiProject * inputs.Multiplier;
        var discountAmount = priceAfterMultiplier * inputs.DiscountPercent / 100m;
        var priceAfterDiscount = priceAfterMultiplier - discountAmount;

        return new RevenueSummary
        {
            Rows = rows,
            NilaiProject = Decimal.Round(nilaiProject, 2),
            Multiplier = inputs.Multiplier,
            DiscountPercent = inputs.DiscountPercent,
            PriceAfterMultiplier = Decimal.Round(priceAfterMultiplier, 2),
            DiscountAmount = Decimal.Round(discountAmount, 2),
            PriceAfterDiscount = Decimal.Round(priceAfterDiscount, 2)
        };
    }

    private static decimal ApplyCommission(List<CommissionBracket> brackets, decimal amount)
    {
        if (amount <= 0 || brackets == null || brackets.Count == 0)
        {
            return 0m;
        }

        decimal remaining = amount;
        decimal total = 0m;

        foreach (var bracket in brackets)
        {
            if (bracket.UpperBound <= 0)
            {
                total += remaining * bracket.RatePercent / 100m;
                break;
            }

            var apply = Math.Min(remaining, bracket.UpperBound);
            total += apply * bracket.RatePercent / 100m;
            remaining -= apply;
            if (remaining <= 0)
            {
                break;
            }
        }

        return total;
    }

    private CostEstimationInputs MergeInputs(CostEstimationConfiguration configuration, CostEstimationInputs? overrides)
    {
        var inputs = new CostEstimationInputs
        {
            WorstCaseBufferPercent = configuration.DefaultWorstCaseBufferPercent,
            WarrantyAnalystResources = 0,
            WarrantyDeveloperResources = 0,
            WarrantyDurationMonths = 1,
            AnnualInterestRatePercent = configuration.DefaultAnnualInterestRatePercent,
            ClientPaymentDelayMonths = configuration.DefaultClientPaymentDelayMonths,
            OverheadPercent = configuration.DefaultOverheadPercent,
            ExternalCommissionMode = configuration.DefaultExternalCommissionMode,
            ExternalCommissionPercentage = configuration.DefaultExternalCommissionPercent,
            ExternalCommissionAmount = 0,
            PphPercent = configuration.DefaultPphPercent,
            OperationalCostPercent = configuration.DefaultOperationalCostPercent,
            Multiplier = configuration.DefaultMultiplier,
            DiscountPercent = configuration.DefaultDiscountPercent,
            RateCardKey = configuration.DefaultRateCardKey
        };

        if (overrides != null)
        {
            inputs.WorstCaseBufferPercent = overrides.WorstCaseBufferPercent;
            inputs.WarrantyAnalystResources = overrides.WarrantyAnalystResources;
            inputs.WarrantyDeveloperResources = overrides.WarrantyDeveloperResources;
            inputs.WarrantyDurationMonths = overrides.WarrantyDurationMonths <= 0 ? inputs.WarrantyDurationMonths : overrides.WarrantyDurationMonths;
            inputs.AnnualInterestRatePercent = overrides.AnnualInterestRatePercent <= 0 ? inputs.AnnualInterestRatePercent : overrides.AnnualInterestRatePercent;
            inputs.ClientPaymentDelayMonths = overrides.ClientPaymentDelayMonths < 0 ? inputs.ClientPaymentDelayMonths : overrides.ClientPaymentDelayMonths;
            inputs.OverheadPercent = overrides.OverheadPercent < 0 ? inputs.OverheadPercent : overrides.OverheadPercent;
            inputs.ExternalCommissionMode = overrides.ExternalCommissionMode;
            inputs.ExternalCommissionPercentage = overrides.ExternalCommissionPercentage;
            inputs.ExternalCommissionAmount = overrides.ExternalCommissionAmount;
            inputs.PphPercent = overrides.PphPercent < 0 ? inputs.PphPercent : overrides.PphPercent;
            inputs.OperationalCostPercent = overrides.OperationalCostPercent < 0 ? inputs.OperationalCostPercent : overrides.OperationalCostPercent;
            inputs.Multiplier = overrides.Multiplier <= 0 ? inputs.Multiplier : overrides.Multiplier;
            inputs.DiscountPercent = overrides.DiscountPercent;
            inputs.RateCardKey = string.IsNullOrWhiteSpace(overrides.RateCardKey) ? inputs.RateCardKey : overrides.RateCardKey;

            foreach (var kvp in overrides.RoleHeadcounts)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key))
                {
                    inputs.RoleHeadcounts[kvp.Key] = kvp.Value;
                }
            }
        }

        inputs.WorstCaseBufferPercent = Math.Max(0, inputs.WorstCaseBufferPercent);
        inputs.WarrantyAnalystResources = Math.Max(0, inputs.WarrantyAnalystResources);
        inputs.WarrantyDeveloperResources = Math.Max(0, inputs.WarrantyDeveloperResources);
        inputs.WarrantyDurationMonths = Math.Max(0, inputs.WarrantyDurationMonths);
        inputs.AnnualInterestRatePercent = Math.Max(0, inputs.AnnualInterestRatePercent);
        inputs.ClientPaymentDelayMonths = Math.Max(0, inputs.ClientPaymentDelayMonths);
        inputs.OverheadPercent = Math.Max(0, inputs.OverheadPercent);
        inputs.ExternalCommissionPercentage = Math.Max(0, inputs.ExternalCommissionPercentage);
        inputs.ExternalCommissionAmount = Math.Max(0, inputs.ExternalCommissionAmount);
        inputs.PphPercent = Math.Max(0, inputs.PphPercent);
        inputs.OperationalCostPercent = Math.Max(0, inputs.OperationalCostPercent);
        inputs.Multiplier = Math.Max(0.01m, inputs.Multiplier);
        inputs.DiscountPercent = Math.Max(0, inputs.DiscountPercent);

        return inputs;
    }

    private AdjustableField? ResolveAdjustableField(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return key.ToLowerInvariant() switch
        {
            "discount" or "discountpercent" => new AdjustableField(
                getter: inputs => inputs.DiscountPercent,
                setter: (inputs, value) => inputs.DiscountPercent = Clamp(value, 0, 100),
                minValue: 0,
                maxValue: 100,
                tolerance: 0.01m),
            "multiplier" => new AdjustableField(
                getter: inputs => inputs.Multiplier,
                setter: (inputs, value) => inputs.Multiplier = Clamp(value, 0.1m, 10m),
                minValue: 0.1m,
                maxValue: 10m,
                tolerance: 0.01m),
            "buffer" or "worstcasebuffer" => new AdjustableField(
                getter: inputs => inputs.WorstCaseBufferPercent,
                setter: (inputs, value) => inputs.WorstCaseBufferPercent = Clamp(value, 0m, 200m),
                minValue: 0m,
                maxValue: 200m,
                tolerance: 0.1m),
            _ => null
        };
    }

    private static Func<CostEstimationResult, decimal>? ResolveTargetField(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return key.ToLowerInvariant() switch
        {
            "profit" or "profitamount" => result => result.Profitability.ProfitAmount,
            "profitpercent" or "profit%" => result => result.Profitability.ProfitPercent,
            "totalcost" => result => result.Profitability.TotalCost,
            _ => null
        };
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private sealed class AdjustableField
    {
        public AdjustableField(Func<CostEstimationInputs, decimal> getter, Action<CostEstimationInputs, decimal> setter, decimal minValue, decimal maxValue, decimal tolerance)
        {
            Getter = getter;
            Setter = setter;
            MinValue = minValue;
            MaxValue = maxValue;
            Tolerance = tolerance;
        }

        public Func<CostEstimationInputs, decimal> Getter { get; }
        public Action<CostEstimationInputs, decimal> Setter { get; }
        public decimal MinValue { get; }
        public decimal MaxValue { get; }
        public decimal Tolerance { get; }
    }
}
