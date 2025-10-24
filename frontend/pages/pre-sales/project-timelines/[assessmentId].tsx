import clsx from 'clsx';
import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import { Alert, Box, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material';
import { apiFetch } from '../../../lib/api';
import styles from './timeline.module.css';

// --- INTERFACES & CONSTANTS ---
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

const DAY_WIDTH = 35;
const LEFT_PANE_WIDTHS = { col1: 200, col2: 280, col3: 150, col4: 90 };
const TOTAL_LEFT_PANE_WIDTH = Object.values(LEFT_PANE_WIDTHS).reduce((a, b) => a + b, 0);

export default function ProjectTimelineDetailPage() {
  // --- HOOKS AND HANDLERS (No changes needed) ---
  const router = useRouter();
  const { assessmentId } = router.query;
  const [timeline, setTimeline] = useState<AiTimelineResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);
  const [regenerating, setRegenerating] = useState(false);
  const [exporting, setExporting] = useState(false);

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

  const handleExport = useCallback(async () => {
    if (!resolvedId) return;
    setExportError(null);
    setExporting(true);
    try {
      const res = await apiFetch(`/api/timelines/${resolvedId}/export`);
      if (!res.ok) throw new Error(await res.text());
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `timeline-${resolvedId}.xlsx`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to export the timeline.';
      setExportError(message);
    } finally {
      setExporting(false);
    }
  }, [resolvedId]);

  const metrics = useMemo(() => {
    if (!timeline) return null;
    const days = Array.from({ length: timeline.totalDurationDays }, (_, i) => i + 1);
    const weeks: { index: number; span: number }[] = [];
    for (let dayIndex = 0; dayIndex < days.length; ) {
      const span = Math.min(5, days.length - dayIndex);
      weeks.push({ index: weeks.length + 1, span });
      dayIndex += span;
    }

    const months: { index: number; span: number }[] = [];
    for (let weekIndex = 0; weekIndex < weeks.length; ) {
      const monthWeeks = Math.min(5, weeks.length - weekIndex);
      const span = weeks.slice(weekIndex, weekIndex + monthWeeks).reduce((acc, week) => acc + week.span, 0);
      months.push({ index: months.length + 1, span });
      weekIndex += monthWeeks;
    }

    return { days, weeks, months };
  }, [timeline]);

  const summary = useMemo(() => {
    if (!timeline) return null;

    const formatNumber = (value: number) =>
      new Intl.NumberFormat('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(value);

    const totalManDays = timeline.activities.reduce((activitySum, activity) => {
      const detailSum = activity.details.reduce((sum, detail) => sum + (detail.manDays ?? 0), 0);
      return activitySum + detailSum;
    }, 0);

    return {
      totalManDays,
      formatNumber,
    };
  }, [timeline]);

  if (loading) {
    return (
      <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', justifyContent: 'center' }}>
        <CircularProgress />
      </Box>
    );
  }
  if (error) {
    return (
      <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6 }}>
        <Alert severity="error">{error}</Alert>
      </Box>
    );
  }
  if (!timeline || !metrics) {
    return (
      <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6 }}>
        <Alert severity="info">No timeline data.</Alert>
      </Box>
    );
  }

  const formatGeneratedAt = (value: string) => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return value;
    return new Intl.DateTimeFormat(undefined, {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(date);
  };

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
            {timeline.projectName}
          </Typography>
          <Typography color="text.secondary">
            Generated from template “{timeline.templateName}” on {formatGeneratedAt(timeline.generatedAt)}
          </Typography>
        </Box>
        <Stack direction="row" spacing={1.5} flexWrap="wrap">
          <Button
            variant="outlined"
            startIcon={<span className="material-symbols-outlined">download</span>}
            onClick={handleExport}
            disabled={exporting}
          >
            {exporting ? 'Preparing…' : 'Download as Excel'}
          </Button>
          <Button variant="contained" onClick={handleRegenerate} disabled={regenerating}>
            {regenerating ? 'Regenerating…' : 'Regenerate Timeline'}
          </Button>
        </Stack>
      </Stack>
      {exportError && <Alert severity="error">{exportError}</Alert>}

      <Paper variant="outlined" sx={{ overflow: 'auto', width: '100%', bgcolor: 'background.paper', borderRadius: 3 }}>
        <table
          className={styles.timelineTable}
          style={{ minWidth: TOTAL_LEFT_PANE_WIDTH + timeline.totalDurationDays * DAY_WIDTH }}
        >
          <colgroup>
            <col style={{ width: `${LEFT_PANE_WIDTHS.col1}px` }} />
            <col style={{ width: `${LEFT_PANE_WIDTHS.col2}px` }} />
            <col style={{ width: `${LEFT_PANE_WIDTHS.col3}px` }} />
            <col style={{ width: `${LEFT_PANE_WIDTHS.col4}px` }} />
            {metrics.days.map(day => (
              <col key={`day-col-${day}`} style={{ width: `${DAY_WIDTH}px` }} />
            ))}
          </colgroup>
          <thead>
            {/* Row 1: Months */}
            <tr>
              <th rowSpan={3} className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col1 }}>Activity</th>
              <th rowSpan={3} className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col2 }}>Detail</th>
              <th rowSpan={3} className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col3 }}>Actor</th>
              <th rowSpan={3} className={styles.headerCell} style={{ width: LEFT_PANE_WIDTHS.col4 }}>Man-days</th>
              {metrics.months.map(m => (
                <th key={m.index} colSpan={m.span} className={styles.headerMonth}>{`Month ${m.index}`}</th>
              ))}
            </tr>
            {/* Row 2: Weeks */}
            <tr>
              {metrics.weeks.map(w => (
                <th key={w.index} colSpan={w.span} className={styles.headerWeek}>{`W${w.index}`}</th>
              ))}
            </tr>
            {/* Row 3: Days */}
            <tr>
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
                    {index === 0 && (
                      <td
                        rowSpan={activity.details.length}
                        className={clsx(styles.dataCell, styles.activityGroup)}
                        style={{ width: LEFT_PANE_WIDTHS.col1 }}
                      >
                        {activity.activityName}
                      </td>
                    )}
                    <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col2 }}>
                      {detail.taskName}
                    </td>
                    <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col3 }}>
                      {detail.actor}
                    </td>
                    <td className={clsx(styles.dataCell, styles.textRight)} style={{ width: LEFT_PANE_WIDTHS.col4 }}>
                      {summary?.formatNumber(detail.manDays ?? 0)}
                    </td>
                    {metrics.days.map(day => {
                      const isActive = day >= detail.startDay && day < detail.startDay + detail.durationDays;
                      return <td key={day} className={clsx(styles.timelineCell, isActive && styles.ganttBar)} />;
                    })}
                  </tr>
                ))}
              </Fragment>
            ))}

            <tr className={styles.spacerRow}>
              <td colSpan={4 + timeline.totalDurationDays} />
            </tr>

            <tr>
              <td colSpan={3} className={clsx(styles.dataCell, styles.boldCell)}>
                Mandays Total
              </td>
              <td className={clsx(styles.dataCell, styles.boldCell, styles.textRight)}>
                {summary ? summary.formatNumber(summary.totalManDays) : '0'}
              </td>
              {metrics.days.map(day => (
                <td key={`summary-total-${day}`} className={styles.timelineCell} />
              ))}
            </tr>

            <tr className={styles.summarySeparatorRow}>
              <td colSpan={4 + metrics.days.length} />
            </tr>

            <tr>
              <td colSpan={3} className={clsx(styles.dataCell, styles.summaryHeader)}>
                Role
              </td>
              <td className={clsx(styles.dataCell, styles.summaryHeader)}>Mandays Total</td>
              {metrics.days.map(day => (
                <td key={`summary-header-${day}`} className={styles.timelineCell} />
              ))}
            </tr>

            {timeline.resourceAllocation.map((res, index) => (
              <tr key={res.role}>
                <td
                  colSpan={3}
                  className={clsx(
                    styles.dataCell,
                    styles.summaryRole,
                    index % 2 === 0 ? styles.roleYellow : styles.roleBlue
                  )}
                >
                  {res.role}
                </td>
                <td className={clsx(styles.dataCell, styles.textRight, styles.summaryTotal)}>
                  {summary?.formatNumber(res.totalManDays ?? 0)}
                </td>
                {metrics.days.map((day, dayIndex) => {
                  const effort = res.dailyEffort?.[dayIndex] ?? 0;
                  return (
                    <td
                      key={`${res.role}-day-${day}`}
                      className={clsx(
                        styles.resourceCell,
                        effort > 0 && (index % 2 === 0 ? styles.effortYellow : styles.effortBlue)
                      )}
                    >
                      {effort > 0 ? summary?.formatNumber(effort) : ''}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </Paper>
    </Box>
  );
}
