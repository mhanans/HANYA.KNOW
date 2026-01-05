import clsx from 'clsx';
import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import { Alert, Box, Button, CircularProgress, Paper, Stack, Typography, Tabs, Tab } from '@mui/material';
import Swal from 'sweetalert2';
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
  assessmentId: number;
  version: number;
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
  const router = useRouter();
  const { assessmentId } = router.query;
  const [timeline, setTimeline] = useState<AiTimelineResponse | null>(null);
  const [versions, setVersions] = useState<AiTimelineResponse[]>([]);
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);
  const [regenerating, setRegenerating] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [dragging, setDragging] = useState<{
    actIdx: number;
    detIdx: number;
    startX: number;
    origStart: number;
  } | null>(null);

  const resolvedId = useMemo(() => {
    if (Array.isArray(assessmentId)) return parseInt(assessmentId[0] ?? '', 10);
    return assessmentId ? parseInt(assessmentId, 10) : NaN;
  }, [assessmentId]);

  const loadVersions = useCallback(async () => {
    if (!resolvedId) return;
    try {
      const res = await apiFetch(`/api/timelines/${resolvedId}/versions`);
      if (res.ok) {
        const data: AiTimelineResponse[] = await res.json();
        setVersions(data);

        // If no version selected, select the latest
        if (selectedVersion === null && data.length > 0) {
          const maxVer = Math.max(...data.map(d => d.version));
          setSelectedVersion(maxVer);
        }
      }
    } catch (e) {
      console.error("Failed to load versions", e);
    }
  }, [resolvedId, selectedVersion]);

  const loadTimeline = useCallback(async () => {
    if (!resolvedId || selectedVersion === null) return;
    setLoading(true);
    setError(null);
    try {
      // If we already have the data in 'versions', use it instead of refetching
      // But fetching ensures detailed data if list was summary. (Here list is full record currently).
      // For safety, let's fetch to match previous behavior or just find in list.
      // Given the list endpoint returns full records, we can just find it.

      const found = versions.find(v => v.version === selectedVersion);
      if (found) {
        setTimeline(found);
      } else {
        // Fallback fetch
        const res = await apiFetch(`/api/timelines/${resolvedId}?version=${selectedVersion}`);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        setTimeline(data);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load timeline');
    } finally {
      setLoading(false);
    }
  }, [resolvedId, selectedVersion, versions]);

  useEffect(() => {
    loadVersions();
  }, [loadVersions]);

  useEffect(() => {
    loadTimeline();
  }, [loadTimeline]);

  const handleTabChange = (event: React.SyntheticEvent, newValue: number) => {
    if (dirty) {
      Swal.fire({
        title: "Unsaved Changes",
        text: "You have unsaved changes. Switch version anyway?",
        showCancelButton: true,
        confirmButtonText: "Switch"
      }).then((result) => {
        if (result.isConfirmed) {
          setDirty(false);
          setIsEditing(false);
          setSelectedVersion(newValue);
        }
      });
    } else {
      setSelectedVersion(newValue);
    }
  };

  const handleReset = useCallback(async () => {
    if (!resolvedId) return;

    const result = await Swal.fire({
      title: 'Reset Timeline?',
      text: "This will permanently delete the current timeline and return you to the list.",
      icon: 'warning',
      showCancelButton: true,
      confirmButtonColor: '#d33',
      cancelButtonColor: '#3085d6',
      confirmButtonText: 'Yes, reset it!'
    });

    if (!result.isConfirmed) return;

    setRegenerating(true);
    try {
      const res = await apiFetch(`/api/timelines/${resolvedId}`, {
        method: 'DELETE',
      });
      if (!res.ok) throw new Error(await res.text());

      await Swal.fire(
        'Reset!',
        'Your timeline has been reset.',
        'success'
      );
      router.push('/pre-sales/project-timelines');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to reset timeline');
      setRegenerating(false);
    }
  }, [resolvedId, router]);

  const handleExport = useCallback(async () => {
    if (!resolvedId || selectedVersion === null) return;
    setExportError(null);
    setExporting(true);
    try {
      const res = await apiFetch(`/api/timelines/${resolvedId}/export?version=${selectedVersion}`);
      if (!res.ok) throw new Error(await res.text());
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `timeline-${resolvedId}-v${selectedVersion}.xlsx`;
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
  }, [resolvedId, selectedVersion]);

  const handleSave = useCallback(async () => {
    if (!resolvedId || !timeline) return;
    try {
      setLoading(true);
      const res = await apiFetch(`/api/timelines/${resolvedId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(timeline)
      });
      if (!res.ok) throw new Error(await res.text());

      const newRecord: AiTimelineResponse = await res.json();

      setDirty(false);
      setIsEditing(false); // Exit edit mode

      // Update versions list and switch to new version
      await loadVersions();
      setSelectedVersion(newRecord.version);

      Swal.fire("Saved", `Timeline saved as Version ${newRecord.version}`, "success");

    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed');
    } finally {
      setLoading(false);
    }
  }, [resolvedId, timeline, loadVersions]);

  const handleDragStart = (e: React.MouseEvent, actIdx: number, detIdx: number, detail: TimelineDetail) => {
    if (!isEditing) return; // Prevent drag if not editing
    e.preventDefault();
    setDragging({
      actIdx,
      detIdx,
      startX: e.clientX,
      origStart: detail.startDay
    });
  };

  const handleRemoveEmptyColumns = useCallback(() => {
    if (!timeline) return;

    setTimeline(prev => {
      if (!prev) return null;

      const newActs = JSON.parse(JSON.stringify(prev.activities));
      const maxDay = prev.totalDurationDays;

      // 1. Mark occupied days
      const occupied = new Set<number>();
      newActs.forEach((a: TimelineActivity) => {
        a.details.forEach(d => {
          for (let i = 0; i < d.durationDays; i++) {
            occupied.add(d.startDay + i);
          }
        });
      });

      // 2. Identify empty days and mapping
      // mapping[originalDay] = newDay
      const mapping = new Map<number, number>();
      let shift = 0;
      let newMaxEnd = 0;

      for (let d = 1; d <= maxDay; d++) {
        if (!occupied.has(d)) {
          // This day is empty, so we increase the shift for subsequent days
          shift++;
        } else {
          // This day is occupied, so it maps to (d - shift)
          mapping.set(d, d - shift);
          newMaxEnd = Math.max(newMaxEnd, d - shift);
        }
      }

      if (shift === 0) return prev; // No gaps found

      // 3. Update all tasks
      newActs.forEach((a: TimelineActivity) => {
        a.details.forEach(d => {
          const oldStart = d.startDay;
          if (mapping.has(oldStart)) {
            d.startDay = mapping.get(oldStart)!;
          } else {
            // Fallback if start day was somehow in a gap (shouldn't happen if logic holds, 
            // but if a task started in a gap but had duration 0? Unlikely).
            // Just shift it by the count of gaps before it.
            // Actually, if a task starts on day X, and day X is empty, it means duration is 0? 
            // Logic above adds `d.startDay ... + duration`. 
            // If duration > 0, startDay IS occupied.
          }
        });
      });

      return { ...prev, activities: newActs, totalDurationDays: Math.max(0, newMaxEnd) };
    });
    setDirty(true);
  }, [timeline]);

  useEffect(() => {
    const onMove = (e: MouseEvent) => {
      if (!dragging || !timeline) return;

      const deltaPx = e.clientX - dragging.startX;
      const deltaDays = Math.round(deltaPx / DAY_WIDTH);
      const newStart = Math.max(1, dragging.origStart + deltaDays);

      if (newStart !== dragging.origStart) {
        setTimeline(prev => {
          if (!prev) return null;
          const newActs = [...prev.activities];
          const act = { ...newActs[dragging.actIdx] };
          const dets = [...act.details];

          if (dets[dragging.detIdx].startDay === newStart) return prev; // No change

          dets[dragging.detIdx] = { ...dets[dragging.detIdx], startDay: newStart };
          act.details = dets;
          newActs[dragging.actIdx] = act;

          // Expand total duration if needed
          const endDay = newStart + dets[dragging.detIdx].durationDays;
          const newTotal = Math.max(prev.totalDurationDays, endDay);

          return { ...prev, activities: newActs, totalDurationDays: newTotal };
        });
        setDirty(true);
      }
    };

    const onUp = () => {
      if (dragging) setDragging(null);
    };

    if (dragging) {
      window.addEventListener('mousemove', onMove);
      window.addEventListener('mouseup', onUp);
    }
    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    };
  }, [dragging, timeline]);

  const metrics = useMemo(() => {
    if (!timeline) return null;
    const days = Array.from({ length: timeline.totalDurationDays }, (_, i) => i + 1);
    const weeks: { index: number; span: number }[] = [];
    for (let dayIndex = 0; dayIndex < days.length;) {
      const span = Math.min(5, days.length - dayIndex);
      weeks.push({ index: weeks.length + 1, span });
      dayIndex += span;
    }

    const months: { index: number; span: number }[] = [];
    for (let weekIndex = 0; weekIndex < weeks.length;) {
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

  if (loading && !timeline) {
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
    if (versions.length === 0) {
      return (
        <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6 }}>
          <Alert severity="info">No timeline data available. Please generate one first.</Alert>
        </Box>
      );
    }
    return null; // Should switch to loading
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
          {!isEditing && (
            <Button variant="contained" color="secondary" onClick={() => setIsEditing(true)}>
              Edit Timeline
            </Button>
          )}
          {isEditing && (
            <>
              <Button color="inherit" onClick={() => { setIsEditing(false); loadTimeline(); setDirty(false); }}>
                Cancel
              </Button>
              <Button variant="outlined" color="primary" onClick={handleRemoveEmptyColumns}>
                Remove Gaps
              </Button>
              <Button variant="contained" color="primary" onClick={handleSave} disabled={!dirty}>
                Save Changes (New Version)
              </Button>
            </>
          )}
          <Button variant="contained" onClick={handleReset} disabled={regenerating} sx={{ ml: 2 }}>
            {regenerating ? 'Resetting…' : 'Reset Timeline'}
          </Button>
        </Stack>
      </Stack>

      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs value={selectedVersion} onChange={handleTabChange} aria-label="timeline version tabs">
          {versions.sort((a, b) => a.version - b.version).map((v) => (
            <Tab
              key={v.version}
              label={v.version === 0 ? "V0 (Standard)" : v.version === 1 ? "V1 (AI Detailed)" : `V${v.version} (Refined)`}
              value={v.version}
            />
          ))}
        </Tabs>
      </Box>

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
            {timeline.activities.map((activity, actIdx) => (
              <Fragment key={activity.activityName}>
                {activity.details.map((detail, detIdx) => (
                  <tr key={`${detail.taskName}-${detIdx}`}>
                    {detIdx === 0 && (
                      <td
                        rowSpan={activity.details.length}
                        className={clsx(styles.dataCell, styles.activityGroup)}
                        style={{ width: LEFT_PANE_WIDTHS.col1 }}
                      >
                        {activity.activityName}
                      </td>
                    )}
                    <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col2 }}>
                      <Stack direction="row" alignItems="center" justifyContent="space-between">
                        <span>{detail.taskName}</span>
                      </Stack>
                    </td>
                    <td className={styles.dataCell} style={{ width: LEFT_PANE_WIDTHS.col3 }}>
                      {detail.actor}
                    </td>
                    <td className={clsx(styles.dataCell, styles.textRight)} style={{ width: LEFT_PANE_WIDTHS.col4 }}>
                      {summary?.formatNumber(detail.manDays ?? 0)}
                    </td>
                    {metrics.days.map(day => {
                      const isStart = day === detail.startDay;
                      return (
                        <td key={day} className={styles.timelineCell}>
                          {isStart && (
                            <div
                              className={styles.ganttBar}
                              style={{
                                width: (detail.durationDays * DAY_WIDTH) - 2
                              }}
                              onMouseDown={(e) => handleDragStart(e, actIdx, detIdx, detail)}
                              title={`${detail.taskName} (${detail.durationDays} days)`}
                            />
                          )}
                        </td>
                      );
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

            {timeline.resourceAllocation.filter(res => res.role !== 'Unassigned').map((res, index) => (
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
