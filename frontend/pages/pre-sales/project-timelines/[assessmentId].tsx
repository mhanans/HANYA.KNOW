import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import { Alert, Box, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material';
import { apiFetch } from '../../../lib/api';
import styles from './timeline.module.css'; // Import the CSS module

// --- INTERFACES (Correct based on your JSON) ---
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
const LEFT_PANE_WIDTHS = {
  col1: 200, // Activity
  col2: 280, // Detail
  col3: 150, // Actor
  col4: 90,  // Man-days
};
const TOTAL_LEFT_PANE_WIDTH = Object.values(LEFT_PANE_WIDTHS).reduce((a, b) => a + b, 0);

export default function ProjectTimelineDetailPage() {
  const router = useRouter();
  const { assessmentId } = router.query;
  const [timeline, setTimeline] = useState<AiTimelineResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [regenerating, setRegenerating] = useState(false);

  const resolvedId = useMemo(() => {
    if (Array.isArray(assessmentId)) return parseInt(assessmentId[0] ?? '', 10);
    return assessmentId ? parseInt(assessmentId, 10) : NaN;
  }, [assessmentId]);

  // Data fetching logic remains the same
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
    } finally { setLoading(false); }
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

  const rightPaneWidth = timeline.totalDurationDays * DAY_WIDTH;

  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Box>
          <Typography variant="h1">{timeline.projectName}</Typography>
          <Typography color="text.secondary">Template: {timeline.templateName}</Typography>
        </Box>
        <Stack direction="row" spacing={2}>
          <Button variant="outlined" startIcon={<Box component="span" className="material-symbols-outlined">download</Box>} onClick={handleExport}>Download as Excel</Button>
          <Button variant="contained" onClick={handleRegenerate} disabled={regenerating}>
            {regenerating ? 'Regenerating...' : 'Regenerate Timeline'}
          </Button>
        </Stack>
      </Stack>

      <Paper variant="outlined" sx={{ overflow: 'auto' }}>
        <Box className={styles.timelineGrid} sx={{ minWidth: `${TOTAL_LEFT_PANE_WIDTH + rightPaneWidth}px` }}>
          {/* --- HEADER AREA --- */}
          <div className={styles.headerArea}>
            <div className={styles.headerLeftPane}>
              <div className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col1 }}>Activity</div>
              <div className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col2 }}>Detail</div>
              <div className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col3 }}>Actor</div>
              <div className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col4 }}>Man-days</div>
            </div>
            <div className={styles.headerRightPane} style={{ width: rightPaneWidth }}>
              <div className={styles.headerMonths}>{metrics.months.map(m => (<div key={m.index} style={{ width: m.span * 5 * DAY_WIDTH }}>Month {m.index}</div>))}</div>
              <div className={styles.headerWeeks}>{metrics.weeks.map(w => (<div key={w.index} style={{ width: w.span * DAY_WIDTH }}>W{w.index}</div>))}</div>
              <div className={styles.headerDays}>{metrics.days.map(d => (<div key={d} style={{ width: DAY_WIDTH }}>{d}</div>))}</div>
            </div>
          </div>

          {/* --- GANTT ROWS --- */}
          {timeline.activities.map(activity => (
            <Fragment key={activity.activityName}>
              {activity.details.map((detail, index) => (
                <div key={`${detail.taskName}-${index}`} className={styles.dataRow}>
                  <div className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col1, fontWeight: index === 0 ? 'bold' : 'normal' }}>{index === 0 ? activity.activityName : ''}</div>
                  <div className={styles.dataCell}>{detail.taskName}</div>
                  <div className={styles.dataCell}>{detail.actor}</div>
                  <div className={`${styles.dataCell} ${styles.textCenter}`}>{detail.manDays.toFixed(2)}</div>
                  <div className={styles.barContainer} style={{ width: rightPaneWidth }}>
                    <div className={styles.bar} style={{ left: (detail.startDay - 1) * DAY_WIDTH, width: detail.durationDays * DAY_WIDTH }} />
                  </div>
                </div>
              ))}
            </Fragment>
          ))}
          
          {/* --- SPACER ROW --- */}
          <div className={styles.spacerRow} />
          
          {/* --- RESOURCE HEADER --- */}
          <div className={`${styles.dataRow} ${styles.resourceHeader}`}>
            <div className={`${styles.dataCell} ${styles.headerCell}`}>Role</div>
            <div className={`${styles.dataCell} ${styles.headerCell}`}>Mandays Total</div>
            <div className={styles.dataCell} />
            <div className={styles.dataCell} />
            <div className={styles.barContainer} style={{ width: rightPaneWidth }} />
          </div>

          {/* --- RESOURCE ROWS --- */}
          {timeline.resourceAllocation.map((res, index) => (
            <div key={res.role} className={styles.dataRow}>
              <div className={`${styles.dataCell} ${index % 2 === 0 ? styles.roleYellow : styles.roleBlue}`}>{res.role}</div>
              <div className={`${styles.dataCell} ${styles.textCenter}`}>{res.totalManDays.toFixed(2)}</div>
              <div className={styles.dataCell} />
              <div className={styles.dataCell} />
              <div className={styles.effortContainer} style={{ width: rightPaneWidth }}>
                {res.dailyEffort.map((effort, dayIndex) => (
                  <div key={dayIndex} className={`${styles.effortCell} ${effort > 0 ? (index % 2 === 0 ? styles.effortYellow : styles.effortBlue) : ''}`}>
                    {effort > 0 ? effort : ''}
                  </div>
                ))}
              </div>
            </div>
          ))}
        </Box>
      </Paper>
    </Stack>
  );
}

