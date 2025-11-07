import { useRouter } from 'next/router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Box,
  Button,
  Chip,
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
import Swal from 'sweetalert2';
import { apiFetch } from '../../../lib/api';

interface TimelinePhaseEstimate {
  phaseName: string;
  durationDays: number;
  sequenceType: string;
}

interface TimelineRoleEstimate {
  role: string;
  estimatedHeadcount: number;
  totalManDays: number;
}

interface TimelineEstimationRecord {
  assessmentId: number;
  projectName: string;
  templateName: string;
  generatedAt: string;
  projectScale: string;
  totalDurationDays: number;
  sequencingNotes: string;
  phases: TimelinePhaseEstimate[];
  roles: TimelineRoleEstimate[];
}

const formatDateTime = (value?: string) => {
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

export default function TimelineEstimatorDetailPage() {
  const router = useRouter();
  const { assessmentId } = router.query;
  const resolvedId = useMemo(() => {
    if (!assessmentId) return null;
    const parsed = Array.isArray(assessmentId) ? parseInt(assessmentId[0], 10) : parseInt(assessmentId, 10);
    return Number.isNaN(parsed) ? null : parsed;
  }, [assessmentId]);

  const [estimation, setEstimation] = useState<TimelineEstimationRecord | null>(null);
  const [loading, setLoading] = useState(false);
  const [regenerating, setRegenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadEstimation = useCallback(async () => {
    if (!resolvedId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch(`/api/timeline-estimations/${resolvedId}`);
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to load timeline estimation');
      }
      const data = await res.json();
      setEstimation(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load timeline estimation');
    } finally {
      setLoading(false);
    }
  }, [resolvedId]);

  useEffect(() => {
    if (resolvedId) {
      loadEstimation();
    }
  }, [resolvedId, loadEstimation]);

  const handleRegenerate = useCallback(async () => {
    if (!resolvedId) return;
    setRegenerating(true);
    setError(null);
    try {
      const res = await apiFetch('/api/timeline-estimations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ assessmentId: resolvedId }),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to regenerate timeline estimation');
      }
      await loadEstimation();
      Swal.fire({ icon: 'success', title: 'Timeline estimation updated' });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to regenerate timeline estimation';
      setError(message);
      Swal.fire({ icon: 'error', title: 'Timeline estimation failed', text: message });
    } finally {
      setRegenerating(false);
    }
  }, [resolvedId, loadEstimation]);

  if (!resolvedId) {
    return (
      <Box sx={{ py: 6, textAlign: 'center' }}>
        <Typography color="error">Invalid assessment identifier.</Typography>
      </Box>
    );
  }

  if (loading && !estimation) {
    return (
      <Stack direction="row" spacing={2} alignItems="center" justifyContent="center" sx={{ py: 6 }}>
        <CircularProgress size={32} />
        <Typography variant="body1">Loading timeline estimator output…</Typography>
      </Stack>
    );
  }

  if (error && !estimation) {
    return (
      <Box sx={{ py: 6, textAlign: 'center' }}>
        <Typography color="error">{error}</Typography>
        <Button sx={{ mt: 2 }} variant="contained" onClick={loadEstimation}>
          Retry
        </Button>
      </Box>
    );
  }

  if (!estimation) {
    return (
      <Box sx={{ py: 6, textAlign: 'center' }}>
        <Typography>No timeline estimation available. Generate one from the timeline list.</Typography>
      </Box>
    );
  }

  const sumPhaseDuration = estimation.phases.reduce((sum, phase) => sum + phase.durationDays, 0);

  return (
    <Box sx={{ maxWidth: 1100, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
        <Box>
          <Typography variant="h1" gutterBottom>
            Timeline Estimator
          </Typography>
          <Typography variant="body1" color="text.secondary">
            AI-generated scale, phase durations, and resource headcount guidance for downstream timeline generation.
          </Typography>
        </Box>
        <Stack direction="row" alignItems="center" spacing={1}>
          <Chip color="info" label={`Scale: ${estimation.projectScale}`} />
          <Chip color="secondary" label={`Target Duration: ${estimation.totalDurationDays} days`} />
        </Stack>
      </Stack>

      <Paper variant="outlined" sx={{ p: 3, borderRadius: 3 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2}>
          <Box>
            <Typography variant="h6">Project Overview</Typography>
            <Typography variant="body2" color="text.secondary">
              Project: <strong>{estimation.projectName || 'Untitled Project'}</strong>
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Template: <strong>{estimation.templateName || '—'}</strong>
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Generated: <strong>{formatDateTime(estimation.generatedAt)}</strong>
            </Typography>
          </Box>
          <Stack direction="row" spacing={1} alignItems="flex-end" justifyContent="flex-end">
            <Button variant="outlined" onClick={() => router.push('/pre-sales/project-timelines')}>
              Back to Timelines
            </Button>
            <Button
              variant="contained"
              color="primary"
              onClick={handleRegenerate}
              disabled={regenerating}
            >
              {regenerating ? 'Regenerating…' : 'Regenerate Estimation'}
            </Button>
          </Stack>
        </Stack>
        <Divider sx={{ my: 2 }} />
        <Typography variant="body2" color="text.secondary">
          Sequencing notes: {estimation.sequencingNotes || '—'}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
          Sum of phase durations: {sumPhaseDuration} days. Total duration target: {estimation.totalDurationDays} days.
        </Typography>
      </Paper>

      <Paper variant="outlined" sx={{ p: 3, borderRadius: 3 }}>
        <Typography variant="h6" gutterBottom>
          Phase Duration Guidance
        </Typography>
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Phase</TableCell>
                <TableCell align="right">Duration (days)</TableCell>
                <TableCell align="right">Sequencing</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {estimation.phases.map(phase => (
                <TableRow key={phase.phaseName}>
                  <TableCell>{phase.phaseName}</TableCell>
                  <TableCell align="right">{phase.durationDays}</TableCell>
                  <TableCell align="right">{phase.sequenceType}</TableCell>
                </TableRow>
              ))}
              {estimation.phases.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3} align="center">
                    No phase guidance available.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>

      <Paper variant="outlined" sx={{ p: 3, borderRadius: 3 }}>
        <Typography variant="h6" gutterBottom>
          Resource Headcount Guidance
        </Typography>
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Role</TableCell>
                <TableCell align="right">Estimated Headcount</TableCell>
                <TableCell align="right">Total Man-Days</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {estimation.roles.map(role => (
                <TableRow key={role.role}>
                  <TableCell>{role.role}</TableCell>
                  <TableCell align="right">{role.estimatedHeadcount.toFixed(2)}</TableCell>
                  <TableCell align="right">{role.totalManDays.toFixed(2)}</TableCell>
                </TableRow>
              ))}
              {estimation.roles.length === 0 && (
                <TableRow>
                  <TableCell colSpan={3} align="center">
                    No resource guidance available.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
    </Box>
  );
}
