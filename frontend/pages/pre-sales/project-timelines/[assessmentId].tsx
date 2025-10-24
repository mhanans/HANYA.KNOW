import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  CircularProgress,
  Divider,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { apiFetch } from '../../../lib/api';

interface TimelineDetailRecord {
  taskKey: string;
  detailName: string;
  manDays: number;
  startDayIndex: number;
  durationDays: number;
  startDate: string;
  endDate: string;
}

interface TimelineActivityRecord {
  activityName: string;
  displayOrder: number;
  details: TimelineDetailRecord[];
  manDays: number;
  startDayIndex: number;
  durationDays: number;
}

interface TimelineManpowerSummary {
  roleName: string;
  expectedLevel: string;
  manDays: number;
  costPerDay: number;
  totalCost: number;
}

interface TimelineResourceAllocation {
  roleName: string;
  expectedLevel: string;
  dailyAllocation: number[];
}

interface TimelineRecord {
  assessmentId: number;
  projectName: string;
  templateName: string;
  generatedAt: string;
  startDate: string;
  workingDays: string[];
  activities: TimelineActivityRecord[];
  manpowerSummary: TimelineManpowerSummary[];
  resourceAllocations: TimelineResourceAllocation[];
  totalCost: number;
}

const formatCurrency = (value: number) => {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', currencyDisplay: 'narrowSymbol', maximumFractionDigits: 0 }).format(value);
};

const formatNumber = (value: number, fractionDigits = 1) => {
  return value.toLocaleString(undefined, { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits });
};

const formatDate = (value?: string) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  }).format(date);
};

export default function ProjectTimelineDetailPage() {
  const router = useRouter();
  const { assessmentId } = router.query;
  const [timeline, setTimeline] = useState<TimelineRecord | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [regenerating, setRegenerating] = useState(false);

  const resolvedId = useMemo(() => {
    if (Array.isArray(assessmentId)) return parseInt(assessmentId[0] ?? '', 10);
    return assessmentId ? parseInt(assessmentId, 10) : NaN;
  }, [assessmentId]);

  const loadTimeline = useCallback(async () => {
    if (!resolvedId || Number.isNaN(resolvedId)) return;
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch(`/api/timelines/${resolvedId}`);
      if (res.status === 404) {
        setError('Timeline not found. Generate it from the overview page.');
        setTimeline(null);
        return;
      }
      if (!res.ok) {
        throw new Error(`Failed to load timeline (${res.status})`);
      }
      const data = await res.json();
      setTimeline(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load timeline');
      setTimeline(null);
    } finally {
      setLoading(false);
    }
  }, [resolvedId]);

  useEffect(() => {
    loadTimeline();
  }, [loadTimeline]);

  const handleRegenerate = useCallback(async () => {
    if (!resolvedId || Number.isNaN(resolvedId)) return;
    setRegenerating(true);
    setError(null);
    try {
      const res = await apiFetch('/api/timelines', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ assessmentId: resolvedId }),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to regenerate timeline');
      }
      const data = await res.json();
      setTimeline(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to regenerate timeline');
    } finally {
      setRegenerating(false);
    }
  }, [resolvedId]);

  const weeks = useMemo(() => {
    const workingDays = timeline?.workingDays ?? [];
    const chunks: { index: number; days: string[] }[] = [];
    for (let i = 0; i < workingDays.length; i += 5) {
      chunks.push({ index: chunks.length + 1, days: workingDays.slice(i, i + 5) });
    }
    return chunks;
  }, [timeline?.workingDays]);

  const months = useMemo(() => {
    const result: { index: number; spanWeeks: number }[] = [];
    for (let i = 0; i < weeks.length; i += 4) {
      result.push({ index: result.length + 1, spanWeeks: Math.min(4, weeks.length - i) });
    }
    return result;
  }, [weeks]);

  const dayCount = timeline?.workingDays?.length ?? 0;
  const rightGridTemplate = useMemo(() => `repeat(${Math.max(dayCount, 1)}, minmax(32px, 1fr))`, [dayCount]);

  const headerGeneratedAt = timeline ? formatDate(timeline.generatedAt) : '—';

  return (
    <Box className="page-container">
      <Card className="page-card timeline-card">
        <CardHeader
          title={
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" flexWrap="wrap" gap={2}>
              <div>
                <Typography variant="h1">Project Timeline</Typography>
                <Typography variant="subtitle1" color="text.secondary">
                  {timeline?.projectName ?? 'Loading project…'}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Template: <strong>{timeline?.templateName ?? '—'}</strong>
                </Typography>
              </div>
              <Stack direction="row" spacing={2} alignItems="center">
                <Typography variant="body2" color="text.secondary">
                  Generated: <strong>{headerGeneratedAt}</strong>
                </Typography>
                <Button variant="contained" color="primary" onClick={handleRegenerate} disabled={regenerating || loading}>
                  {regenerating ? 'Regenerating…' : 'Regenerate Timeline'}
                </Button>
              </Stack>
            </Stack>
          }
        />
        <CardContent className="timeline-content">
          {loading ? (
            <Stack direction="row" spacing={2} alignItems="center" justifyContent="center" sx={{ py: 8 }}>
              <CircularProgress size={32} />
              <Typography variant="body1">Building timeline…</Typography>
            </Stack>
          ) : error ? (
            <Alert severity="error" sx={{ mb: 3 }}>{error}</Alert>
          ) : !timeline ? (
            <Alert severity="info">Timeline is not available. Return to the overview to generate it.</Alert>
          ) : (
            <Stack spacing={4}>
              <Box className="gantt-area">
                <div className="timeline-matrix">
                  <div className="timeline-header-left">
                    <div className="header-cell">Activity</div>
                    <div className="header-cell">Detail</div>
                    <div className="header-cell">Man-days</div>
                  </div>
                  <div
                    className="timeline-header-right"
                    style={{ gridTemplateColumns: rightGridTemplate, ['--day-count' as any]: Math.max(dayCount, 1) }}
                  >
                    <div className="month-row">
                      {months.map(month => (
                        <div key={month.index} className="month-cell" style={{ gridColumn: `span ${month.spanWeeks * 5}` }}>
                          Month {month.index}
                        </div>
                      ))}
                    </div>
                    <div className="week-row">
                      {weeks.map(week => (
                        <div key={week.index} className="week-cell" style={{ gridColumn: `span ${week.days.length}` }}>
                          W{week.index}
                        </div>
                      ))}
                    </div>
                    <div className="day-row">
                      {timeline.workingDays.map((day, idx) => {
                        const date = new Date(day);
                        const weekday = date.toLocaleDateString(undefined, { weekday: 'short' });
                        const dayNum = date.getDate();
                        return (
                          <div key={idx} className="day-cell">
                            <span className="day-label">{weekday.toUpperCase()}</span>
                            <span className="day-number">{dayNum}</span>
                          </div>
                        );
                      })}
                    </div>
                  </div>

                  {timeline.activities.map(activity => (
                    <Fragment key={activity.activityName}>
                      <div className="activity-row">
                        <div className="activity-cell activity-name-cell">{activity.activityName}</div>
                        <div className="activity-cell" />
                        <div className="activity-cell mandays-cell">{formatNumber(activity.manDays, 2)}</div>
                      </div>
                      <div
                        className="activity-right-row"
                        style={{ gridTemplateColumns: rightGridTemplate, ['--day-count' as any]: Math.max(dayCount, 1) }}
                      >
                        <div className="timeline-grid-lines" />
                      </div>

                      {activity.details.map(detail => (
                        <Fragment key={detail.taskKey}>
                          <div className="detail-row">
                            <div className="detail-cell" />
                            <div className="detail-cell detail-name-cell">{detail.detailName}</div>
                            <div className="detail-cell mandays-cell">{formatNumber(detail.manDays, 2)}</div>
                          </div>
                          <div
                            className="detail-right-row"
                            style={{ gridTemplateColumns: rightGridTemplate, ['--day-count' as any]: Math.max(dayCount, 1) }}
                          >
                            <div className="timeline-grid-lines" />
                            <div
                              className="timeline-bar"
                              style={{
                                ['--start' as any]: detail.startDayIndex + 1,
                                ['--duration' as any]: detail.durationDays,
                              }}
                            >
                              <span className="timeline-bar-label">{detail.taskKey}</span>
                            </div>
                          </div>
                        </Fragment>
                      ))}
                    </Fragment>
                  ))}
                </div>
              </Box>

              <Divider className="timeline-divider" />

              <Stack direction={{ xs: 'column', lg: 'row' }} spacing={4} alignItems="stretch">
                <Box className="manpower-summary" flex={1}>
                  <Typography variant="h2" className="section-title">Manpower Summary</Typography>
                  <TableContainer className="manpower-table">
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell>Role</TableCell>
                          <TableCell>Level</TableCell>
                          <TableCell align="right">Man-days</TableCell>
                          <TableCell align="right">Cost / Day</TableCell>
                          <TableCell align="right">Total Cost</TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {timeline.manpowerSummary.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={5} align="center" sx={{ py: 4 }}>
                              <Typography variant="body2" color="text.secondary">
                                No manpower data available. Configure task-to-role mapping to populate this table.
                              </Typography>
                            </TableCell>
                          </TableRow>
                        ) : (
                          timeline.manpowerSummary.map(item => (
                            <TableRow key={item.roleName}>
                              <TableCell>{item.roleName}</TableCell>
                              <TableCell>{item.expectedLevel || '—'}</TableCell>
                              <TableCell align="right">{formatNumber(item.manDays, 1)}</TableCell>
                              <TableCell align="right">{formatCurrency(item.costPerDay)}</TableCell>
                              <TableCell align="right">{formatCurrency(item.totalCost)}</TableCell>
                            </TableRow>
                          ))
                        )}
                        <TableRow className="summary-total-row">
                          <TableCell colSpan={4} align="right">
                            <strong>Total Project Cost</strong>
                          </TableCell>
                          <TableCell align="right">
                            <strong>{formatCurrency(timeline.totalCost)}</strong>
                          </TableCell>
                        </TableRow>
                      </TableBody>
                    </Table>
                  </TableContainer>
                </Box>

                <Box className="resource-allocation" flex={2}>
                  <Typography variant="h2" className="section-title">Resource Allocation Grid</Typography>
                  {timeline.resourceAllocations.length === 0 ? (
                    <Box className="resource-empty">
                      <Typography variant="body2" color="text.secondary">
                        No allocation data available. Assign roles to tasks in the configuration screen to visualize staffing levels.
                      </Typography>
                    </Box>
                  ) : (
                    <div className="resource-grid">
                      <div className="resource-header-left">
                        <span>Role</span>
                        <span>Level</span>
                      </div>
                      <div
                        className="resource-header-right"
                        style={{ gridTemplateColumns: rightGridTemplate, ['--day-count' as any]: Math.max(dayCount, 1) }}
                      >
                        {timeline.workingDays.map((day, idx) => {
                          const date = new Date(day);
                          return (
                            <div key={idx} className="resource-day-header">
                              {date.toLocaleDateString(undefined, { weekday: 'short' }).slice(0, 1)}
                            </div>
                          );
                        })}
                      </div>
                      {timeline.resourceAllocations.map(role => (
                        <Fragment key={role.roleName}>
                          <div className="resource-left-row">
                            <span className="resource-role-name">{role.roleName}</span>
                            <span className="resource-role-level">{role.expectedLevel || '—'}</span>
                          </div>
                          <div
                            className="resource-right-row"
                            style={{ gridTemplateColumns: rightGridTemplate, ['--day-count' as any]: Math.max(dayCount, 1) }}
                          >
                            {role.dailyAllocation.map((value, idx) => (
                              <div key={idx} className="resource-cell">
                                {value > 0 ? formatNumber(value, value >= 1 ? 1 : 2) : ''}
                              </div>
                            ))}
                          </div>
                        </Fragment>
                      ))}
                    </div>
                  )}
                </Box>
              </Stack>
            </Stack>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}
