import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Card,
  CardContent,
  CardHeader,
  Chip,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
  IconButton,
} from '@mui/material';
import LaunchIcon from '@mui/icons-material/Launch';
import { apiFetch } from '../../lib/api';

export type AssessmentJobStatus =
  | 'Pending'
  | 'GenerationInProgress'
  | 'GenerationComplete'
  | 'EstimationInProgress'
  | 'EstimationComplete'
  | 'Complete'
  | 'FailedGeneration'
  | 'FailedEstimation';

export interface AssessmentJobSummary {
  id: number;
  templateId: number;
  templateName: string;
  projectName: string;
  status: AssessmentJobStatus;
  createdAt?: string;
  lastModifiedAt?: string;
}

interface AssessmentHistoryProps {
  refreshToken: number;
  onSelect: (jobId: number) => void;
}

const formatTimestamp = (value?: string) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
};

const statusColors: Record<AssessmentJobStatus, 'default' | 'warning' | 'success' | 'error' | 'info'> = {
  Pending: 'warning',
  GenerationInProgress: 'warning',
  GenerationComplete: 'info',
  EstimationInProgress: 'warning',
  EstimationComplete: 'info',
  Complete: 'success',
  FailedGeneration: 'error',
  FailedEstimation: 'error',
};

export default function AssessmentHistory({ refreshToken, onSelect }: AssessmentHistoryProps) {
  const [rows, setRows] = useState<AssessmentJobSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const loadHistory = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await apiFetch('/api/assessment/jobs');
      if (!response.ok) {
        throw new Error(await response.text());
      }
      const data: AssessmentJobSummary[] = await response.json();
      setRows(data);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unable to load assessment jobs.';
      setError(message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadHistory();
  }, [loadHistory, refreshToken]);

  const content = useMemo(() => {
    if (rows.length === 0) {
      return (
        <Stack spacing={1.5} alignItems="center" py={4}>
          <Typography variant="subtitle1" color="text.secondary">
            No assessments yet. Jobs launched from the workspace will appear here.
          </Typography>
        </Stack>
      );
    }

    return (
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Project Name</TableCell>
              <TableCell>Template</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Created</TableCell>
              <TableCell>Last Updated</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map(row => (
              <TableRow key={row.id} hover>
                <TableCell>{row.projectName || 'Untitled Assessment'}</TableCell>
                <TableCell>{row.templateName || '—'}</TableCell>
                <TableCell>
                  <Chip
                    label={row.status.replace(/([a-z])([A-Z])/g, '$1 $2')}
                    color={statusColors[row.status]}
                    size="small"
                  />
                </TableCell>
                <TableCell>{formatTimestamp(row.createdAt)}</TableCell>
                <TableCell>{formatTimestamp(row.lastModifiedAt)}</TableCell>
                <TableCell align="right">
                  <Stack direction="row" spacing={1} justifyContent="flex-end">
                    <Tooltip title="Open in workspace">
                      <IconButton color="primary" onClick={() => onSelect(row.id)}>
                        <LaunchIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </Stack>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    );
  }, [rows, onSelect]);

  return (
    <Card>
      <CardHeader
        title="Recent Assessment Jobs"
        subheader="Monitor processing progress and reopen results in the workspace."
      />
      {loading && <LinearProgress />}
      <CardContent>
        <Stack spacing={2}>
          {error && <Alert severity="error">{error}</Alert>}
          {content}
        </Stack>
      </CardContent>
    </Card>
  );
}
