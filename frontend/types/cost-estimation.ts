export type CommissionMode = 'ManualAmount' | 'Percentage';

export interface CostEstimationInputs {
  worstCaseBufferPercent: number;
  roleHeadcounts: Record<string, number>;
  warrantyAnalystResources: number;
  warrantyDeveloperResources: number;
  warrantyDurationMonths: number;
  annualInterestRatePercent: number;
  clientPaymentDelayMonths: number;
  overheadPercent: number;
  externalCommissionMode: CommissionMode;
  externalCommissionPercentage: number;
  externalCommissionAmount: number;
  pphPercent: number;
  operationalCostPercent: number;
  multiplier: number;
  discountPercent: number;
  rateCardKey: string;
}

export interface RoleCostRow {
  role: string;
  resources: number;
  monthlySalary: number;
  bestCaseMonths: number;
  worstCaseMonths: number;
  totalCost: number;
}

export interface WarrantyCostSummary {
  analystResources: number;
  developerResources: number;
  durationMonths: number;
  analystMonthlySalary: number;
  developerMonthlySalary: number;
  totalCost: number;
}

export interface CostComponentSummary {
  financingCost: number;
  overheadCost: number;
  externalCommission: number;
  operationalCost: number;
  pphCost: number;
  salesCommission: number;
  costCommission: number;
}

export interface RevenueRow {
  role: string;
  manDays: number;
  ratePerDay: number;
  mandaysPrice: number;
}

export interface RevenueSummary {
  rows: RevenueRow[];
  nilaiProject: number;
  multiplier: number;
  discountPercent: number;
  priceAfterMultiplier: number;
  discountAmount: number;
  priceAfterDiscount: number;
}

export interface ProfitabilitySummary {
  totalCost: number;
  profitAmount: number;
  profitPercent: number;
}

export interface CostEstimationResult {
  assessmentId: number;
  projectName: string;
  templateName: string;
  inputs: CostEstimationInputs;
  roleCosts: RoleCostRow[];
  totalSalaries: number;
  projectDurationMonths: number;
  warranty: WarrantyCostSummary;
  components: CostComponentSummary;
  revenue: RevenueSummary;
  profitability: ProfitabilitySummary;
}

export interface GoalSeekRequest {
  inputs: CostEstimationInputs;
  targetField: string;
  targetValue: number;
  adjustableField: string;
  minValue?: number;
  maxValue?: number;
}

export interface GoalSeekResponse {
  inputs: CostEstimationInputs;
  result: CostEstimationResult;
  iterations: number;
  converged: boolean;
}

export interface CostEstimationConfiguration {
  roleMonthlySalaries: Record<string, number>;
  roleDefaultHeadcount: Record<string, number>;
  rateCards: Record<string, { displayName: string; roleRates: Record<string, number> }>;
  defaultRateCardKey: string;
  defaultWorstCaseBufferPercent: number;
  defaultAnnualInterestRatePercent: number;
  defaultClientPaymentDelayMonths: number;
  defaultOverheadPercent: number;
  defaultOperationalCostPercent: number;
  defaultPphPercent: number;
  defaultExternalCommissionPercent: number;
  defaultExternalCommissionMode: CommissionMode;
  defaultMultiplier: number;
  defaultDiscountPercent: number;
}
