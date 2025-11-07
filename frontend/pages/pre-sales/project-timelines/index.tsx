import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import {
  Box,
  Button,
  Chip,
  CircularProgress,
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
import Swal from 'sweetalert2';

interface TimelineAssessmentSummary {
  assessmentId: number;
  projectName: string;
  templateName: string;
  status: string;
  lastModifiedAt?: string;
  hasTimeline: boolean;
  timelineGeneratedAt?: string;
  hasTimelineEstimation: boolean;
  timelineEstimationGeneratedAt?: string;
  timelineEstimationScale?: string | null;
}

const formatDate = (value?: string) => {
  if (!value) return '—';
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

export default function ProjectTimelinesPage() {
  const router = useRouter();
  const [rows, setRows] = useState<TimelineAssessmentSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [generatingId, setGeneratingId] = useState<number | null>(null);
  const [estimatingId, setEstimatingId] = useState<number | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch('/api/timelines');
      if (!res.ok) {
        throw new Error(`Failed to load timelines (${res.status})`);
      }
      const data = await res.json();
      setRows(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load timelines');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleGenerateEstimation = useCallback(
    async (assessmentId: number) => {
      if (!assessmentId) return;
      setEstimatingId(assessmentId);
      setError(null);
      try {
        const res = await apiFetch('/api/timeline-estimations', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ assessmentId }),
        });
        if (!res.ok) {
          const text = await res.text();
          throw new Error(text || 'Failed to generate timeline estimation');
        }
        await loadData();
        router.push(`/pre-sales/timeline-estimator/${assessmentId}`);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to generate timeline estimation';
        setError(message);
        await Swal.fire({
          icon: 'error',
          title: 'Timeline estimation failed',
          text: message,
        });
      } finally {
        setEstimatingId(null);
      }
    },
    [loadData, router]
  );

  const handleViewEstimation = useCallback(
    (assessmentId: number) => {
      router.push(`/pre-sales/timeline-estimator/${assessmentId}`);
    },
    [router]
  );

  const handleGenerate = useCallback(
    async (assessmentId: number) => {
      if (!assessmentId) return;
      setGeneratingId(assessmentId);
      setError(null);
      try {
        const res = await apiFetch('/api/timelines', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ assessmentId }),
        });
        if (!res.ok) {
          const text = await res.text();
          throw new Error(text || 'Failed to generate timeline');
        }
        await loadData();
        router.push(`/pre-sales/project-timelines/${assessmentId}`);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to generate timeline';
        setError(message);
        const result = await Swal.fire({
          icon: 'warning',
          title: 'Resume timeline generation?',
          text: message,
          confirmButtonText: 'Resume',
          cancelButtonText: 'Dismiss',
          showCancelButton: true,
          reverseButtons: true,
          focusCancel: true,
        });
        if (result.isConfirmed) {
          setTimeout(() => {
            void handleGenerate(assessmentId);
          }, 0);
        }
      } finally {
        setGeneratingId(null);
      }
    },
    [loadData, router]
  );

  const handleView = useCallback(
    (assessmentId: number) => {
      router.push(`/pre-sales/project-timelines/${assessmentId}`);
    },
    [router]
  );

  const content = useMemo(() => {
    if (loading) {
      return (
        <Stack direction="row" alignItems="center" justifyContent="center" spacing={2} sx={{ py: 6 }}>
          <CircularProgress size={32} />
          <Typography variant="body1">Loading completed assessments…</Typography>
        </Stack>
      );
    }

    if (error) {
      return (
        <Box sx={{ py: 6, textAlign: 'center' }}>
          <Typography color="error">{error}</Typography>
          <Button sx={{ mt: 2 }} variant="contained" onClick={loadData}>Retry</Button>
        </Box>
      );
    }

    if (rows.length === 0) {
      return (
        <Box sx={{ py: 6, textAlign: 'center' }}>
          <Typography variant="h6">No completed assessments found.</Typography>
          <Typography variant="body2" sx={{ mt: 1 }}>
            Complete an assessment in the workspace to enable timeline generation.
          </Typography>
        </Box>
      );
    }

    return (
      <TableContainer>
        <Table size="small" className="timelines-table">
          <TableHead>
            <TableRow>
              <TableCell>Project</TableCell>
              <TableCell>Template</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Last Modified</TableCell>
              <TableCell>Timeline Estimator</TableCell>
              <TableCell>Timeline</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map(row => {
              const estimationLabel = row.hasTimelineEstimation ? 'View Estimate' : 'Generate Estimate';
              const timelineLabel = row.hasTimeline ? 'View Timeline' : 'Generate Timeline';
              const isGenerating = generatingId === row.assessmentId;
              const isEstimating = estimatingId === row.assessmentId;
              return (
                <TableRow key={row.assessmentId} hover>
                  <TableCell>{row.projectName || 'Untitled Project'}</TableCell>
                  <TableCell>{row.templateName || '—'}</TableCell>
                  <TableCell>
                    <Chip
                      color={row.status === 'Completed' ? 'success' : 'default'}
                      size="small"
                      label={row.status}
                    />
                  </TableCell>
                  <TableCell>{formatDate(row.lastModifiedAt)}</TableCell>
                  <TableCell>
                    {row.hasTimelineEstimation ? (
                      <Stack spacing={0.5}>
                        <Chip
                          color="info"
                          size="small"
                          label={row.timelineEstimationScale ? `Scale: ${row.timelineEstimationScale}` : 'Ready'}
                        />
                        <Typography variant="caption">
                          {formatDate(row.timelineEstimationGeneratedAt)}
                        </Typography>
                      </Stack>
                    ) : (
                      <Chip color="warning" size="small" label="Pending" />
                    )}
                  </TableCell>
                  <TableCell>
                    {row.hasTimeline ? (
                      <Stack spacing={0.5}>
                        <Chip color="success" size="small" label="Ready" />
                        <Typography variant="caption">{formatDate(row.timelineGeneratedAt)}</Typography>
                      </Stack>
                    ) : (
                      <Chip color="warning" size="small" label="Pending" />
                    )}
                  </TableCell>
                  <TableCell align="right">
                    <Stack spacing={1} direction="column" alignItems="flex-end">
                      <Button
                        variant="contained"
                        color={row.hasTimelineEstimation ? 'secondary' : 'primary'}
                        size="small"
                        disabled={isEstimating || isGenerating}
                        onClick={() =>
                          row.hasTimelineEstimation
                            ? handleViewEstimation(row.assessmentId)
                            : handleGenerateEstimation(row.assessmentId)
                        }
                      >
                        {isEstimating ? 'Estimating…' : estimationLabel}
                      </Button>
                      <Button
                        variant="contained"
                        color={row.hasTimeline ? 'secondary' : 'primary'}
                        size="small"
                        disabled={isGenerating || !row.hasTimelineEstimation}
                        onClick={() =>
                          row.hasTimeline
                            ? handleView(row.assessmentId)
                            : handleGenerate(row.assessmentId)
                        }
                        title={row.hasTimelineEstimation ? undefined : 'Generate an estimator first'}
                      >
                        {isGenerating ? 'Generating…' : timelineLabel}
                      </Button>
                    </Stack>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableContainer>
    );
  }, [
    loading,
    error,
    rows,
    generatingId,
    estimatingId,
    handleGenerate,
    handleView,
    handleGenerateEstimation,
    handleViewEstimation,
    loadData,
  ]);

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Box>
        <Typography variant="h1" gutterBottom>
          Pre-Sales Project Timelines
        </Typography>
        <Typography variant="body1" color="text.secondary">
          Transform completed AI assessments into timeline-ready project plans.
        </Typography>
      </Box>
      <Paper variant="outlined" sx={{ p: 3, bgcolor: 'background.paper', borderRadius: 3 }}>
        {content}
      </Paper>
    </Box>
  );
}
