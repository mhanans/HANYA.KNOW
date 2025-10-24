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

interface TaskActivityMapping {
  taskKey: string;
  activityName: string;
}

interface TaskRoleMapping {
  taskKey: string;
  roleName: string;
  allocationPercentage: number;
}

interface PresalesConfiguration {
  roles: PresalesRole[];
  activities: PresalesActivity[];
  taskActivities: TaskActivityMapping[];
  taskRoles: TaskRoleMapping[];
}

const emptyConfig: PresalesConfiguration = {
  roles: [],
  activities: [],
  taskActivities: [],
  taskRoles: [],
};

const toNumber = (value: string, fallback = 0) => {
  const parsed = parseFloat(value);
  return Number.isFinite(parsed) ? parsed : fallback;
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
  const [availableTasks, setAvailableTasks] = useState<string[]>([]);
  const [syncingTasks, setSyncingTasks] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const fetchTasks = useCallback(async () => {
    try {
      const res = await apiFetch('/api/presales/config/tasks');
      if (!res.ok) {
        throw new Error(`Failed to load tasks (${res.status})`);
      }
      const data = await res.json();
      setAvailableTasks(Array.isArray(data) ? data : []);
    } catch (err) {
      console.warn('Failed to load task list', err);
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
        taskActivities: presalesData.taskActivities ?? [],
        taskRoles: presalesData.taskRoles ?? [],
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
    fetchTasks();
  }, [fetchTasks]);

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

  const handleTaskActivityChange = useCallback((index: number, key: keyof TaskActivityMapping, value: string) => {
    setConfig(prev => {
      const taskActivities = [...prev.taskActivities];
      const mapping = { ...taskActivities[index] };
      if (key === 'taskKey') {
        mapping.taskKey = value;
      } else if (key === 'activityName') {
        mapping.activityName = value;
      }
      taskActivities[index] = mapping;
      return { ...prev, taskActivities };
    });
  }, []);

  const handleTaskRoleChange = useCallback((index: number, key: keyof TaskRoleMapping, value: string) => {
    setConfig(prev => {
      const taskRoles = [...prev.taskRoles];
      const mapping = { ...taskRoles[index] };
      if (key === 'allocationPercentage') {
        mapping.allocationPercentage = Math.max(0, toNumber(value, 0));
      } else if (key === 'taskKey') {
        mapping.taskKey = value;
      } else if (key === 'roleName') {
        mapping.roleName = value;
      }
      taskRoles[index] = mapping;
      return { ...prev, taskRoles };
    });
  }, []);

  const addRole = () =>
    setConfig(prev => ({
      ...prev,
      roles: [...prev.roles, { roleName: '', expectedLevel: '', costPerDay: 0, monthlySalary: 0, ratePerDay: 0 }],
    }));
  const addActivity = () => setConfig(prev => ({ ...prev, activities: [...prev.activities, { activityName: '', displayOrder: prev.activities.length + 1 }] }));
  const addTaskActivity = () => setConfig(prev => ({ ...prev, taskActivities: [...prev.taskActivities, { taskKey: '', activityName: '' }] }));
  const addTaskRole = () => setConfig(prev => ({ ...prev, taskRoles: [...prev.taskRoles, { taskKey: '', roleName: '', allocationPercentage: 0 }] }));

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
  const removeTaskActivity = (index: number) => setConfig(prev => ({ ...prev, taskActivities: prev.taskActivities.filter((_, i) => i !== index) }));
  const removeTaskRole = (index: number) => setConfig(prev => ({ ...prev, taskRoles: prev.taskRoles.filter((_, i) => i !== index) }));

  const handleSyncTasks = useCallback(async () => {
    setSyncingTasks(true);
    try {
      await fetchTasks();
    } finally {
      setSyncingTasks(false);
    }
  }, [fetchTasks]);

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
        taskActivities: presalesData.taskActivities ?? [],
        taskRoles: presalesData.taskRoles ?? [],
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
                        <TableCell>Timeline Cost / Day</TableCell>
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
                            type="number"
                            value={role.costPerDay}
                            inputProps={{ min: 0, step: 50 }}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleChange(index, 'costPerDay', event.target.value)}
                          />
                        </TableCell>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            type="number"
                            value={role.monthlySalary ?? 0}
                            inputProps={{ min: 0, step: 500000 }}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleSalaryChange(index, event.target.value)}
                          />
                        </TableCell>
                        <TableCell>
                          <TextField
                            fullWidth
                            size="small"
                            type="number"
                            value={role.ratePerDay ?? 0}
                            inputProps={{ min: 0, step: 50000 }}
                            onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleRateChange(index, event.target.value)}
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
                  <Typography variant="h2" className="section-title">Task → Activity Mapping</Typography>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Button
                      startIcon={<SyncIcon />}
                      variant="outlined"
                      onClick={handleSyncTasks}
                      disabled={syncingTasks || loading}
                    >
                      {syncingTasks ? 'Syncing…' : 'Sync Tasks'}
                    </Button>
                    <Button startIcon={<AddIcon />} variant="outlined" onClick={addTaskActivity}>Add Mapping</Button>
                  </Stack>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Task Key</TableCell>
                        <TableCell>Activity</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.taskActivities.map((mapping, index) => (
                        <TableRow key={index}>
                          <TableCell>
                            <Autocomplete
                              freeSolo
                              options={availableTasks}
                              value={mapping.taskKey || ''}
                              onInputChange={(_, newValue) => handleTaskActivityChange(index, 'taskKey', newValue)}
                              onChange={(_, newValue) => handleTaskActivityChange(index, 'taskKey', newValue ?? '')}
                              renderInput={params => (
                                <TextField
                                  {...params}
                                  fullWidth
                                  size="small"
                                  placeholder="e.g. BE Development"
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
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleTaskActivityChange(index, 'activityName', event.target.value)}
                            >
                              {activityNames.length === 0 && <MenuItem value="">—</MenuItem>}
                              {activityNames.map(name => (
                                <MenuItem key={name} value={name}>{name}</MenuItem>
                              ))}
                            </TextField>
                          </TableCell>
                          <TableCell align="right">
                            <IconButton onClick={() => removeTaskActivity(index)} aria-label="Remove mapping">
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
                  <Typography variant="h2" className="section-title">Task → Role Allocation</Typography>
                  <Button startIcon={<AddIcon />} variant="outlined" onClick={addTaskRole}>Add Allocation</Button>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Task Key</TableCell>
                        <TableCell>Role</TableCell>
                        <TableCell>Allocation %</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.taskRoles.map((mapping, index) => (
                        <TableRow key={index}>
                          <TableCell>
                            <Autocomplete
                              freeSolo
                              options={availableTasks}
                              value={mapping.taskKey || ''}
                              onInputChange={(_, newValue) => handleTaskRoleChange(index, 'taskKey', newValue)}
                              onChange={(_, newValue) => handleTaskRoleChange(index, 'taskKey', newValue ?? '')}
                              renderInput={params => (
                                <TextField
                                  {...params}
                                  fullWidth
                                  size="small"
                                  placeholder="e.g. BE Development"
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
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleTaskRoleChange(index, 'roleName', event.target.value)}
                            >
                              {roleNames.length === 0 && <MenuItem value="">—</MenuItem>}
                              {roleNames.map(name => (
                                <MenuItem key={name} value={name}>{name}</MenuItem>
                              ))}
                            </TextField>
                          </TableCell>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              inputProps={{ min: 0, max: 100, step: 5 }}
                              value={mapping.allocationPercentage}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleTaskRoleChange(index, 'allocationPercentage', event.target.value)}
                            />
                          </TableCell>
                          <TableCell align="right">
                            <IconButton onClick={() => removeTaskRole(index)} aria-label="Remove allocation">
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
