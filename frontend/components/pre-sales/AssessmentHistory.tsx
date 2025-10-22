import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Card,
  CardContent,
  CardHeader,
  IconButton,
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
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import LaunchIcon from '@mui/icons-material/Launch';
import { apiFetch } from '../../lib/api';
import Swal from 'sweetalert2';

export type AssessmentStatus = 'Draft' | 'Completed';

export interface AssessmentSummary {
  id: number;
  templateId: number;
  templateName: string;
  projectName: string;
  status: AssessmentStatus;
  createdAt?: string;
  lastModifiedAt?: string;
}

interface AssessmentHistoryProps {
  refreshToken: number;
  onSelect: (assessmentId: number) => void;
}

const formatTimestamp = (value?: string) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
};

export default function AssessmentHistory({ refreshToken, onSelect }: AssessmentHistoryProps) {
  const [rows, setRows] = useState<AssessmentSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [deletingId, setDeletingId] = useState<number | null>(null);

  const loadHistory = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await apiFetch('/api/assessment/history');
      if (!response.ok) {
        throw new Error(await response.text());
      }
      const data: AssessmentSummary[] = await response.json();
      setRows(data);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unable to load assessment history.';
      setError(message);
      await Swal.fire({
        icon: 'error',
        title: 'Unable to load history',
        text: message,
      });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadHistory();
  }, [loadHistory, refreshToken]);

  const handleDelete = useCallback(
    async (id: number) => {
      setError('');
      const confirmation = await Swal.fire({
        title: 'Delete this assessment?',
        text: 'This action cannot be undone.',
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Delete',
        cancelButtonText: 'Cancel',
        reverseButtons: true,
        confirmButtonColor: '#ef4444',
      });

      if (!confirmation.isConfirmed) {
        return;
      }

      setDeletingId(id);
      try {
        const response = await apiFetch(`/api/assessment/${id}`, { method: 'DELETE' });
        if (!response.ok) {
          throw new Error(await response.text());
        }
        await loadHistory();
        await Swal.fire({
          icon: 'success',
          title: 'Assessment deleted',
          text: 'The assessment entry has been removed.',
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to delete assessment.';
        setError(message);
        await Swal.fire({
          icon: 'error',
          title: 'Delete failed',
          text: message,
        });
      } finally {
        setDeletingId(null);
      }
    },
    [loadHistory]
  );

  const content = useMemo(() => {
    if (rows.length === 0) {
      return (
        <Stack spacing={1.5} alignItems="center" py={4}>
          <Typography variant="subtitle1" color="text.secondary">
            No assessments yet. Saved assessments will appear here.
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
                <TableCell>{row.status}</TableCell>
                <TableCell>{formatTimestamp(row.createdAt)}</TableCell>
                <TableCell>{formatTimestamp(row.lastModifiedAt)}</TableCell>
                <TableCell align="right">
                  <Stack direction="row" spacing={1} justifyContent="flex-end">
                    <Tooltip title="Open assessment">
                      <IconButton color="primary" onClick={() => onSelect(row.id)}>
                        <LaunchIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                    <Tooltip title="Delete assessment">
                      <span>
                        <IconButton
                          color="error"
                          disabled={deletingId === row.id}
                          onClick={() => handleDelete(row.id)}
                        >
                          <DeleteIcon fontSize="small" />
                        </IconButton>
                      </span>
                    </Tooltip>
                  </Stack>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    );
  }, [rows, deletingId, handleDelete, onSelect]);

  return (
    <Card>
      <CardHeader
        title="Recent Assessments"
        subheader="Access drafts and completed project assessments created in this workspace."
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
