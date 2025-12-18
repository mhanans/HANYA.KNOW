import type { ChangeEvent } from 'react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  Grid,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  SelectChangeEvent,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { apiFetch } from '../../../lib/api';
import {
  CommissionMode,
  CostEstimationConfiguration,
  CostEstimationInputs,
  CostEstimationResult,
  GoalSeekRequest,
  GoalSeekResponse,
} from '../../../types/cost-estimation';

const currencyFormatter = new Intl.NumberFormat('id-ID', {
  style: 'currency',
  currency: 'IDR',
  minimumFractionDigits: 0,
});

const percentFormatter = new Intl.NumberFormat('id-ID', {
  maximumFractionDigits: 2,
});

const formatCurrency = (value: number) => (Number.isFinite(value) ? currencyFormatter.format(value) : 'Rp 0');
const formatNumber = (value: number, fractionDigits = 2) =>
  Number.isFinite(value) ? value.toFixed(fractionDigits) : '0';

const goalSeekTargets = [
  { value: 'ProfitPercent', label: 'Profit %' },
  { value: 'ProfitAmount', label: 'Profit Amount' },
  { value: 'TotalCost', label: 'Total Cost' },
];

const goalSeekAdjustables = [
  { value: 'Discount', label: 'Discount (%)' },
  { value: 'Multiplier', label: 'Multiplier' },
  { value: 'WorstCaseBuffer', label: 'Worst Case Buffer (%)' },
];

interface GoalSeekFormState {
  targetField: string;
  targetValue: string;
  adjustableField: string;
  minValue?: string;
  maxValue?: string;
}

export default function CostEstimationDetailPage() {
  const router = useRouter();
  const { assessmentId } = router.query;
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<CostEstimationResult | null>(null);
  const [inputs, setInputs] = useState<CostEstimationInputs | null>(null);
  const [config, setConfig] = useState<CostEstimationConfiguration | null>(null);
  const [recalculating, setRecalculating] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [goalSeekOpen, setGoalSeekOpen] = useState(false);
  const [goalSeekState, setGoalSeekState] = useState<GoalSeekFormState>({
    targetField: 'ProfitPercent',
    adjustableField: 'Discount',
    targetValue: '',
  });
  const [goalSeekResult, setGoalSeekResult] = useState<GoalSeekResponse | null>(null);
  const [goalSeekError, setGoalSeekError] = useState<string | null>(null);

  const loadConfiguration = useCallback(async () => {
    try {
      const res = await apiFetch('/api/cost-estimations/configuration');
      if (res.ok) {
        const data = await res.json();
        setConfig(data);
      }
    } catch (err) {
      console.warn('Failed to load configuration', err);
    }
  }, []);

  const loadData = useCallback(async () => {
    if (!assessmentId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch(`/api/cost-estimations/${assessmentId}`);
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || `Failed to load estimation (${res.status})`);
      }
      const data = (await res.json()) as CostEstimationResult;
      setResult(data);
      setInputs(JSON.parse(JSON.stringify(data.inputs)));
      setGoalSeekResult(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load cost estimation');
    } finally {
      setLoading(false);
    }
  }, [assessmentId]);

  useEffect(() => {
    loadConfiguration();
  }, [loadConfiguration]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleNumericChange = useCallback(
    (field: keyof CostEstimationInputs) => (event: ChangeEvent<HTMLInputElement>) => {
      if (!inputs) return;
      const value = Number(event.target.value);
      setInputs({ ...inputs, [field]: Number.isFinite(value) ? value : 0 });
    },
    [inputs]
  );

  const handleHeadcountChange = useCallback(
    (role: string) => (event: ChangeEvent<HTMLInputElement>) => {
      if (!inputs) return;
      const value = Number(event.target.value);
      setInputs({
        ...inputs,
        roleHeadcounts: {
          ...inputs.roleHeadcounts,
          [role]: Number.isFinite(value) ? value : 0,
        },
      });
    },
    [inputs]
  );

  const handleCommissionModeChange = useCallback(
    (event: SelectChangeEvent<CommissionMode>) => {
      if (!inputs) return;
      setInputs({ ...inputs, externalCommissionMode: event.target.value as CommissionMode });
    },
    [inputs]
  );

  const handleRateCardChange = useCallback(
    (event: SelectChangeEvent<string>) => {
      if (!inputs) return;
      setInputs({ ...inputs, rateCardKey: event.target.value });
    },
    [inputs]
  );

  const handleRecalculate = useCallback(async () => {
    if (!inputs || !assessmentId) return;
    setRecalculating(true);
    setGoalSeekError(null);
    try {
      const res = await apiFetch(`/api/cost-estimations/${assessmentId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ inputs }),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to recalculate');
      }
      const data = (await res.json()) as CostEstimationResult;
      setResult(data);
      setInputs(JSON.parse(JSON.stringify(data.inputs)));
      setGoalSeekResult(null);
    } catch (err) {
      setGoalSeekError(err instanceof Error ? err.message : 'Failed to recalculate');
    } finally {
      setRecalculating(false);
    }
  }, [inputs, assessmentId]);

  const handleDownload = useCallback(async () => {
    if (!assessmentId || !inputs) return;
    setDownloading(true);
    try {
      const res = await apiFetch(`/api/cost-estimations/${assessmentId}/export`, {
        method: 'POST',
        body: JSON.stringify({ inputs }),
        headers: { 'Content-Type': 'application/json' },
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to export estimation');
      }
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `Cost-Estimation-${assessmentId}.xlsx`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      setGoalSeekError(err instanceof Error ? err.message : 'Failed to export estimation');
    } finally {
      setDownloading(false);
    }
  }, [assessmentId, inputs]);

  const handleGoalSeekSubmit = useCallback(async () => {
    if (!assessmentId || !inputs) return;
    if (!goalSeekState.targetValue) {
      setGoalSeekError('Target value is required for goal seek simulation.');
      return;
    }

    setGoalSeekError(null);
    setRecalculating(true);
    try {
      const payload: GoalSeekRequest = {
        inputs,
        targetField: goalSeekState.targetField,
        targetValue: Number(goalSeekState.targetValue),
        adjustableField: goalSeekState.adjustableField,
      };
      if (goalSeekState.minValue) payload.minValue = Number(goalSeekState.minValue);
      if (goalSeekState.maxValue) payload.maxValue = Number(goalSeekState.maxValue);

      const res = await apiFetch(`/api/cost-estimations/${assessmentId}/goal-seek`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Goal seek failed');
      }
      const data = (await res.json()) as GoalSeekResponse;
      setGoalSeekResult(data);
      setResult(data.result);
      setInputs(JSON.parse(JSON.stringify(data.inputs)));
      setGoalSeekOpen(false);
    } catch (err) {
      setGoalSeekError(err instanceof Error ? err.message : 'Goal seek failed');
    } finally {
      setRecalculating(false);
    }
  }, [assessmentId, inputs, goalSeekState]);

  const handleGoalSeekFieldChange = useCallback(
    (field: keyof GoalSeekFormState) => (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement> | SelectChangeEvent<string>) => {
      const value = event.target.value as string;
      setGoalSeekState(prev => ({ ...prev, [field]: value }));
    },
    []
  );

  const roleHeadcounts = useMemo(() => {
    if (!inputs) return {} as Record<string, number>;
    const map: Record<string, number> = { ...inputs.roleHeadcounts };
    result?.roleCosts.forEach(row => {
      if (map[row.role] === undefined) {
        map[row.role] = row.resources;
      }
    });
    return map;
  }, [inputs, result]);

  const rateCardOptions = useMemo(() => {
    if (!config) return [] as { key: string; label: string }[];
    return Object.entries(config.rateCards ?? {}).map(([key, value]) => ({ key, label: value.displayName || key }));
  }, [config]);

  const layout = useMemo(() => {
    if (loading) {
      return (
        <Stack direction="row" justifyContent="center" alignItems="center" spacing={2} sx={{ py: 10 }}>
          <CircularProgress size={36} />
          <Typography variant="body1">Loading cost estimation…</Typography>
        </Stack>
      );
    }

    if (error) {
      return (
        <Box sx={{ py: 10, textAlign: 'center' }}>
          <Typography color="error" sx={{ mb: 2 }}>
            {error}
          </Typography>
          <Button variant="contained" onClick={loadData}>
            Retry
          </Button>
        </Box>
      );
    }

    if (!result || !inputs) {
      return null;
    }

    return (
      <Grid container spacing={3} alignItems="flex-start">
        <Grid item xs={12} md={6}>
          <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: '1px solid rgba(0,0,0,0.08)' }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 2 }}>
              <Typography variant="h5">Project Cost Breakdown</Typography>
              <TextField
                label="Worst Case Buffer (%)"
                type="number"
                size="small"
                value={inputs.worstCaseBufferPercent}
                onChange={handleNumericChange('worstCaseBufferPercent')}
              />
            </Stack>
            <TableContainer>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Role</TableCell>
                    <TableCell>Resources</TableCell>
                    <TableCell>Salary (IDR)</TableCell>
                    <TableCell>Best (mo)</TableCell>
                    <TableCell>Worst (mo)</TableCell>
                    <TableCell align="right">Total</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {result.roleCosts
                    .filter(row => row.role !== 'Unassigned')
                    .map(row => (
                      <TableRow key={row.role}>
                        <TableCell>{row.role}</TableCell>
                        <TableCell>
                          <TextField
                            size="small"
                            type="number"
                            value={roleHeadcounts[row.role] ?? row.resources}
                            onChange={handleHeadcountChange(row.role)}
                            inputProps={{ min: 0, step: 0.1 }}
                          />
                        </TableCell>
                        <TableCell>{formatCurrency(row.monthlySalary)}</TableCell>
                        <TableCell>{formatNumber(row.bestCaseMonths, 2)}</TableCell>
                        <TableCell>{formatNumber(row.worstCaseMonths, 2)}</TableCell>
                        <TableCell align="right">{formatCurrency(row.totalCost)}</TableCell>
                      </TableRow>
                    ))}
                  <TableRow>
                    <TableCell colSpan={5}>Total Salaries</TableCell>
                    <TableCell align="right">{formatCurrency(result.totalSalaries)}</TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell colSpan={5}>Project Duration (months)</TableCell>
                    <TableCell align="right">{formatNumber(result.projectDurationMonths, 2)}</TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </TableContainer>

            <Box sx={{ mt: 4 }}>
              <Typography variant="subtitle1" gutterBottom>
                Warranty
              </Typography>
              <Grid container spacing={2}>
                <Grid item xs={12} sm={4}>
                  <TextField
                    fullWidth
                    label="Analyst Resources"
                    type="number"
                    size="small"
                    value={inputs.warrantyAnalystResources}
                    onChange={handleNumericChange('warrantyAnalystResources')}
                  />
                </Grid>
                <Grid item xs={12} sm={4}>
                  <TextField
                    fullWidth
                    label="Developer Resources"
                    type="number"
                    size="small"
                    value={inputs.warrantyDeveloperResources}
                    onChange={handleNumericChange('warrantyDeveloperResources')}
                  />
                </Grid>
                <Grid item xs={12} sm={4}>
                  <TextField
                    fullWidth
                    label="Duration (months)"
                    type="number"
                    size="small"
                    value={inputs.warrantyDurationMonths}
                    onChange={handleNumericChange('warrantyDurationMonths')}
                  />
                </Grid>
              </Grid>
              <Stack spacing={0.5} sx={{ mt: 2 }}>
                <Typography variant="body2">
                  Analyst Salary: {formatCurrency(result.warranty.analystMonthlySalary)}
                </Typography>
                <Typography variant="body2">
                  Developer Salary: {formatCurrency(result.warranty.developerMonthlySalary)}
                </Typography>
                <Typography variant="subtitle2">Total Warranty: {formatCurrency(result.warranty.totalCost)}</Typography>
              </Stack>
            </Box>

            <Box sx={{ mt: 4 }}>
              <Typography variant="subtitle1" gutterBottom>
                Other Costs
              </Typography>
              <Grid container spacing={2}>
                <Grid item xs={12} sm={6}>
                  <TextField
                    fullWidth
                    label="Annual Interest Rate (%)"
                    type="number"
                    size="small"
                    value={inputs.annualInterestRatePercent}
                    onChange={handleNumericChange('annualInterestRatePercent')}
                  />
                </Grid>
                <Grid item xs={12} sm={6}>
                  <TextField
                    fullWidth
                    label="Payment Delay (months)"
                    type="number"
                    size="small"
                    value={inputs.clientPaymentDelayMonths}
                    onChange={handleNumericChange('clientPaymentDelayMonths')}
                  />
                </Grid>
                <Grid item xs={12} sm={4}>
                  <TextField
                    fullWidth
                    label="Overhead (%)"
                    type="number"
                    size="small"
                    value={inputs.overheadPercent}
                    onChange={handleNumericChange('overheadPercent')}
                  />
                </Grid>
                <Grid item xs={12} sm={4}>
                  <FormControl fullWidth size="small">
                    <InputLabel id="commission-mode">External Commission</InputLabel>
                    <Select
                      labelId="commission-mode"
                      value={inputs.externalCommissionMode}
                      label="External Commission"
                      onChange={handleCommissionModeChange}
                    >
                      <MenuItem value="Percentage">Percentage of Price</MenuItem>
                      <MenuItem value="ManualAmount">Manual Amount</MenuItem>
                    </Select>
                  </FormControl>
                </Grid>
                <Grid item xs={12} sm={4}>
                  {inputs.externalCommissionMode === 'ManualAmount' ? (
                    <TextField
                      fullWidth
                      label="Manual Amount"
                      type="number"
                      size="small"
                      value={inputs.externalCommissionAmount}
                      onChange={handleNumericChange('externalCommissionAmount')}
                    />
                  ) : (
                    <TextField
                      fullWidth
                      label="Commission (%)"
                      type="number"
                      size="small"
                      value={inputs.externalCommissionPercentage}
                      onChange={handleNumericChange('externalCommissionPercentage')}
                    />
                  )}
                </Grid>
                <Grid item xs={12} sm={4}>
                  <TextField
                    fullWidth
                    label="Operational Cost (%)"
                    type="number"
                    size="small"
                    value={inputs.operationalCostPercent}
                    onChange={handleNumericChange('operationalCostPercent')}
                  />
                </Grid>
                <Grid item xs={12} sm={4}>
                  <TextField
                    fullWidth
                    label="PPH (%)"
                    type="number"
                    size="small"
                    value={inputs.pphPercent}
                    onChange={handleNumericChange('pphPercent')}
                  />
                </Grid>
              </Grid>

              <Table size="small" sx={{ mt: 2 }}>
                <TableBody>
                  <TableRow>
                    <TableCell>Financing Cost</TableCell>
                    <TableCell align="right">{formatCurrency(result.components.financingCost)}</TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell>Overhead Cost</TableCell>
                    <TableCell align="right">{formatCurrency(result.components.overheadCost)}</TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell>Operational Cost</TableCell>
                    <TableCell align="right">{formatCurrency(result.components.operationalCost)}</TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell>External Commission</TableCell>
                    <TableCell align="right">{formatCurrency(result.components.externalCommission)}</TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell>PPH</TableCell>
                    <TableCell align="right">{formatCurrency(result.components.pphCost)}</TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell>Sales Commission</TableCell>
                    <TableCell align="right">{formatCurrency(result.components.salesCommission)}</TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell>Cost Commission</TableCell>
                    <TableCell align="right">{formatCurrency(result.components.costCommission)}</TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} md={6}>
          <Paper elevation={0} sx={{ p: 3, borderRadius: 3, border: '1px solid rgba(0,0,0,0.08)', position: 'sticky', top: 24 }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 2 }}>
              <Typography variant="h5">Revenue & Pricing</Typography>
              <FormControl size="small" sx={{ minWidth: 180 }}>
                <InputLabel id="rate-card-label">Rate Card</InputLabel>
                <Select
                  labelId="rate-card-label"
                  value={inputs.rateCardKey}
                  label="Rate Card"
                  onChange={handleRateCardChange}
                >
                  {rateCardOptions.length === 0 ? (
                    <MenuItem value={inputs.rateCardKey}>Default</MenuItem>
                  ) : (
                    rateCardOptions.map(option => (
                      <MenuItem key={option.key} value={option.key}>
                        {option.label}
                      </MenuItem>
                    ))
                  )}
                </Select>
              </FormControl>
            </Stack>

            <TableContainer>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Role</TableCell>
                    <TableCell>Mandays</TableCell>
                    <TableCell>Rate</TableCell>
                    <TableCell align="right">Mandays Price</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {result.revenue.rows
                    .filter(row => row.role !== 'Unassigned')
                    .map(row => (
                      <TableRow key={row.role}>
                        <TableCell>{row.role}</TableCell>
                        <TableCell>{formatNumber(row.manDays, 2)}</TableCell>
                        <TableCell>{formatCurrency(row.ratePerDay)}</TableCell>
                        <TableCell align="right">{formatCurrency(row.mandaysPrice)}</TableCell>
                      </TableRow>
                    ))}
                  <TableRow>
                    <TableCell colSpan={3}>Nilai Project</TableCell>
                    <TableCell align="right">{formatCurrency(result.revenue.nilaiProject)}</TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </TableContainer>

            <Stack spacing={2} sx={{ mt: 3 }}>
              <TextField
                label="Multiplier"
                type="number"
                size="small"
                value={inputs.multiplier}
                onChange={handleNumericChange('multiplier')}
              />
              <Typography variant="body2">
                Price after Multiplier: {formatCurrency(result.revenue.priceAfterMultiplier)}
              </Typography>
              <TextField
                label="Discount (%)"
                type="number"
                size="small"
                value={inputs.discountPercent}
                onChange={handleNumericChange('discountPercent')}
              />
              <Typography variant="body2">
                Discount Amount: {formatCurrency(result.revenue.discountAmount)}
              </Typography>
              <Typography variant="subtitle1">
                Price after Discount: {formatCurrency(result.revenue.priceAfterDiscount)}
              </Typography>
            </Stack>

            <Box sx={{ mt: 4, p: 2, borderRadius: 2, backgroundColor: 'rgba(25,118,210,0.06)' }}>
              <Typography variant="subtitle1">Profitability</Typography>
              <Typography variant="body2">Total Cost: {formatCurrency(result.profitability.totalCost)}</Typography>
              <Typography variant="body2">Profit Amount: {formatCurrency(result.profitability.profitAmount)}</Typography>
              <Typography variant="body1" fontWeight={600}>
                Profit %: {percentFormatter.format(result.profitability.profitPercent)}%
              </Typography>
            </Box>
          </Paper>
        </Grid>
      </Grid>
    );
  }, [loading, error, result, inputs, loadData, handleNumericChange, handleHeadcountChange, roleHeadcounts, handleCommissionModeChange, rateCardOptions]);

  return (
    <Box sx={{ maxWidth: 1400, mx: 'auto', py: 5, display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ xs: 'stretch', md: 'center' }} justifyContent="space-between">
        <div>
          <Typography variant="h1" gutterBottom>
            Cost & Revenue Estimation
          </Typography>
          {result && (
            <Typography variant="body1" color="text.secondary">
              {result.projectName} • {result.templateName}
            </Typography>
          )}
        </div>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
          <Button variant="contained" color="primary" onClick={handleRecalculate} disabled={recalculating || !inputs}>
            {recalculating ? 'Recalculating…' : 'Recalculate'}
          </Button>
          <Button variant="outlined" onClick={() => setGoalSeekOpen(true)} disabled={!inputs}>
            Simulate (Goal Seek)
          </Button>
          <Button variant="outlined" onClick={handleDownload} disabled={downloading || !inputs}>
            {downloading ? 'Preparing…' : 'Download as Excel'}
          </Button>
        </Stack>
      </Stack>

      {goalSeekError && (
        <Alert severity="error" onClose={() => setGoalSeekError(null)}>
          {goalSeekError}
        </Alert>
      )}

      {goalSeekResult && (
        <Alert severity={goalSeekResult.converged ? 'success' : 'warning'}>
          Goal seek {goalSeekResult.converged ? 'completed' : 'finished without convergence'} after {goalSeekResult.iterations}{' '}
          iterations.
        </Alert>
      )}

      {layout}

      <Dialog open={goalSeekOpen} onClose={() => setGoalSeekOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Goal Seek Simulation</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 2 }}>
          <FormControl fullWidth size="small">
            <InputLabel id="goal-target-label">Target</InputLabel>
            <Select
              labelId="goal-target-label"
              value={goalSeekState.targetField}
              label="Target"
              onChange={event => setGoalSeekState(prev => ({ ...prev, targetField: event.target.value }))}
            >
              {goalSeekTargets.map(target => (
                <MenuItem key={target.value} value={target.value}>
                  {target.label}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <TextField
            label="Target Value"
            type="number"
            size="small"
            value={goalSeekState.targetValue}
            onChange={handleGoalSeekFieldChange('targetValue')}
          />
          <FormControl fullWidth size="small">
            <InputLabel id="goal-adjustable-label">Adjust By</InputLabel>
            <Select
              labelId="goal-adjustable-label"
              value={goalSeekState.adjustableField}
              label="Adjust By"
              onChange={event => setGoalSeekState(prev => ({ ...prev, adjustableField: event.target.value }))}
            >
              {goalSeekAdjustables.map(option => (
                <MenuItem key={option.value} value={option.value}>
                  {option.label}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              label="Min Value (optional)"
              type="number"
              size="small"
              value={goalSeekState.minValue ?? ''}
              onChange={handleGoalSeekFieldChange('minValue')}
            />
            <TextField
              label="Max Value (optional)"
              type="number"
              size="small"
              value={goalSeekState.maxValue ?? ''}
              onChange={handleGoalSeekFieldChange('maxValue')}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setGoalSeekOpen(false)}>Cancel</Button>
          <Button onClick={handleGoalSeekSubmit} variant="contained">
            Run Simulation
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
