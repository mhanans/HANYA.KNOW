import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import { Alert, Box, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material';
import { apiFetch } from '../../../lib/api';
import styles from './timeline.module.css';

// --- INTERFACES ---
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

// --- CONSTANTS ---
const DAY_WIDTH = 35;
const LEFT_PANE_WIDTHS = {
  col1: 200,
  col2: 280,
  col3: 150,
  col4: 90,
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

  const loadTimeline = useCallback(async () => {
    if (!resolvedId) return;
    setLoading(true);
    setError(null);
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

  useEffect(() => {
    loadTimeline();
  }, [loadTimeline]);

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
  if (!timeline || !metrics) return <Alert severity="info">No timeline data.</Alert>;

  return (
    <Stack spacing={3}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Box>
          <Typography variant="h1">{timeline.projectName}</Typography>
          <Typography color="text.secondary">Template: {timeline.templateName}</Typography>
        </Box>
        <Stack direction="row" spacing={2}>
          <Button
            variant="outlined"
            startIcon={<span className="material-symbols-outlined">download</span>}
            onClick={handleExport}
          >
            Download as Excel
          </Button>
          <Button variant="contained" onClick={handleRegenerate} disabled={regenerating}>
            {regenerating ? 'Regenerating...' : 'Regenerate Timeline'}
          </Button>
        </Stack>
      </Stack>

      <Paper variant="outlined" sx={{ overflow: 'auto', width: '100%' }}>
        <table className={styles.timelineTable} style={{ minWidth: TOTAL_LEFT_PANE_WIDTH + timeline.totalDurationDays * DAY_WIDTH }}>
          <thead>
            <tr>
              <th colSpan={4} className={styles.headerTopLeft} />
              {metrics.months.map(m => (
                <th
                  key={m.index}
                  colSpan={m.span * 5}
                  className={styles.headerMonth}
                  style={{ width: m.span * 5 * DAY_WIDTH }}
                >
                  {`Month ${m.index}`}
                </th>
              ))}
            </tr>
            <tr>
              <th colSpan={4} className={styles.headerTopLeft} />
              {metrics.weeks.map(w => (
                <th
                  key={w.index}
                  colSpan={w.span}
                  className={styles.headerWeek}
                  style={{ width: w.span * DAY_WIDTH }}
                >
                  {`W${w.index}`}
                </th>
              ))}
            </tr>
            <tr>
              <th
                className={styles.headerCell}
                style={{ width: LEFT_PANE_WIDTHS.col1, left: 0 }}
              >
                Activity
              </th>
              <th
                className={styles.headerCell}
                style={{ width: LEFT_PANE_WIDTHS.col2, left: LEFT_PANE_WIDTHS.col1 }}
              >
                Detail
              </th>
              <th
                className={styles.headerCell}
                style={{ width: LEFT_PANE_WIDTHS.col3, left: LEFT_PANE_WIDTHS.col1 + LEFT_PANE_WIDTHS.col2 }}
              >
                Actor
              </th>
              <th
                className={styles.headerCell}
                style={{ width: LEFT_PANE_WIDTHS.col4, left: TOTAL_LEFT_PANE_WIDTH - LEFT_PANE_WIDTHS.col4 }}
              >
                Man-days
              </th>
              {metrics.days.map(d => (
                <th key={d} className={styles.headerDay} style={{ width: DAY_WIDTH }}>
                  {d}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {timeline.activities.map(activity => (
              <Fragment key={activity.activityName}>
                {activity.details.map((detail, index) => (
                  <tr key={`${detail.taskName}-${index}`}>
                    <td
                      className={styles.dataCell}
                      style={{ width: LEFT_PANE_WIDTHS.col1, fontWeight: index === 0 ? 'bold' : 'normal' }}
                    >
                      {index === 0 ? activity.activityName : ''}
                    </td>
                    <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col2 }}>
                      {detail.taskName}
                    </td>
                    <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col3 }}>
                      {detail.actor}
                    </td>
                    <td className={`${styles.dataCell} ${styles.textCenter}`} style={{ width: LEFT_PANE_WIDTHS.col4 }}>
                      {detail.manDays.toFixed(2)}
                    </td>
                    <td colSpan={timeline.totalDurationDays} className={styles.barContainer}>
                      <div
                        className={styles.bar}
                        style={{ left: (detail.startDay - 1) * DAY_WIDTH, width: detail.durationDays * DAY_WIDTH }}
                      />
                    </td>
                  </tr>
                ))}
              </Fragment>
            ))}

            <tr className={styles.spacerRow}>
              <td colSpan={4 + timeline.totalDurationDays} />
            </tr>

            <tr>
              <th
                className={`${styles.headerCell} ${styles.resourceHeader}`}
                style={{ width: LEFT_PANE_WIDTHS.col1 }}
              >
                Role
              </th>
              <th
                className={`${styles.headerCell} ${styles.resourceHeader}`}
                style={{ width: LEFT_PANE_WIDTHS.col2 }}
              >
                Mandays Total
              </th>
              <th className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col3 }} />
              <th className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col4 }} />
              <td colSpan={timeline.totalDurationDays} />
            </tr>

            {timeline.resourceAllocation.map((res, index) => (
              <tr key={res.role}>
                <td
                  className={`${styles.dataCell} ${index % 2 === 0 ? styles.roleYellow : styles.roleBlue}`}
                  style={{ width: LEFT_PANE_WIDTHS.col1 }}
                >
                  {res.role}
                </td>
                <td
                  className={`${styles.dataCell} ${styles.textCenter}`}
                  style={{ width: LEFT_PANE_WIDTHS.col2 }}
                >
                  {res.totalManDays.toFixed(2)}
                </td>
                <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col3 }} />
                <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col4 }} />
                <td colSpan={timeline.totalDurationDays} className={styles.effortContainer}>
                  {res.dailyEffort.map((effort, dayIndex) => (
                    <div
                      key={dayIndex}
                      className={`${styles.effortCell} ${effort > 0 ? (index % 2 === 0 ? styles.effortYellow : styles.effortBlue) : ''}`}
                    >
                      {effort > 0 ? effort : ''}
                    </div>
                  ))}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Paper>
    </Stack>
  );
}

