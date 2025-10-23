import { ChangeEvent, useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  CircularProgress,
  Grid,
  IconButton,
  MenuItem,
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
import { apiFetch } from '../../../lib/api';

interface PresalesRole {
  roleName: string;
  expectedLevel: string;
  costPerDay: number;
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

export default function PresalesConfigurationPage() {
  const [config, setConfig] = useState<PresalesConfiguration>(emptyConfig);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const loadConfig = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch('/api/presales/config');
      if (!res.ok) {
        throw new Error(`Failed to load configuration (${res.status})`);
      }
      const data = await res.json();
      setConfig({
        roles: data.roles ?? [],
        activities: data.activities ?? [],
        taskActivities: data.taskActivities ?? [],
        taskRoles: data.taskRoles ?? [],
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load configuration');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadConfig();
  }, [loadConfig]);

  const handleRoleChange = useCallback((index: number, key: keyof PresalesRole, value: string) => {
    setConfig(prev => {
      const roles = [...prev.roles];
      const role = { ...roles[index] };
      if (key === 'costPerDay') {
        role.costPerDay = toNumber(value, 0);
      } else if (key === 'roleName') {
        role.roleName = value;
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

  const addRole = () => setConfig(prev => ({ ...prev, roles: [...prev.roles, { roleName: '', expectedLevel: '', costPerDay: 0 }] }));
  const addActivity = () => setConfig(prev => ({ ...prev, activities: [...prev.activities, { activityName: '', displayOrder: prev.activities.length + 1 }] }));
  const addTaskActivity = () => setConfig(prev => ({ ...prev, taskActivities: [...prev.taskActivities, { taskKey: '', activityName: '' }] }));
  const addTaskRole = () => setConfig(prev => ({ ...prev, taskRoles: [...prev.taskRoles, { taskKey: '', roleName: '', allocationPercentage: 0 }] }));

  const removeRole = (index: number) => setConfig(prev => ({ ...prev, roles: prev.roles.filter((_, i) => i !== index) }));
  const removeActivity = (index: number) => setConfig(prev => ({ ...prev, activities: prev.activities.filter((_, i) => i !== index) }));
  const removeTaskActivity = (index: number) => setConfig(prev => ({ ...prev, taskActivities: prev.taskActivities.filter((_, i) => i !== index) }));
  const removeTaskRole = (index: number) => setConfig(prev => ({ ...prev, taskRoles: prev.taskRoles.filter((_, i) => i !== index) }));

  const handleSave = useCallback(async () => {
    setSaving(true);
    setError(null);
    setSuccessMessage(null);
    try {
      const res = await apiFetch('/api/presales/config', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to save configuration');
      }
      const data = await res.json();
      setConfig({
        roles: data.roles ?? [],
        activities: data.activities ?? [],
        taskActivities: data.taskActivities ?? [],
        taskRoles: data.taskRoles ?? [],
      });
      setSuccessMessage('Configuration saved successfully.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration');
    } finally {
      setSaving(false);
    }
  }, [config]);

  const roleNames = useMemo(() => config.roles.map(role => role.roleName).filter(Boolean), [config.roles]);
  const activityNames = useMemo(() => config.activities.map(activity => activity.activityName).filter(Boolean), [config.activities]);

  return (
    <Box className="page-container">
      <Card className="page-card">
        <CardHeader
          title={<Typography variant="h1">Presales Timeline Configuration</Typography>}
          subheader="Define the master data that drives project timeline generation."
          action={
            <Button variant="contained" color="primary" onClick={handleSave} disabled={saving || loading}>
              {saving ? 'Saving…' : 'Save Changes'}
            </Button>
          }
        />
        <CardContent>
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
                        <TableCell>Cost per Man-day</TableCell>
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
                  <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
                    <Typography variant="h2" className="section-title">Task → Activity Mapping</Typography>
                    <Button startIcon={<AddIcon />} variant="outlined" onClick={addTaskActivity}>Add Mapping</Button>
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
                              <TextField
                                fullWidth
                                size="small"
                                value={mapping.taskKey}
                                onChange={(event: ChangeEvent<HTMLInputElement>) => handleTaskActivityChange(index, 'taskKey', event.target.value)}
                                placeholder="e.g. BE Development"
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
                              <TextField
                                fullWidth
                                size="small"
                                value={mapping.taskKey}
                                onChange={(event: ChangeEvent<HTMLInputElement>) => handleTaskRoleChange(index, 'taskKey', event.target.value)}
                                placeholder="e.g. BE Development"
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
        </CardContent>
      </Card>
    </Box>
  );
}
