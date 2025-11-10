import { ChangeEvent, useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Grid,
  IconButton,
  MenuItem,
  Paper,
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
import DeleteIcon from '@mui/icons-material/Delete';
import AddIcon from '@mui/icons-material/Add';
import SyncIcon from '@mui/icons-material/Sync';
import { apiFetch } from '../../../lib/api';
import Autocomplete from '@mui/material/Autocomplete';
import { CostEstimationConfiguration } from '../../../types/cost-estimation';

interface PresalesRole {
  roleName: string;
  expectedLevel: string;
  costPerDay: number;
  monthlySalary?: number;
  ratePerDay?: number;
}

interface PresalesActivity {
  activityName: string;
  displayOrder: number;
}

interface ItemActivityMapping {
  itemName: string;
  activityName: string;
}

interface EstimationColumnRoleMapping {
  estimationColumn: string;
  roleName: string;
}

interface PresalesConfiguration {
  roles: PresalesRole[];
  activities: PresalesActivity[];
  itemActivities: ItemActivityMapping[];
  estimationColumnRoles: EstimationColumnRoleMapping[];
}

const emptyConfig: PresalesConfiguration = {
  roles: [],
  activities: [],
  itemActivities: [],
  estimationColumnRoles: [],
};

const toNumber = (value: string, fallback = 0) => {
  if (!value) return fallback;
  const sanitized = value.replace(/[^0-9,.-]/g, '');
  if (!sanitized) return fallback;
  const hasComma = sanitized.includes(',');
  let normalized = sanitized;
  if (hasComma) {
    normalized = sanitized.replace(/\./g, '').replace(/,/g, '.');
  } else {
    const thousandSeparated = /^\d{1,3}(\.\d{3})+$/.test(sanitized);
    if (thousandSeparated) {
      normalized = sanitized.replace(/\./g, '');
    }
  }
  const parsed = parseFloat(normalized);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const formatIDR = (value?: number | null) => {
  if (value === null || value === undefined) return '';
  return new Intl.NumberFormat('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(value);
};

const resolveDefaultRateCardKey = (config?: CostEstimationConfiguration | null) => {
  if (!config) return 'default';
  const keys = Object.keys(config.rateCards ?? {});
  return config.defaultRateCardKey || keys[0] || 'default';
};

const prepareRateCard = (config: CostEstimationConfiguration, key: string) => {
  const rateCards = { ...config.rateCards };
  const existing = rateCards[key];
  const card = existing
    ? { ...existing, roleRates: { ...existing.roleRates } }
    : { displayName: key, roleRates: {} as Record<string, number> };
  rateCards[key] = card;
  return { rateCards, card };
};

export default function PresalesConfigurationPage() {
  const [config, setConfig] = useState<PresalesConfiguration>(emptyConfig);
  const [costConfig, setCostConfig] = useState<CostEstimationConfiguration | null>(null);
  const [availableItems, setAvailableItems] = useState<string[]>([]);
  const [availableEstimationColumns, setAvailableEstimationColumns] = useState<string[]>([]);
  const [syncingReferenceData, setSyncingReferenceData] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const fetchReferenceData = useCallback(async () => {
    try {
      const [itemsRes, columnsRes] = await Promise.all([
        apiFetch('/api/presales/config/items'),
        apiFetch('/api/presales/config/estimation-columns'),
      ]);

      if (itemsRes.ok) {
        try {
          const data = await itemsRes.json();
          setAvailableItems(Array.isArray(data) ? data : []);
        } catch (err) {
          console.warn('Failed to parse item list', err);
          setAvailableItems([]);
        }
      } else {
        console.warn(`Failed to load item list (${itemsRes.status})`);
        setAvailableItems([]);
      }

      if (columnsRes.ok) {
        try {
          const data = await columnsRes.json();
          setAvailableEstimationColumns(Array.isArray(data) ? data : []);
        } catch (err) {
          console.warn('Failed to parse estimation column list', err);
          setAvailableEstimationColumns([]);
        }
      } else {
        console.warn(`Failed to load estimation column list (${columnsRes.status})`);
        setAvailableEstimationColumns([]);
      }
    } catch (err) {
      console.warn('Failed to load reference data', err);
    }
  }, []);

  const loadConfig = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [presalesRes, costRes] = await Promise.all([
        apiFetch('/api/presales/config'),
        apiFetch('/api/cost-estimations/configuration'),
      ]);
      if (!presalesRes.ok) {
        throw new Error(`Failed to load configuration (${presalesRes.status})`);
      }

      const presalesData = await presalesRes.json();
      let costData: CostEstimationConfiguration | null = null;
      if (costRes.ok) {
        try {
          const json = await costRes.json();
          costData = json ?? null;
        } catch (err) {
          console.warn('Failed to parse cost configuration', err);
        }
      }

      let normalizedCost: CostEstimationConfiguration | null = null;
      if (costData) {
        const key = resolveDefaultRateCardKey(costData);
        const { rateCards } = prepareRateCard(costData, key);
        normalizedCost = {
          ...costData,
          defaultRateCardKey: key,
          rateCards,
          roleMonthlySalaries: { ...costData.roleMonthlySalaries },
        };
      }

      const activeRateCardKey = resolveDefaultRateCardKey(normalizedCost);
      const activeRateCard = normalizedCost?.rateCards?.[activeRateCardKey];

      setConfig({
        roles: (presalesData.roles ?? []).map((role: PresalesRole) => {
          const name = role.roleName?.trim() ?? '';
          return {
            ...role,
            monthlySalary: name && normalizedCost ? normalizedCost.roleMonthlySalaries?.[name] ?? 0 : role.monthlySalary ?? 0,
            ratePerDay: name && activeRateCard ? activeRateCard.roleRates?.[name] ?? 0 : role.ratePerDay ?? 0,
          };
        }),
        activities: presalesData.activities ?? [],
        itemActivities: presalesData.itemActivities ?? [],
        estimationColumnRoles: presalesData.estimationColumnRoles ?? [],
      });
      setCostConfig(normalizedCost);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load configuration');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadConfig();
  }, [loadConfig]);

  useEffect(() => {
    fetchReferenceData();
  }, [fetchReferenceData]);

  const handleRoleChange = useCallback((index: number, key: keyof PresalesRole, value: string) => {
    setConfig(prev => {
      const roles = [...prev.roles];
      const role = { ...roles[index] };
      if (key === 'costPerDay') {
        role.costPerDay = toNumber(value, 0);
      } else if (key === 'roleName') {
        const oldName = role.roleName?.trim() ?? '';
        role.roleName = value;
        const newName = value.trim();
        const salary = role.monthlySalary ?? 0;
        const rate = role.ratePerDay ?? 0;
        setCostConfig(current => {
          if (!current) return current;
          const roleMonthlySalaries = { ...current.roleMonthlySalaries };
          if (oldName) {
            delete roleMonthlySalaries[oldName];
          }
          if (newName) {
            roleMonthlySalaries[newName] = salary;
          }
          const keyName = resolveDefaultRateCardKey(current);
          const { rateCards, card } = prepareRateCard(current, keyName);
          const roleRates = { ...card.roleRates };
          if (oldName) {
            delete roleRates[oldName];
          }
          if (newName) {
            roleRates[newName] = rate;
          }
          rateCards[keyName] = { ...card, roleRates };
          return { ...current, roleMonthlySalaries, rateCards, defaultRateCardKey: keyName };
        });
      } else if (key === 'expectedLevel') {
        role.expectedLevel = value;
      }
      roles[index] = role;
      return { ...prev, roles };
    });
  }, []);

  const handleActivityChange = useCallback((index: number, key: keyof PresalesActivity, value: string) => {
    setConfig(prev => {
      const activities = [...prev.activities];
      const activity = { ...activities[index] };
      if (key === 'displayOrder') {
        activity.displayOrder = Math.max(1, Math.round(toNumber(value, 1)));
      } else if (key === 'activityName') {
        activity.activityName = value;
      }
      activities[index] = activity;
      return { ...prev, activities };
    });
  }, []);

  const handleRoleSalaryChange = useCallback((index: number, value: string) => {
    const salary = toNumber(value, 0);
    setConfig(prev => {
      const roles = [...prev.roles];
      const role = { ...roles[index] };
      role.monthlySalary = salary;
      roles[index] = role;
      const name = role.roleName?.trim();
      if (name) {
        setCostConfig(current => {
          if (!current) return current;
          return {
            ...current,
            roleMonthlySalaries: { ...current.roleMonthlySalaries, [name]: salary },
          };
        });
      }
      return { ...prev, roles };
    });
  }, []);

  const handleRoleRateChange = useCallback((index: number, value: string) => {
    const rate = toNumber(value, 0);
    setConfig(prev => {
      const roles = [...prev.roles];
      const role = { ...roles[index] };
      role.ratePerDay = rate;
      roles[index] = role;
      const name = role.roleName?.trim();
      if (name) {
        setCostConfig(current => {
          if (!current) return current;
          const keyName = resolveDefaultRateCardKey(current);
          const { rateCards, card } = prepareRateCard(current, keyName);
          const roleRates = { ...card.roleRates };
          if (rate > 0) {
            roleRates[name] = rate;
          } else {
            delete roleRates[name];
          }
          rateCards[keyName] = { ...card, roleRates };
          return { ...current, rateCards, defaultRateCardKey: keyName };
        });
      }
      return { ...prev, roles };
    });
  }, []);

  const handleItemActivityChange = useCallback((index: number, key: keyof ItemActivityMapping, value: string) => {
    setConfig(prev => {
      const itemActivities = [...prev.itemActivities];
      const mapping = { ...itemActivities[index] };
      if (key === 'itemName') {
        mapping.itemName = value;
      } else if (key === 'activityName') {
        mapping.activityName = value;
      }
      itemActivities[index] = mapping;
      return { ...prev, itemActivities };
    });
  }, []);

  const handleEstimationRoleChange = useCallback((index: number, key: 'estimationColumn' | 'roleName', value: string) => {
    setConfig(prev => {
      const estimationColumnRoles = [...prev.estimationColumnRoles];
      const mapping = { ...estimationColumnRoles[index] };
      if (key === 'estimationColumn') {
        mapping.estimationColumn = value;
      } else if (key === 'roleName') {
        mapping.roleName = value;
      }
      estimationColumnRoles[index] = mapping;
      return { ...prev, estimationColumnRoles };
    });
  }, []);

  const addRole = () =>
    setConfig(prev => ({
      ...prev,
      roles: [
        ...prev.roles,
        { roleName: '', expectedLevel: '', costPerDay: 0, monthlySalary: undefined, ratePerDay: undefined },
      ],
    }));
  const addActivity = () => setConfig(prev => ({ ...prev, activities: [...prev.activities, { activityName: '', displayOrder: prev.activities.length + 1 }] }));
  const addItemActivity = () =>
    setConfig(prev => ({
      ...prev,
      itemActivities: [...prev.itemActivities, { itemName: '', activityName: '' }],
    }));
  const addEstimationColumnRole = () =>
    setConfig(prev => ({
      ...prev,
      estimationColumnRoles: [...prev.estimationColumnRoles, { estimationColumn: '', roleName: '' }],
    }));

  const removeRole = (index: number) =>
    setConfig(prev => {
      const roles = prev.roles.filter((_, i) => i !== index);
      const removed = prev.roles[index];
      const name = removed?.roleName?.trim();
      if (name) {
        setCostConfig(current => {
          if (!current) return current;
          const roleMonthlySalaries = { ...current.roleMonthlySalaries };
          delete roleMonthlySalaries[name];
          const keyName = resolveDefaultRateCardKey(current);
          const { rateCards, card } = prepareRateCard(current, keyName);
          const roleRates = { ...card.roleRates };
          delete roleRates[name];
          rateCards[keyName] = { ...card, roleRates };
          return { ...current, roleMonthlySalaries, rateCards, defaultRateCardKey: keyName };
        });
      }
      return { ...prev, roles };
    });
  const removeActivity = (index: number) => setConfig(prev => ({ ...prev, activities: prev.activities.filter((_, i) => i !== index) }));
  const removeItemActivity = (index: number) =>
    setConfig(prev => ({ ...prev, itemActivities: prev.itemActivities.filter((_, i) => i !== index) }));
  const removeEstimationColumnRole = (index: number) =>
    setConfig(prev => ({ ...prev, estimationColumnRoles: prev.estimationColumnRoles.filter((_, i) => i !== index) }));

  const handleSyncReferenceData = useCallback(async () => {
    setSyncingReferenceData(true);
    try {
      await fetchReferenceData();
    } finally {
      setSyncingReferenceData(false);
    }
  }, [fetchReferenceData]);

  const handleSave = useCallback(async () => {
    setSaving(true);
    setError(null);
    setSuccessMessage(null);
    try {
      const presalesResponse = await apiFetch('/api/presales/config', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config),
      });
      if (!presalesResponse.ok) {
        const text = await presalesResponse.text();
        throw new Error(text || 'Failed to save configuration');
      }
      const presalesData = await presalesResponse.json();

      let updatedCost = costConfig;
      if (costConfig) {
        const costResponse = await apiFetch('/api/cost-estimations/configuration', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(costConfig),
        });
        if (!costResponse.ok) {
          const text = await costResponse.text();
          throw new Error(text || 'Failed to save cost configuration');
        }
        const savedCost = await costResponse.json();
        if (savedCost) {
          const keyName = resolveDefaultRateCardKey(savedCost);
          const { rateCards } = prepareRateCard(savedCost, keyName);
          updatedCost = {
            ...savedCost,
            defaultRateCardKey: keyName,
            rateCards,
            roleMonthlySalaries: { ...savedCost.roleMonthlySalaries },
          };
          setCostConfig(updatedCost);
        }
      }

      const activeRateCardKey = resolveDefaultRateCardKey(updatedCost);
      const activeRateCard = updatedCost?.rateCards?.[activeRateCardKey];

      setConfig({
        roles: (presalesData.roles ?? []).map((role: PresalesRole) => {
          const name = role.roleName?.trim() ?? '';
          return {
            ...role,
            monthlySalary: name && updatedCost ? updatedCost.roleMonthlySalaries?.[name] ?? 0 : 0,
            ratePerDay: name && activeRateCard ? activeRateCard.roleRates?.[name] ?? 0 : 0,
          };
        }),
        activities: presalesData.activities ?? [],
        itemActivities: presalesData.itemActivities ?? [],
        estimationColumnRoles: presalesData.estimationColumnRoles ?? [],
      });
      setSuccessMessage('Configuration saved successfully.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration');
    } finally {
      setSaving(false);
    }
  }, [config, costConfig]);

  const roleNames = useMemo(
    () => config.roles.map(role => role.roleName?.trim()).filter((name): name is string => Boolean(name)),
    [config.roles]
  );
  const activityNames = useMemo(
    () => config.activities.map(activity => activity.activityName?.trim()).filter((name): name is string => Boolean(name)),
    [config.activities]
  );

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={2}
        justifyContent="space-between"
        alignItems={{ xs: 'flex-start', sm: 'center' }}
      >
        <Box>
          <Typography variant="h1" gutterBottom>
            Pre-Sales Timeline Configuration
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Define the master data that drives project timeline generation.
          </Typography>
        </Box>
        <Button variant="contained" color="primary" onClick={handleSave} disabled={saving || loading}>
          {saving ? 'Saving…' : 'Save Changes'}
        </Button>
      </Stack>
      <Paper variant="outlined" sx={{ p: 3, bgcolor: 'background.paper', borderRadius: 3 }}>
        {loading ? (
          <Stack direction="row" alignItems="center" spacing={2} justifyContent="center" sx={{ py: 6 }}>
            <CircularProgress size={32} />
            <Typography variant="body1">Loading presales configuration…</Typography>
          </Stack>
        ) : (
          <Stack spacing={4}>
            {error && <Alert severity="error">{error}</Alert>}
            {successMessage && <Alert severity="success">{successMessage}</Alert>}

            <Box>
              <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
                <Typography variant="h2" className="section-title">Roles &amp; Rates</Typography>
                <Button startIcon={<AddIcon />} variant="outlined" onClick={addRole}>Add Role</Button>
              </Stack>
              <TableContainer>
                <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Role Name</TableCell>
                        <TableCell>Expected Level</TableCell>
                        <TableCell>Monthly Salary (IDR)</TableCell>
                        <TableCell>Billing Rate / Day</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                  <TableBody>
                    {config.roles.map((role, index) => (
                      <TableRow key={index}>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            value={role.roleName}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleChange(index, 'roleName', event.target.value)}
                            placeholder="e.g. Architect"
                          />
                        </TableCell>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            value={role.expectedLevel}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleChange(index, 'expectedLevel', event.target.value)}
                            placeholder="e.g. Senior"
                          />
                        </TableCell>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            value={formatIDR(role.monthlySalary)}
                            inputProps={{ inputMode: 'numeric', pattern: '[0-9.,]*' }}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleSalaryChange(index, event.target.value)}
                            placeholder="e.g. 15.000.000"
                          />
                        </TableCell>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            value={formatIDR(role.ratePerDay)}
                            inputProps={{ inputMode: 'numeric', pattern: '[0-9.,]*' }}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleRateChange(index, event.target.value)}
                            placeholder="e.g. 1.500.000"
                          />
                        </TableCell>
                        <TableCell align="right">
                          <IconButton onClick={() => removeRole(index)} aria-label="Remove role">
                            <DeleteIcon />
                          </IconButton>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            </Box>

            <Box>
              <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
                <Typography variant="h2" className="section-title">Activity Groupings</Typography>
                <Button startIcon={<AddIcon />} variant="outlined" onClick={addActivity}>Add Activity</Button>
              </Stack>
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Activity Name</TableCell>
                      <TableCell>Display Order</TableCell>
                      <TableCell align="right">Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {config.activities.map((activity, index) => (
                      <TableRow key={index}>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            value={activity.activityName}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleActivityChange(index, 'activityName', event.target.value)}
                            placeholder="e.g. Analysis & Design"
                          />
                        </TableCell>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            type="number"
                            inputProps={{ min: 1, step: 1 }}
                            value={activity.displayOrder}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleActivityChange(index, 'displayOrder', event.target.value)}
                          />
                        </TableCell>
                        <TableCell align="right">
                          <IconButton onClick={() => removeActivity(index)} aria-label="Remove activity">
                            <DeleteIcon />
                          </IconButton>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            </Box>

            <Grid container spacing={4}>
              <Grid item xs={12} md={6}>
                <Stack
                  direction={{ xs: 'column', sm: 'row' }}
                  justifyContent="space-between"
                  alignItems={{ xs: 'flex-start', sm: 'center' }}
                  spacing={1}
                  sx={{ mb: 2 }}
                >
                  <Typography variant="h2" className="section-title">Item → Activity Mapping</Typography>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Button
                      startIcon={<SyncIcon />}
                      variant="outlined"
                      onClick={handleSyncReferenceData}
                      disabled={syncingReferenceData || loading}
                    >
                      {syncingReferenceData ? 'Syncing…' : 'Sync Reference Data'}
                    </Button>
                    <Button startIcon={<AddIcon />} variant="outlined" onClick={addItemActivity}>Add Mapping</Button>
                  </Stack>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Item Name</TableCell>
                        <TableCell>Activity</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.itemActivities.map((mapping, index) => (
                        <TableRow key={index}>
                          <TableCell>
                            <Autocomplete
                              options={availableItems}
                              value={mapping.itemName || ''}
                              onChange={(_, newValue) => handleItemActivityChange(index, 'itemName', newValue ?? '')}
                              autoHighlight
                              renderInput={params => (
                                <TextField
                                  {...params}
                                  fullWidth
                                  size="small"
                                  placeholder="Select item"
                                />
                              )}
                            />
                          </TableCell>
                          <TableCell>
                            <TextField
                              select
                              fullWidth
                              size="small"
                              value={mapping.activityName}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleItemActivityChange(index, 'activityName', event.target.value)}
                            >
                              {activityNames.length === 0 && <MenuItem value="">—</MenuItem>}
                              {activityNames.map(name => (
                                <MenuItem key={name} value={name}>{name}</MenuItem>
                              ))}
                            </TextField>
                          </TableCell>
                          <TableCell align="right">
                            <IconButton onClick={() => removeItemActivity(index)} aria-label="Remove mapping">
                              <DeleteIcon />
                            </IconButton>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Grid>

              <Grid item xs={12} md={6}>
                <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
                  <Typography variant="h2" className="section-title">Estimation Column → Role Allocation</Typography>
                  <Button startIcon={<AddIcon />} variant="outlined" onClick={addEstimationColumnRole}>Add Allocation</Button>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Estimation Column</TableCell>
                        <TableCell>Role</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.estimationColumnRoles.map((mapping, index) => (
                        <TableRow key={index}>
                          <TableCell>
                            <Autocomplete
                              options={availableEstimationColumns}
                              value={mapping.estimationColumn || ''}
                              onChange={(_, newValue) => handleEstimationRoleChange(index, 'estimationColumn', newValue ?? '')}
                              autoHighlight
                              renderInput={params => (
                                <TextField
                                  {...params}
                                  fullWidth
                                  size="small"
                                  placeholder="Select estimation column"
                                />
                              )}
                            />
                          </TableCell>
                          <TableCell>
                            <TextField
                              select
                              fullWidth
                              size="small"
                              value={mapping.roleName}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleEstimationRoleChange(index, 'roleName', event.target.value)}
                            >
                              {roleNames.length === 0 && <MenuItem value="">—</MenuItem>}
                              {roleNames.map(name => (
                                <MenuItem key={name} value={name}>{name}</MenuItem>
                              ))}
                            </TextField>
                          </TableCell>
                          <TableCell align="right">
                            <IconButton onClick={() => removeEstimationColumnRole(index)} aria-label="Remove allocation">
                              <DeleteIcon />
                            </IconButton>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Grid>
            </Grid>
          </Stack>
        )}
      </Paper>
    </Box>
  );
}
