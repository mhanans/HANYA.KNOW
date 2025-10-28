import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/router';
import {
  Alert,
  Backdrop,
  Card,
  CardContent,
  CardHeader,
  Chip,
  CircularProgress,
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
import LaunchIcon from '@mui/icons-material/Launch';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import DownloadIcon from '@mui/icons-material/Download';
import TaskAltIcon from '@mui/icons-material/TaskAlt';
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
  step?: number;
  createdAt?: string;
  lastModifiedAt?: string;
}

interface ProjectAssessmentSummary {
  id: number;
  templateId: number;
  templateName: string;
  projectName: string;
  status: string;
  step?: number;
  createdAt?: string;
  lastModifiedAt?: string;
}

interface AssessmentHistoryProps {
  refreshToken: number;
  onOpenJob?: (jobId: number) => void | boolean | Promise<void | boolean>;
  onOpenAssessment?: (assessmentId: number) => void | boolean | Promise<void | boolean>;
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

const formatStatusLabel = (status: string) => {
  if (typeof status !== 'string') {
    return 'Unknown';
  }

  return status.replace(/([a-z])([A-Z])/g, '$1 $2');
};

const getStatusColor = (status: AssessmentJobSummary['status']) => {
  if (typeof status === 'string' && status in statusColors) {
    return statusColors[status as AssessmentJobStatus];
  }

  return 'default';
};

export default function AssessmentHistory({ refreshToken, onOpenJob, onOpenAssessment }: AssessmentHistoryProps) {
  const router = useRouter();
  const [jobs, setJobs] = useState<AssessmentJobSummary[]>([]);
  const [savedAssessments, setSavedAssessments] = useState<ProjectAssessmentSummary[]>([]);
  const [jobsLoading, setJobsLoading] = useState(false);
  const [assessmentsLoading, setAssessmentsLoading] = useState(false);
  const [jobsError, setJobsError] = useState('');
  const [assessmentsError, setAssessmentsError] = useState('');
  const [jobDeleting, setJobDeleting] = useState<number | null>(null);
  const [assessmentDeleting, setAssessmentDeleting] = useState<number | null>(null);
  const [isNavigating, setIsNavigating] = useState(false);
  const [navigationError, setNavigationError] = useState('');
  const [downloadingId, setDownloadingId] = useState<number | null>(null);
  const [markingCompleteId, setMarkingCompleteId] = useState<number | null>(null);
  const isMountedRef = useRef(true);

  useEffect(() => {
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  useEffect(() => {
    const handleNavigationComplete = () => {
      if (isMountedRef.current) {
        setIsNavigating(false);
      }
    };

    const handleNavigationError = (error: unknown) => {
      if (!isMountedRef.current) {
        return;
      }

      setIsNavigating(false);
      if (!navigationError) {
        const message =
          error instanceof Error && error.message
            ? error.message
            : 'Unable to open the workspace. Please try again.';
        setNavigationError(message);
      }
    };

    router.events.on('routeChangeComplete', handleNavigationComplete);
    router.events.on('routeChangeError', handleNavigationError);

    return () => {
      router.events.off('routeChangeComplete', handleNavigationComplete);
      router.events.off('routeChangeError', handleNavigationError);
    };
  }, [navigationError, router.events]);

  const loadJobs = useCallback(async () => {
    setJobsLoading(true);
    setJobsError('');
    try {
      const response = await apiFetch('/api/assessment/jobs');
      if (!response.ok) {
        throw new Error(await response.text());
      }
      const data: AssessmentJobSummary[] = await response.json();
      setJobs(data);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unable to load assessment jobs.';
      setJobsError(message);
      setJobs([]);
    } finally {
      setJobsLoading(false);
    }
  }, []);

  const loadSavedAssessments = useCallback(async () => {
    setAssessmentsLoading(true);
    setAssessmentsError('');
    try {
      const response = await apiFetch('/api/assessment/history');
      if (!response.ok) {
        throw new Error(await response.text());
      }
      const data: ProjectAssessmentSummary[] = await response.json();
      setSavedAssessments(data);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unable to load saved assessments.';
      setAssessmentsError(message);
      setSavedAssessments([]);
    } finally {
      setAssessmentsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadJobs();
    loadSavedAssessments();
  }, [loadJobs, loadSavedAssessments, refreshToken]);

  const handleOpenJob = useCallback(
    async (id: number) => {
      if (!onOpenJob) return;
      setNavigationError('');
      setIsNavigating(true);
      try {
        const result = await Promise.resolve(onOpenJob(id));
        if (result === false) {
          setIsNavigating(false);
          return;
        }
      } catch (err) {
        const message =
          err instanceof Error && err.message
            ? err.message
            : 'Unable to open the workspace. Please try again.';
        setNavigationError(message);
        if (isMountedRef.current) {
          setIsNavigating(false);
        }
      } finally {
      }
    },
    [onOpenJob]
  );

  const handleOpenAssessment = useCallback(
    async (id: number) => {
      if (!onOpenAssessment) return;
      setNavigationError('');
      setIsNavigating(true);
      try {
        const result = await Promise.resolve(onOpenAssessment(id));
        if (result === false) {
          setIsNavigating(false);
          return;
        }
      } catch (err) {
        const message =
          err instanceof Error && err.message
            ? err.message
            : 'Unable to open the workspace. Please try again.';
        setNavigationError(message);
        if (isMountedRef.current) {
          setIsNavigating(false);
        }
      } finally {
      }
    },
    [onOpenAssessment]
  );

  const deleteJob = useCallback(
    async (id: number) => {
      if (!window.confirm('Remove this assessment job from history?')) {
        return;
      }
      setJobDeleting(id);
      try {
        const response = await apiFetch(`/api/assessment/jobs/${id}`, { method: 'DELETE' });
        if (!response.ok && response.status !== 404) {
          throw new Error(await response.text());
        }
        setJobs(current => current.filter(job => job.id !== id));
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Unable to delete assessment job.';
        setJobsError(message);
      } finally {
        setJobDeleting(current => (current === id ? null : current));
      }
    },
    []
  );

  const deleteAssessment = useCallback(
    async (id: number) => {
      if (!window.confirm('Delete this saved assessment? This action cannot be undone.')) {
        return;
      }
      setAssessmentDeleting(id);
      try {
        const response = await apiFetch(`/api/assessment/${id}`, { method: 'DELETE' });
        if (!response.ok && response.status !== 404) {
          throw new Error(await response.text());
        }
        setSavedAssessments(current => current.filter(item => item.id !== id));
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Unable to delete saved assessment.';
        setAssessmentsError(message);
      } finally {
        setAssessmentDeleting(current => (current === id ? null : current));
      }
    },
    []
  );

  const handleDownload = useCallback(
    async (id: number) => {
      setAssessmentsError('');
      setDownloadingId(id);
      try {
        const response = await apiFetch(`/api/assessment/${id}/export/full`);
        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || 'Failed to download export.');
        }
        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `assessment-${id}-bundle.xlsx`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.URL.revokeObjectURL(url);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to download export.';
        setAssessmentsError(message);
      } finally {
        setDownloadingId(current => (current === id ? null : current));
      }
    },
    []
  );

  const markAssessmentComplete = useCallback(
    async (id: number) => {
      setAssessmentsError('');
      setMarkingCompleteId(id);
      try {
        const response = await apiFetch(`/api/assessment/${id}/status`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ status: 'Completed' }),
        });
        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || 'Unable to update assessment status.');
        }
        const updated = (await response.json()) as Partial<ProjectAssessmentSummary>;
        setSavedAssessments(current =>
          current.map(item => {
            if (item.id !== id) return item;
            return {
              ...item,
              id: updated.id ?? item.id,
              templateId: updated.templateId ?? item.templateId,
              templateName: updated.templateName ?? item.templateName,
              projectName: updated.projectName ?? item.projectName,
              status: updated.status ?? 'Completed',
              step: updated.step ?? item.step,
              createdAt: updated.createdAt ?? item.createdAt,
              lastModifiedAt: updated.lastModifiedAt ?? item.lastModifiedAt,
            };
          })
        );
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Unable to update assessment status.';
        setAssessmentsError(message);
      } finally {
        setMarkingCompleteId(current => (current === id ? null : current));
      }
    },
    []
  );

  const jobsContent = useMemo(() => {
    if (jobs.length === 0) {
      return (
        <Stack spacing={1.5} alignItems="center" py={4}>
          <Typography variant="subtitle1" color="text.secondary">
            No assessment jobs yet. Jobs launched from the workspace will appear here.
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
            {jobs.map(row => (
              <TableRow key={row.id} hover>
                <TableCell>{row.projectName || 'Untitled Assessment'}</TableCell>
                <TableCell>{row.templateName || '—'}</TableCell>
                <TableCell>
                  <Chip label={formatStatusLabel(row.status)} color={getStatusColor(row.status)} size="small" />
                </TableCell>
                <TableCell>{formatTimestamp(row.createdAt)}</TableCell>
                <TableCell>{formatTimestamp(row.lastModifiedAt)}</TableCell>
                <TableCell align="right">
                  <Stack direction="row" spacing={1} justifyContent="flex-end">
                    {onOpenJob && (
                      <Tooltip title="Open in workspace">
                        <span>
                          <IconButton
                            color="primary"
                            onClick={() => handleOpenJob(row.id)}
                            disabled={isNavigating}
                          >
                            <LaunchIcon fontSize="small" />
                          </IconButton>
                        </span>
                      </Tooltip>
                    )}
                    <Tooltip title="Remove job">
                      <span>
                        <IconButton
                          color="error"
                          onClick={() => deleteJob(row.id)}
                          disabled={jobDeleting === row.id || isNavigating}
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
  }, [deleteJob, handleOpenJob, isNavigating, jobDeleting, jobs, onOpenJob]);

  const savedContent = useMemo(() => {
    if (savedAssessments.length === 0) {
      return (
        <Stack spacing={1.5} alignItems="center" py={4}>
          <Typography variant="subtitle1" color="text.secondary">
            No saved assessments yet. Save workspace results to reuse them during estimation.
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
            {savedAssessments.map(row => (
              <TableRow key={row.id} hover>
                <TableCell>{row.projectName || 'Untitled Assessment'}</TableCell>
                <TableCell>{row.templateName || '—'}</TableCell>
                <TableCell>
                  <Chip label={formatStatusLabel(row.status)} size="small" color={row.status === 'Completed' ? 'success' : 'default'} />
                </TableCell>
                <TableCell>{formatTimestamp(row.createdAt)}</TableCell>
                <TableCell>{formatTimestamp(row.lastModifiedAt)}</TableCell>
                <TableCell align="right">
                  <Stack direction="row" spacing={1} justifyContent="flex-end">
                    {row.status?.toLowerCase() !== 'completed' && (
                      <Tooltip title="Mark as completed">
                        <span>
                          <IconButton
                            color="success"
                            onClick={() => markAssessmentComplete(row.id)}
                            disabled={markingCompleteId === row.id || isNavigating}
                          >
                            <TaskAltIcon fontSize="small" />
                          </IconButton>
                        </span>
                      </Tooltip>
                    )}
                    <Tooltip title="Download bundled export">
                      <span>
                        <IconButton
                          color="primary"
                          onClick={() => handleDownload(row.id)}
                          disabled={downloadingId === row.id || isNavigating}
                        >
                          <DownloadIcon fontSize="small" />
                        </IconButton>
                      </span>
                    </Tooltip>
                    {onOpenAssessment && (
                      <Tooltip title="Open for editing">
                        <span>
                          <IconButton
                            color="primary"
                            onClick={() => handleOpenAssessment(row.id)}
                            disabled={isNavigating}
                          >
                            <EditIcon fontSize="small" />
                          </IconButton>
                        </span>
                      </Tooltip>
                    )}
                    <Tooltip title="Delete assessment">
                      <span>
                        <IconButton
                          color="error"
                          onClick={() => deleteAssessment(row.id)}
                          disabled={assessmentDeleting === row.id || isNavigating}
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
  }, [assessmentDeleting, deleteAssessment, downloadingId, handleDownload, handleOpenAssessment, isNavigating, markAssessmentComplete, markingCompleteId, onOpenAssessment, savedAssessments]);

  return (
    <>
      <Backdrop
        open={isNavigating}
        sx={{ color: '#fff', zIndex: theme => theme.zIndex.modal + 1 }}
      >
        <CircularProgress color="inherit" />
      </Backdrop>
      <Stack spacing={3}>
        {navigationError && <Alert severity="error">{navigationError}</Alert>}
        <Card>
          <CardHeader
            title="Assessment Jobs"
            subheader="Monitor processing progress and reopen results in the workspace."
          />
          {jobsLoading && <LinearProgress />}
          <CardContent>
            <Stack spacing={2}>
              {jobsError && <Alert severity="error">{jobsError}</Alert>}
              {jobsContent}
            </Stack>
          </CardContent>
        </Card>

        <Card>
          <CardHeader
            title="Saved Project Assessments"
            subheader="Reuse completed assessments as reference material or edit them in the workspace."
          />
          {assessmentsLoading && <LinearProgress />}
          <CardContent>
            <Stack spacing={2}>
              {assessmentsError && <Alert severity="error">{assessmentsError}</Alert>}
              {savedContent}
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </>
  );
}
