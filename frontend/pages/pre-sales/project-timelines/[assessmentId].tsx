import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import { Alert, Box, Button, Card, CardContent, CardHeader, CircularProgress, Paper, Stack, Typography } from '@mui/material';
import { apiFetch } from '../../../lib/api';

// --- NEW, DAY-BASED INTERFACES ---
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
const LEFT_PANE_WIDTHS = { activity: 200, detail: 280, actor: 150, manDays: 90 };
const DAY_COLUMN_WIDTH = 35;
const TOTAL_LEFT_PANE_WIDTH = Object.values(LEFT_PANE_WIDTHS).reduce((a, b) => a + b, 0);

export default function ProjectTimelineDetailPage() {
  const router = useRouter();
  const { assessmentId } = router.query;
  const [timeline, setTimeline] = useState<AiTimelineResponse | null>(null);
  // (State for loading, error, regenerating is the same...)
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [regenerating, setRegenerating] = useState(false);

  // (Data fetching logic is the same...)
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

  // --- DERIVED DATA FOR RENDERING ---
  const timelineMetrics = useMemo(() => {
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

  // --- EXPORT TO EXCEL FUNCTION ---
  const handleExport = useCallback(() => {
    if (!resolvedId) return;

    const link = document.createElement('a');
    link.href = `/api/timelines/${resolvedId}/export`;
    link.setAttribute('download', `Timeline_Assessment_${resolvedId}.xlsx`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  }, [resolvedId]);

  // --- MAIN RENDER FUNCTION ---
  if (loading) return <CircularProgress />;
  if (error) return <Alert severity="error">{error}</Alert>;
  if (!timeline || !timelineMetrics) return <Alert severity="info">No timeline data available.</Alert>;
  
  return (
    <Stack spacing={4}>
      <Stack direction="row" justifyContent="space-between">
          <Box>
              <Typography variant="h4">{timeline.projectName}</Typography>
              <Typography color="text.secondary">Template: {timeline.templateName}</Typography>
          </Box>
          <Stack direction="row" spacing={2}>
            <Button variant="outlined" onClick={handleExport}>Download as Excel</Button>
            <Button variant="contained" onClick={handleRegenerate} disabled={regenerating}>
                {regenerating ? 'Regenerating...' : 'Regenerate Timeline'}
            </Button>
          </Stack>
      </Stack>
      
      <Paper variant="outlined" sx={{ overflow: 'auto' }}>
        <Box sx={{ minWidth: `${TOTAL_LEFT_PANE_WIDTH + timeline.totalDurationDays * DAY_COLUMN_WIDTH}px` }}>
          <div className="timeline-grid">
            {/* --- STATIC HEADERS --- */}
            <div className="header-cell sticky-col-1">Activity</div>
            <div className="header-cell sticky-col-2">Detail</div>
            <div className="header-cell sticky-col-3">Actor</div>
            <div className="header-cell sticky-col-4">Man-days</div>
            
            <div className="header-months">
              {timelineMetrics.months.map(m => (<div key={m.index} style={{ width: m.span * 5 * DAY_COLUMN_WIDTH }}>Month {m.index}</div>))}
            </div>
            <div className="header-weeks">
              {timelineMetrics.weeks.map(w => (<div key={w.index} style={{ width: w.span * DAY_COLUMN_WIDTH }}>W{w.index}</div>))}
            </div>
            <div className="header-days">
              {timelineMetrics.days.map(d => (<div key={d}>{d}</div>))}
            </div>

            {/* --- GANTT DATA --- */}
            <div className="data-rows">
              {timeline.activities.map(activity => (
                  <Fragment key={activity.activityName}>
                      {activity.details.map((detail, index) => (
                          <div key={detail.taskName} className="data-row">
                              <div className="data-cell sticky-col-1">{index === 0 ? activity.activityName : ''}</div>
                              <div className="data-cell sticky-col-2">{detail.taskName}</div>
                              <div className="data-cell sticky-col-3">{detail.actor}</div>
                              <div className="data-cell sticky-col-4">{detail.manDays.toFixed(2)}</div>
                              <div className="bar-container">
                                  <div className="bar" style={{ left: (detail.startDay - 1) * DAY_COLUMN_WIDTH, width: detail.durationDays * DAY_COLUMN_WIDTH }} />
                              </div>
                          </div>
                      ))}
                  </Fragment>
              ))}
            </div>

            {/* --- SPACER --- */}
            <div className="spacer-row" />
            
            {/* --- RESOURCE HEADER --- */}
            <div className="resource-header-row">
              <div className="data-cell sticky-col-1">Role</div>
              <div className="data-cell sticky-col-2">Mandays Total</div>
            </div>
            
            {/* --- RESOURCE DATA --- */}
            <div className="resource-rows">
              {timeline.resourceAllocation.map(res => (
                <div key={res.role} className="data-row">
                  <div className="data-cell sticky-col-1">{res.role}</div>
                  <div className="data-cell sticky-col-2">{res.totalManDays.toFixed(2)}</div>
                  <div className="data-cell sticky-col-3" />
                  <div className="data-cell sticky-col-4" />
                  <div className="effort-container">
                    {res.dailyEffort.map((effort, dayIndex) => (
                      <div key={dayIndex} className="effort-cell">
                        {effort > 0 ? effort : ''}
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </Box>
      </Paper>
    </Stack>
  );
}
