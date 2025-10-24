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

interface TimelineAssessmentSummary {
  assessmentId: number;
  projectName: string;
  templateName: string;
  status: string;
  lastModifiedAt?: string;
  hasTimeline: boolean;
  timelineGeneratedAt?: string;
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

export default function CostEstimationsPage() {
  const router = useRouter();
  const [rows, setRows] = useState<TimelineAssessmentSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch('/api/cost-estimations');
      if (!res.ok) {
        throw new Error(`Failed to load cost estimations (${res.status})`);
      }
      const data = await res.json();
      setRows(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load cost estimations');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleView = useCallback(
    (assessmentId: number) => {
      router.push(`/pre-sales/cost-estimations/${assessmentId}`);
    },
    [router]
  );

  const content = useMemo(() => {
    if (loading) {
      return (
        <Stack direction="row" alignItems="center" justifyContent="center" spacing={2} sx={{ py: 6 }}>
          <CircularProgress size={32} />
          <Typography variant="body1">Loading projects…</Typography>
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
          <Typography variant="h6">No timelines ready yet.</Typography>
          <Typography variant="body2" sx={{ mt: 1 }}>
            Generate a project timeline to enable cost estimations.
          </Typography>
        </Box>
      );
    }

    return (
      <TableContainer component={Paper} elevation={0} sx={{ borderRadius: 2, border: '1px solid rgba(0,0,0,0.08)' }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Project</TableCell>
              <TableCell>Template</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Timeline Generated</TableCell>
              <TableCell align="right">Action</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map(row => (
              <TableRow key={row.assessmentId} hover>
                <TableCell>{row.projectName || 'Untitled Project'}</TableCell>
                <TableCell>{row.templateName || '—'}</TableCell>
                <TableCell>
                  <Chip color={row.status === 'Completed' ? 'success' : 'default'} size="small" label={row.status} />
                </TableCell>
                <TableCell>{formatDate(row.timelineGeneratedAt)}</TableCell>
                <TableCell align="right">
                  <Button variant="contained" size="small" onClick={() => handleView(row.assessmentId)}>
                    Create / View Estimation
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    );
  }, [loading, error, rows, loadData, handleView]);

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Box>
        <Typography variant="h1" gutterBottom>
          Cost & Revenue Estimations
        </Typography>
        <Typography variant="body1" color="text.secondary">
          Explore AI-generated project timelines to craft pricing scenarios and profitability forecasts.
        </Typography>
      </Box>
      {content}
    </Box>
  );
}
