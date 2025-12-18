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
    private readonly CostEstimationStore _resultStore;

    public CostEstimationService(
        TimelineStore timelineStore,
        CostEstimationConfigurationStore configurationStore,
        CostEstimationStore resultStore)
    {
        _timelineStore = timelineStore;
        _configurationStore = configurationStore;
        _resultStore = resultStore;
    }

    public async Task<CostEstimationResult?> GetAsync(int assessmentId, CostEstimationInputs? overrideInputs, CancellationToken cancellationToken)
    {
        if (overrideInputs == null)
        {
            var cached = await _resultStore.GetAsync(assessmentId, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                return cached;
            }
        }

        var timeline = await _timelineStore.GetAsync(assessmentId, null, cancellationToken).ConfigureAwait(false);
        if (timeline == null)
        {
            return null;
        }

        var config = await _configurationStore.GetAsync(cancellationToken).ConfigureAwait(false);
        var inputs = MergeInputs(config, overrideInputs);
        var result = Calculate(timeline, config, inputs);
        await _resultStore.SaveAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<GoalSeekResponse?> GoalSeekAsync(int assessmentId, GoalSeekRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return null;
        }

        var timeline = await _timelineStore.GetAsync(assessmentId, null, cancellationToken).ConfigureAwait(false);
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
        var response = new GoalSeekResponse
        {
            Inputs = inputs,
            Result = currentResult,
            Iterations = iterations,
            Converged = converged
        };
        if (currentResult != null)
        {
            await _resultStore.SaveAsync(currentResult, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    public async Task<byte[]> ExportAsync(int assessmentId, CostEstimationInputs? overrideInputs, CancellationToken cancellationToken)
    {
        var timeline = await _timelineStore.GetAsync(assessmentId, null, cancellationToken).ConfigureAwait(false);
        if (timeline == null)
        {
            throw new KeyNotFoundException("Timeline not found for assessment");
        }

        var config = await _configurationStore.GetAsync(cancellationToken).ConfigureAwait(false);
        var inputs = MergeInputs(config, overrideInputs);
        var result = Calculate(timeline, config, inputs);
        await _resultStore.SaveAsync(result, cancellationToken).ConfigureAwait(false);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Estimation");

        // Styling Constants
        var headerColor = XLColor.FromHtml("#1F4E78"); // Dark Blue
        var whiteColor = XLColor.White;

        var currencyFormat = "#,##0";
        var decimalFormat = "#,##0.00";
        var percentFormat = "0.00%";

        // Helper to create headers
        void CreateHeader(IXLRange range)
        {
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = whiteColor;
            range.Style.Fill.BackgroundColor = headerColor;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        void ApplyTitleStyle(IXLCell cell)
        {
            cell.Style.Font.FontSize = 14;
            cell.Style.Font.Bold = true;
        }

        void AddBorder(IXLRange range)
        {
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // Project Info
        sheet.Cell("A1").Value = "PROJECT COST ESTIMATION";
        sheet.Range("A1:F1").Merge().Style.Font.FontSize = 16;
        sheet.Range("A1:F1").Style.Font.Bold = true;
        sheet.Range("A1:F1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        sheet.Cell("A3").Value = "Project Name";
        sheet.Cell("B3").Value = result.ProjectName;
        sheet.Cell("A4").Value = "Template";
        sheet.Cell("B4").Value = result.TemplateName;
        sheet.Cell("A5").Value = "Date";
        sheet.Cell("B5").Value = DateTime.Now.ToString("dd MMM yyyy");
        sheet.Range("A3:A5").Style.Font.Bold = true;

        int row = 8;

        // 1. Role Costs Table
        sheet.Cell(row, 1).Value = "1. ROLE COST BREAKDOWN";
        ApplyTitleStyle(sheet.Cell(row, 1));
        row++;

        var roleHeaderRow = row;
        sheet.Cell(row, 1).Value = "Role";
        sheet.Cell(row, 2).Value = "Resources";
        sheet.Cell(row, 3).Value = "Monthly Salary (IDR)";
        sheet.Cell(row, 4).Value = "Best Case (Mo)";
        sheet.Cell(row, 5).Value = "Worst Case (Mo)";
        sheet.Cell(row, 6).Value = "Total Cost (IDR)";
        CreateHeader(sheet.Range(row, 1, row, 6));
        row++;

        foreach (var rc in result.RoleCosts)
        {
            sheet.Cell(row, 1).Value = rc.Role;
            sheet.Cell(row, 2).Value = rc.Resources;
            sheet.Cell(row, 3).Value = rc.MonthlySalary;
            sheet.Cell(row, 3).Style.NumberFormat.Format = currencyFormat;
            sheet.Cell(row, 4).Value = rc.BestCaseMonths;
            sheet.Cell(row, 5).FormulaA1 = $"=D{row}*(1+{result.Inputs.WorstCaseBufferPercent}/100)";
            sheet.Cell(row, 6).FormulaA1 = $"=B{row}*C{row}*E{row}";
            sheet.Cell(row, 6).Style.NumberFormat.Format = currencyFormat;
            row++;
        }

        sheet.Cell(row, 5).Value = "Total Salaries";
        sheet.Cell(row, 5).Style.Font.Bold = true;
        sheet.Cell(row, 6).FormulaA1 = $"=SUM(F{roleHeaderRow + 1}:F{row - 1})";
        sheet.Cell(row, 6).Style.NumberFormat.Format = currencyFormat;
        sheet.Cell(row, 6).Style.Font.Bold = true;
        
        AddBorder(sheet.Range(roleHeaderRow, 1, row, 6));
        row += 3;

        // 2. Warranty
        sheet.Cell(row, 1).Value = "2. WARRANTY SUPPORT";
        ApplyTitleStyle(sheet.Cell(row, 1));
        row++;

        var warrantyHeaderRow = row;
        sheet.Cell(row, 1).Value = "Item";
        sheet.Cell(row, 2).Value = "Count / Months";
        sheet.Cell(row, 3).Value = "Rate / Salary (IDR)";
        sheet.Cell(row, 4).Value = "Subtotal (IDR)";
        CreateHeader(sheet.Range(row, 1, row, 4));
        row++;

        // Analyst
        sheet.Cell(row, 1).Value = "Analyst Support";
        sheet.Cell(row, 2).Value = inputs.WarrantyAnalystResources;
        sheet.Cell(row, 3).Value = result.Warranty.AnalystMonthlySalary;
        sheet.Cell(row, 3).Style.NumberFormat.Format = currencyFormat;
        sheet.Cell(row, 4).FormulaA1 = $"=B{row}*C{row}*{inputs.WarrantyDurationMonths}";
        sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
        row++;

        // Developer
        sheet.Cell(row, 1).Value = "Developer Support";
        sheet.Cell(row, 2).Value = inputs.WarrantyDeveloperResources;
        sheet.Cell(row, 3).Value = result.Warranty.DeveloperMonthlySalary;
        sheet.Cell(row, 3).Style.NumberFormat.Format = currencyFormat;
        sheet.Cell(row, 4).FormulaA1 = $"=B{row}*C{row}*{inputs.WarrantyDurationMonths}";
        sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
        row++;

        sheet.Cell(row, 1).Value = "Duration (Months)";
        sheet.Cell(row, 2).Value = inputs.WarrantyDurationMonths;
        row++;

        sheet.Cell(row, 3).Value = "Total Warranty";
        sheet.Cell(row, 3).Style.Font.Bold = true;
        sheet.Cell(row, 4).FormulaA1 = $"=D{row - 3}+D{row - 2}";
        sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
        sheet.Cell(row, 4).Style.Font.Bold = true;

        AddBorder(sheet.Range(warrantyHeaderRow, 1, row, 4));
        row += 3;


        // 3. Components Summary
        sheet.Cell(row, 1).Value = "3. COST COMPONENTS";
        ApplyTitleStyle(sheet.Cell(row, 1));
        row++;

        var compHeaderRow = row;
        sheet.Cell(row, 1).Value = "Component";
        sheet.Cell(row, 2).Value = "Cost (IDR)";
        CreateHeader(sheet.Range(row, 1, row, 2));
        row++;

        var components = result.Components;
        AddRow(sheet, ref row, "Financing Cost", components.FinancingCost, currencyFormat);
        AddRow(sheet, ref row, "Overhead Cost", components.OverheadCost, currencyFormat);
        AddRow(sheet, ref row, "Operational Cost", components.OperationalCost, currencyFormat);
        AddRow(sheet, ref row, "External Commission", components.ExternalCommission, currencyFormat);
        AddRow(sheet, ref row, "PPH", components.PphCost, currencyFormat);
        AddRow(sheet, ref row, "Sales Commission", components.SalesCommission, currencyFormat);
        AddRow(sheet, ref row, "Cost Commission", components.CostCommission, currencyFormat);

        AddBorder(sheet.Range(compHeaderRow, 1, row - 1, 2));
        row += 3;


        // 4. Revenue & Commercial
        sheet.Cell(row, 1).Value = "4. REVENUE & COMMERCIAL";
        ApplyTitleStyle(sheet.Cell(row, 1));
        row++;

        var revHeaderRow = row;
        sheet.Cell(row, 1).Value = "Role";
        sheet.Cell(row, 2).Value = "Man Days";
        sheet.Cell(row, 3).Value = "Rate (IDR)";
        sheet.Cell(row, 4).Value = "Total Price (IDR)";
        CreateHeader(sheet.Range(row, 1, row, 4));
        row++;

        var revDataStart = row;
        foreach (var rev in result.Revenue.Rows)
        {
            sheet.Cell(row, 1).Value = rev.Role;
            sheet.Cell(row, 2).Value = rev.ManDays;
            sheet.Cell(row, 2).Style.NumberFormat.Format = decimalFormat;
            sheet.Cell(row, 3).Value = rev.RatePerDay;
            sheet.Cell(row, 3).Style.NumberFormat.Format = currencyFormat;
            sheet.Cell(row, 4).FormulaA1 = $"=B{row}*C{row}";
            sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
            row++;
        }

        sheet.Cell(row, 3).Value = "Project Value (Gross)";
        sheet.Cell(row, 3).Style.Font.Bold = true;
        sheet.Cell(row, 4).FormulaA1 = $"=SUM(D{revDataStart}:D{row - 1})";
        sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
        sheet.Cell(row, 4).Style.Font.Bold = true;
        row++;

        sheet.Cell(row, 3).Value = "Multiplier";
        sheet.Cell(row, 4).Value = inputs.Multiplier;
        sheet.Cell(row, 4).Style.NumberFormat.Format = "0.00";
        row++;

        sheet.Cell(row, 3).Value = "Price Before Discount";
        sheet.Cell(row, 3).Style.Font.Bold = true;
        sheet.Cell(row, 4).FormulaA1 = $"=D{row - 2}*D{row - 1}";
        sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
        sheet.Cell(row, 4).Style.Font.Bold = true;
        var pamRow = row;
        row++;

        sheet.Cell(row, 3).Value = "Discount";
        sheet.Cell(row, 4).Value = inputs.DiscountPercent / 100m;
        sheet.Cell(row, 4).Style.NumberFormat.Format = percentFormat;
        row++;

        sheet.Cell(row, 3).Value = "Discount Amount";
        sheet.Cell(row, 4).FormulaA1 = $"=D{pamRow}*D{row - 1}";
        sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
        row++;

        sheet.Cell(row, 3).Value = "Net Price";
        sheet.Cell(row, 3).Style.Font.Bold = true;
        sheet.Cell(row, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EFDA"); // Light Green
        sheet.Cell(row, 4).FormulaA1 = $"=D{pamRow}-D{row - 1}";
        sheet.Cell(row, 4).Style.NumberFormat.Format = currencyFormat;
        sheet.Cell(row, 4).Style.Font.Bold = true;
        sheet.Cell(row, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EFDA"); // Light Green

        AddBorder(sheet.Range(revHeaderRow, 1, row, 4));
        row += 3;

        // 5. Profitability
        sheet.Cell(row, 1).Value = "5. PROFITABILITY ANALYSIS";
        ApplyTitleStyle(sheet.Cell(row, 1));
        row++;

        var profitHeaderRow = row;
        sheet.Cell(row, 1).Value = "Metric";
        sheet.Cell(row, 2).Value = "Value";
        CreateHeader(sheet.Range(row, 1, row, 2));
        row++;

        AddRow(sheet, ref row, "Total Cost", result.Profitability.TotalCost, currencyFormat);
        AddRow(sheet, ref row, "Profit Amount", result.Profitability.ProfitAmount, currencyFormat);

        sheet.Cell(row, 1).Value = "Profit Margin";
        sheet.Cell(row, 2).Value = result.Profitability.ProfitPercent / 100m;
        sheet.Cell(row, 2).Style.NumberFormat.Format = percentFormat;
        sheet.Cell(row, 2).Style.Font.Bold = true;
        
        AddBorder(sheet.Range(profitHeaderRow, 1, row, 2));

        // Layout Adjustments
        sheet.Column(1).Width = 35;
        sheet.Column(2).Width = 18;
        sheet.Column(3).Width = 22;
        sheet.Column(4).Width = 22;
        sheet.Column(5).Width = 22;
        sheet.Column(6).Width = 25;

        using var stream = new System.IO.MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddRow(IXLWorksheet sheet, ref int row, string label, decimal value, string format)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value;
        sheet.Cell(row, 2).Style.NumberFormat.Format = format;
        row++;
    }

    public CostEstimationResult Calculate(TimelineRecord timeline, CostEstimationConfiguration configuration, CostEstimationInputs inputs)
    {
        var roleCosts = new List<RoleCostRow>();
        decimal totalSalary = 0m;
        decimal maxDuration = 0m;

        foreach (var allocation in timeline.ResourceAllocation)
        {
            var monthlySalary = GetRoleValueOrDefault(configuration.RoleMonthlySalaries, allocation.Role, 0m);

            var resources = ResolveHeadcount(allocation, inputs, configuration);
            var activeDays = allocation.DailyEffort?.Count(d => d > 0) ?? 0;
            var bestCaseMonths = (decimal)activeDays / 20m;
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

    private static decimal ResolveHeadcount(TimelineResourceAllocationEntry allocation, CostEstimationInputs inputs, CostEstimationConfiguration configuration)
    {
        var role = allocation.Role;
        if (!string.IsNullOrWhiteSpace(role) && inputs.RoleHeadcounts.TryGetValue(role, out var custom) && custom > 0)
        {
            return custom;
        }

        if (!string.IsNullOrWhiteSpace(role) && TryGetRoleValue(configuration.RoleDefaultHeadcount, role, out var defaultValue) && defaultValue > 0)
        {
            return defaultValue;
        }

        if (allocation.DailyEffort != null && allocation.DailyEffort.Any())
        {
            var maxEffort = (decimal)allocation.DailyEffort.Max();
            if (maxEffort > 0)
            {
                return Math.Max(1m, Math.Ceiling(maxEffort));
            }
        }

        return 1m;
    }

    private WarrantyCostSummary CalculateWarranty(CostEstimationConfiguration configuration, CostEstimationInputs inputs)
    {
        var analystSalary = GetRoleValueFromPartsOrDefault(configuration.RoleMonthlySalaries, "Business Analyst", "Junior", 0m);
        if (analystSalary <= 0m)
        {
            analystSalary = GetRoleValueFromPartsOrDefault(configuration.RoleMonthlySalaries, "BA", "Junior", 0m);
        }

        var developerSalary = GetRoleValueFromPartsOrDefault(configuration.RoleMonthlySalaries, "Developer", "Junior", 0m);
        if (developerSalary <= 0m)
        {
            developerSalary = GetRoleValueFromPartsOrDefault(configuration.RoleMonthlySalaries, "Dev", "Junior", 0m);
        }

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
            var rate = rateCard != null
                ? GetRoleValueOrDefault(rateCard.RoleRates, allocation.Role, 0m)
                : 0m;
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

    private static bool TryGetRoleValue<T>(IDictionary<string, T> source, string roleLabel, out T value)
        where T : struct
    {
        foreach (var key in PresalesRoleFormatter.EnumerateLookupKeysFromLabel(roleLabel))
        {
            if (source.TryGetValue(key, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static T GetRoleValueOrDefault<T>(IDictionary<string, T> source, string roleLabel, T defaultValue)
        where T : struct
    {
        return TryGetRoleValue(source, roleLabel, out var value) ? value : defaultValue;
    }

    private static T GetRoleValueFromPartsOrDefault<T>(IDictionary<string, T> source, string roleName, string expectedLevel, T defaultValue)
        where T : struct
    {
        foreach (var key in PresalesRoleFormatter.EnumerateLookupKeys(roleName, expectedLevel))
        {
            if (source.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return defaultValue;
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
