import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import { Alert, Box, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material';
import { apiFetch } from '../../../lib/api';
import { useTheme } from '@mui/material/styles';
import styles from './Timeline.module.css';
void styles;

// --- INTERFACES (from your provided JSON) ---
interface TimelineDetail {
  taskName: string;
  actor: string;
  manDays: number;
  startDay: number;
  durationDays: number;
}
interface TimelineActivity {
  activityName: string;
  details: TimelineDetail[];
}
interface ResourceAllocation {
  role: string;
  totalManDays: number;
  dailyEffort: number[];
}
interface AiTimelineResponse {
  totalDurationDays: number;
  activities: TimelineActivity[];
  resourceAllocation: ResourceAllocation[];
  projectName: string;
  templateName: string;
  generatedAt: string;
}

// --- CONSTANTS FOR LAYOUT ---
const DAY_WIDTH = 35;
const ROW_HEIGHT = 40;
const LEFT_PANE_WIDTHS = {
  col1: 200, // Activity
  col2: 280, // Detail
  col3: 150, // Actor
  col4: 90,  // Man-days
};
const TOTAL_LEFT_PANE_WIDTH = Object.values(LEFT_PANE_WIDTHS).reduce((a, b) => a + b, 0);

export default function ProjectTimelineDetailPage() {
  const router = useRouter();
  const theme = useTheme();
  const { assessmentId } = router.query;
  const [timeline, setTimeline] = useState<AiTimelineResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [regenerating, setRegenerating] = useState(false);

  const resolvedId = useMemo(() => {
    if (Array.isArray(assessmentId)) return parseInt(assessmentId[0] ?? '', 10);
    return assessmentId ? parseInt(assessmentId, 10) : NaN;
  }, [assessmentId]);

  const loadTimeline = useCallback(async () => {
    if (!resolvedId) return;
    setLoading(true); setError(null);
    try {
      const res = await apiFetch(`/api/timelines/${resolvedId}`);
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      setTimeline(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load timeline');
    } finally {
      setLoading(false);
    }
  }, [resolvedId]);
  useEffect(() => { loadTimeline(); }, [loadTimeline]);

  const handleRegenerate = useCallback(async () => {
    if (!resolvedId) return;
    setRegenerating(true);
    await apiFetch('/api/timelines', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ assessmentId: resolvedId }),
    });
    setRegenerating(false);
    await loadTimeline();
  }, [resolvedId, loadTimeline]);

  const handleExport = useCallback(() => {
    if (!resolvedId) return;
    const exportUrl = `/api/assessment/${resolvedId}/export`; 
    window.location.href = exportUrl;
  }, [resolvedId]);

  const metrics = useMemo(() => {
    if (!timeline) return null;
    const days = Array.from({ length: timeline.totalDurationDays }, (_, i) => i + 1);
    const weeks: { index: number; span: number }[] = [];
    for (let i = 0; i < days.length; i += 5) {
      weeks.push({ index: weeks.length + 1, span: Math.min(5, days.length - i) });
    }
    const months: { index: number; span: number }[] = [];
    for (let i = 0; i < weeks.length; i += 4) {
      months.push({ index: months.length + 1, span: Math.min(4, weeks.length - i) });
    }
    return { days, weeks, months };
  }, [timeline]);

  if (loading) return <CircularProgress />;
  if (error) return <Alert severity="error">{error}</Alert>;
  if (!timeline || !metrics) return <Alert severity="info">No timeline data available.</Alert>;

  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Box>
          <Typography variant="h1">{timeline.projectName}</Typography>
          <Typography color="text.secondary">Template: {timeline.templateName}</Typography>
        </Box>
        <Stack direction="row" spacing={2}>
          <Button variant="outlined" onClick={handleExport}>Download as Excel</Button>
          <Button variant="contained" onClick={handleRegenerate} disabled={regenerating}>
            {regenerating ? 'Regenerating...' : 'Regenerate Timeline'}
          </Button>
        </Stack>
      </Stack>

      <Paper variant="outlined" sx={{ overflow: 'auto', position: 'relative' }}>
        <Box sx={{ minWidth: `${TOTAL_LEFT_PANE_WIDTH + timeline.totalDurationDays * DAY_WIDTH}px`, position: 'relative' }}>
          
          {/* --- HEADER --- */}
          <Box className="timeline-header">
            <Box className="header-pane-left">
              <div className="header-cell" style={{ width: LEFT_PANE_WIDTHS.col1 }}>Activity</div>
              <div className="header-cell" style={{ width: LEFT_PANE_WIDTHS.col2 }}>Detail</div>
              <div className="header-cell" style={{ width: LEFT_PANE_WIDTHS.col3 }}>Actor</div>
              <div className="header-cell" style={{ width: LEFT_PANE_WIDTHS.col4 }}>Man-days</div>
            </Box>
            <Box className="header-pane-right">
              <div className="header-months">{metrics.months.map(m => (<div key={m.index} style={{ width: m.span * 5 * DAY_WIDTH }}>Month {m.index}</div>))}</div>
              <div className="header-weeks">{metrics.weeks.map(w => (<div key={w.index} style={{ width: w.span * DAY_WIDTH }}>W{w.index}</div>))}</div>
              <div className="header-days">{metrics.days.map(d => (<div key={d} style={{ width: DAY_WIDTH }}>{d}</div>))}</div>
            </Box>
          </Box>

          {/* --- BODY --- */}
          <Box className="timeline-body">
            {/* GANTT ROWS */}
            {timeline.activities.map(activity => (
              <Fragment key={activity.activityName}>
                {activity.details.map((detail, index) => (
                  <div key={`${detail.taskName}-${index}`} className="data-row">
                    <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col1, backgroundColor: theme.palette.background.default, fontWeight: index === 0 ? 'bold' : 'normal' }}>{index === 0 ? activity.activityName : ''}</div>
                    <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col2 }}>{detail.taskName}</div>
                    <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col3 }}>{detail.actor}</div>
                    <div className="data-cell text-center" style={{ width: LEFT_PANE_WIDTHS.col4 }}>{detail.manDays.toFixed(2)}</div>
                    <div className="bar-container">
                      <div className="bar" style={{ left: (detail.startDay - 1) * DAY_WIDTH, width: detail.durationDays * DAY_WIDTH }} />
                    </div>
                  </div>
                ))}
              </Fragment>
            ))}
            
            {/* SPACER ROW */}
            <div className="spacer-row" />
            
            {/* RESOURCE HEADER */}
            <div className="data-row resource-header">
              <div className="data-cell header-cell" style={{ width: LEFT_PANE_WIDTHS.col1 }}>Role</div>
              <div className="data-cell header-cell" style={{ width: LEFT_PANE_WIDTHS.col2 }}>Mandays Total</div>
              <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col3 }} />
              <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col4 }} />
              <div className="bar-container" />
            </div>

            {/* RESOURCE ROWS */}
            {timeline.resourceAllocation.map((res, index) => (
              <div key={res.role} className="data-row">
                <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col1, backgroundColor: index % 2 === 0 ? '#444' : '#333' }}>{res.role}</div>
                <div className="data-cell text-center" style={{ width: LEFT_PANE_WIDTHS.col2 }}>{res.totalManDays.toFixed(2)}</div>
                <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col3 }} />
                <div className="data-cell" style={{ width: LEFT_PANE_WIDTHS.col4 }} />
                <div className="effort-container">
                  {res.dailyEffort.map((effort, dayIndex) => (
                    <div key={dayIndex} className="effort-cell" style={{ backgroundColor: effort > 0 ? (index % 2 === 0 ? '#FFF2CC' : '#DEEBF7') : 'transparent' }}>
                      {effort > 0 ? effort : ''}
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </Box>
        </Box>
      </Paper>
    </Stack>
  );
}
