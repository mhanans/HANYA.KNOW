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
  Paper,
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

// --- INTERFACES (No changes needed here) ---
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

// --- HELPER FUNCTIONS (No changes needed here) ---
const formatCurrency = (value: number) => {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', currencyDisplay: 'narrowSymbol', maximumFractionDigits: 0 }).format(value);
};
const formatNumber = (value: number, fractionDigits = 2) => {
  return value.toLocaleString(undefined, { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits });
};
const formatDate = (value?: string) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { day: '2-digit', month: 'short', year: 'numeric' }).format(date);
};

// --- CONSTANTS ---
const LEFT_PANE_WIDTHS = { activity: 210, detail: 260, manDays: 90 };
const DAY_COLUMN_WIDTH = 40;

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

  // --- DATA FETCHING LOGIC (No changes needed here) ---
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
      if (!res.ok) throw new Error(`Failed to load timeline (${res.status})`);
      const data = await res.json();
      setTimeline(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load timeline');
      setTimeline(null);
    } finally {
      setLoading(false);
    }
  }, [resolvedId]);

  useEffect(() => { loadTimeline(); }, [loadTimeline]);

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
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      setTimeline(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to regenerate timeline');
    } finally {
      setRegenerating(false);
    }
  }, [resolvedId]);

  // --- DERIVED DATA (No changes needed here) ---
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
  const headerGeneratedAt = timeline ? formatDate(timeline.generatedAt) : '—';
  
  // --- MAIN RENDER FUNCTION (COMPLETE REPLACEMENT) ---
  return (
    <Box>
      <Card>
        <CardHeader
          title={
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" flexWrap="wrap" gap={2}>
              <div>
                <Typography variant="h1">Project Timeline</Typography>
                <Typography variant="subtitle1" color="text.secondary">{timeline?.projectName ?? 'Loading project…'}</Typography>
                <Typography variant="body2" color="text.secondary">Template: <strong>{timeline?.templateName ?? '—'}</strong></Typography>
              </div>
              <Stack direction="row" spacing={2} alignItems="center">
                <Typography variant="body2" color="text.secondary">Generated: <strong>{headerGeneratedAt}</strong></Typography>
                <Button variant="contained" color="primary" onClick={handleRegenerate} disabled={regenerating || loading}>
                  {regenerating ? 'Regenerating…' : 'Regenerate Timeline'}
                </Button>
              </Stack>
            </Stack>
          }
        />
        <CardContent>
          {loading ? (
            <Stack direction="row" spacing={2} alignItems="center" justifyContent="center" sx={{ py: 8 }}>
              <CircularProgress size={32} />
              <Typography>Building timeline…</Typography>
            </Stack>
          ) : error ? (
            <Alert severity="error">{error}</Alert>
          ) : !timeline ? (
            <Alert severity="info">Timeline is not available.</Alert>
          ) : (
            <Stack spacing={4}>
              {/* --- GANTT CHART AREA (NEW STRUCTURE) --- */}
              <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
                <Box sx={{ minWidth: `${LEFT_PANE_WIDTHS.activity + LEFT_PANE_WIDTHS.detail + LEFT_PANE_WIDTHS.manDays + dayCount * DAY_COLUMN_WIDTH}px` }}>
                  {/* --- Header Row --- */}
                  <Box sx={{ display: 'flex', position: 'sticky', top: 0, zIndex: 10 }}>
                    <Box sx={{ display: 'flex', flexShrink: 0, borderBottom: 1, borderColor: 'divider' }}>
                      <Typography className="header-cell" sx={{ width: LEFT_PANE_WIDTHS.activity }}>Activity</Typography>
                      <Typography className="header-cell" sx={{ width: LEFT_PANE_WIDTHS.detail }}>Detail</Typography>
                      <Typography className="header-cell" sx={{ width: LEFT_PANE_WIDTHS.manDays }}>Man-days</Typography>
                    </Box>
                    <Box sx={{ flexGrow: 1, display: 'grid' }}>
                      <Box className="month-row">
                        {months.map(m => (<Box key={m.index} className="month-cell" sx={{ width: m.spanWeeks * 5 * DAY_COLUMN_WIDTH }}>Month {m.index}</Box>))}
                      </Box>
                      <Box className="week-row">
                        {weeks.map(w => (<Box key={w.index} className="week-cell" sx={{ width: w.days.length * DAY_COLUMN_WIDTH }}>W{w.index}</Box>))}
                      </Box>
                      <Box className="day-row">
                        {timeline.workingDays.map((day, idx) => (
                          <Box key={idx} className="day-cell" sx={{ width: DAY_COLUMN_WIDTH }}>
                            <Typography variant="caption" component="span" className="day-label">{new Date(day).toLocaleDateString(undefined, { weekday: 'short' }).toUpperCase()}</Typography>
                            <Typography variant="caption" component="span">{new Date(day).getDate()}</Typography>
                          </Box>
                        ))}
                      </Box>
                    </Box>
                  </Box>

                  {/* --- Data Rows --- */}
                  {timeline.activities.map(activity => (
                    <Fragment key={activity.activityName}>
                      <Box sx={{ display: 'flex' }} className="activity-data-row">
                        <Box sx={{ display: 'flex', flexShrink: 0 }}>
                          <Typography className="data-cell activity-name-cell" sx={{ width: LEFT_PANE_WIDTHS.activity }}>{activity.activityName}</Typography>
                          <Typography className="data-cell" sx={{ width: LEFT_PANE_WIDTHS.detail }} />
                          <Typography className="data-cell mandays-cell" sx={{ width: LEFT_PANE_WIDTHS.manDays }}>{formatNumber(activity.manDays)}</Typography>
                        </Box>
                        <Box sx={{ flexGrow: 1, position: 'relative' }}>
                          <div className="timeline-grid-lines" style={{ '--day-count': dayCount, '--day-width': `${DAY_COLUMN_WIDTH}px` }} />
                        </Box>
                      </Box>
                      {activity.details.map(detail => (
                        <Box key={detail.taskKey} sx={{ display: 'flex' }} className="detail-data-row">
                          <Box sx={{ display: 'flex', flexShrink: 0 }}>
                            <Typography className="data-cell" sx={{ width: LEFT_PANE_WIDTHS.activity }} />
                            <Typography className="data-cell detail-name-cell" sx={{ width: LEFT_PANE_WIDTHS.detail }}>{detail.detailName}</Typography>
                            <Typography className="data-cell mandays-cell" sx={{ width: LEFT_PANE_WIDTHS.manDays }}>{formatNumber(detail.manDays)}</Typography>
                          </Box>
                          <Box sx={{ flexGrow: 1, position: 'relative' }}>
                            <div className="timeline-grid-lines" style={{ '--day-count': dayCount, '--day-width': `${DAY_COLUMN_WIDTH}px` }} />
                            <Box className="timeline-bar" sx={{
                              left: `${detail.startDayIndex * DAY_COLUMN_WIDTH}px`,
                              width: `${detail.durationDays * DAY_COLUMN_WIDTH}px`,
                            }}>
                              <Typography variant="caption" className="timeline-bar-label">{detail.taskKey}</Typography>
                            </Box>
                          </Box>
                        </Box>
                      ))}
                    </Fragment>
                  ))}
                </Box>
              </Paper>

              <Divider />

              <Stack direction={{ xs: 'column', lg: 'row' }} spacing={4} alignItems="stretch">
                <Paper variant="outlined" sx={{ flex: 1, p: 2 }}>
                  <Typography variant="h6" gutterBottom>Manpower Summary</Typography>
                  <TableContainer>
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
                        {timeline.manpowerSummary.map(item => (
                            <TableRow key={item.roleName}>
                              <TableCell>{item.roleName}</TableCell>
                              <TableCell>{item.expectedLevel || '—'}</TableCell>
                              <TableCell align="right">{formatNumber(item.manDays, 1)}</TableCell>
                              <TableCell align="right">{formatCurrency(item.costPerDay)}</TableCell>
                              <TableCell align="right">{formatCurrency(item.totalCost)}</TableCell>
                            </TableRow>
                        ))}
                        <TableRow sx={{ '& .MuiTableCell-root': { fontWeight: 'bold' } }}>
                          <TableCell colSpan={4} align="right">Total Project Cost</TableCell>
                          <TableCell align="right">{formatCurrency(timeline.totalCost)}</TableCell>
                        </TableRow>
                      </TableBody>
                    </Table>
                  </TableContainer>
                </Paper>
                
                <Paper variant="outlined" sx={{ flex: 2, p: 2, overflowX: 'auto' }}>
                  <Typography variant="h6" gutterBottom>Resource Allocation Grid</Typography>
                  <Box sx={{ minWidth: `${210 + dayCount * DAY_COLUMN_WIDTH}px` }}>
                    {/* Resource Grid implementation would go here, styled similarly */}
                  </Box>
                </Paper>
              </Stack>
            </Stack>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}
