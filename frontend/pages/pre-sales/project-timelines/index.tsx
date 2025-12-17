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
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Grid,
  TextField,
  Divider,
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

interface TimelineRoleEstimate {
  role: string;
  estimatedHeadcount: number;
  totalManDays: number;
}

interface TeamRecommendation {
  totalManDays: number;
  totalManHours: number;
  recommendedTeamName: string;
  roles: TimelineRoleEstimate[];
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

  // Wizard State
  const [wizardOpen, setWizardOpen] = useState(false);
  const [wizardAssessmentId, setWizardAssessmentId] = useState<number | null>(null);
  const [wizardLoading, setWizardLoading] = useState(false);
  const [recommendation, setRecommendation] = useState<TeamRecommendation | null>(null);
  const [confirmedRoles, setConfirmedRoles] = useState<TimelineRoleEstimate[]>([]);
  const [bufferPercent, setBufferPercent] = useState<number>(20);

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

  const handleOpenWizard = useCallback(async (assessmentId: number) => {
    setWizardAssessmentId(assessmentId);
    setWizardOpen(true);
    setWizardLoading(true);
    setRecommendation(null);
    try {
      const res = await apiFetch(`/api/timeline-estimations/recommendation/${assessmentId}`);
      if (!res.ok) throw new Error("Failed to fetch team recommendation");
      const data: TeamRecommendation = await res.json();
      setRecommendation(data);
      setConfirmedRoles(data.roles);
    } catch (err) {
      Swal.fire('Error', 'Could not load team recommendation', 'error');
      setWizardOpen(false);
    } finally {
      setWizardLoading(false);
    }
  }, []);

  const handleRoleChange = (index: number, field: keyof TimelineRoleEstimate, value: string | number) => {
    const newRoles = [...confirmedRoles];
    if (field === 'estimatedHeadcount') {
      newRoles[index].estimatedHeadcount = Number(value);
    }
    setConfirmedRoles(newRoles);
  };

  const handleWizardConfirm = async () => {
    if (!wizardAssessmentId) return;
    setWizardLoading(true); // Re-use loading state for submission
    try {
      const res = await apiFetch('/api/timeline-estimations/generate-strict', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          assessmentId: wizardAssessmentId,
          confirmedTeam: confirmedRoles,
          bufferPercentage: bufferPercent
        })
      });

      if (!res.ok) {
        const txt = await res.text();
        throw new Error(txt || "Failed to generate timeline");
      }

      setWizardOpen(false);
      await Swal.fire('Success', 'Timeline generated successfully!', 'success');
      loadData();
      // Redirect to Timeline View (Gantt)
      router.push(`/pre-sales/project-timelines/${wizardAssessmentId}`);

    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      Swal.fire('Error', msg, 'error');
    } finally {
      setWizardLoading(false);
    }
  };

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
              const timelineLabel = row.hasTimeline ? 'View Timeline' : 'Generate Timeline';
              const isGenerating = generatingId === row.assessmentId;
              // "Generate Estimate" now opens wizard
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
                    <Button
                      variant="contained"
                      color={row.hasTimeline ? 'secondary' : 'primary'}
                      size="small"
                      disabled={isGenerating}
                      onClick={() =>
                        row.hasTimeline
                          ? handleView(row.assessmentId)
                          : handleOpenWizard(row.assessmentId)
                      }
                    >
                      {row.hasTimeline ? 'View Timeline' : 'Generate Timeline'}
                    </Button>
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
    handleGenerate,
    handleView,
    handleOpenWizard,
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

      {/* Team Selection Wizard Dialog */}
      <Dialog open={wizardOpen} onClose={() => !wizardLoading && setWizardOpen(false)} maxWidth="md" fullWidth>
        <DialogTitle>Timeline Generation Wizard</DialogTitle>
        <Divider />
        <DialogContent>
          {wizardLoading && !recommendation ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
              <CircularProgress />
            </Box>
          ) : recommendation ? (
            <Stack spacing={3}>
              <Grid container spacing={2}>
                <Grid item xs={6}>
                  <Paper variant="outlined" sx={{ p: 2, textAlign: 'center' }}>
                    <Typography variant="subtitle2" color="text.secondary" gutterBottom>
                      Detected Effort
                    </Typography>
                    <Stack direction="row" alignItems="baseline" justifyContent="center" spacing={2}>
                      <Box>
                        <Typography variant="h4" component="span" fontWeight="bold">
                          {recommendation.totalManHours.toFixed(1)}
                        </Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
                          MH
                        </Typography>
                      </Box>
                      <Divider orientation="vertical" flexItem sx={{ height: 24, alignSelf: 'center' }} />
                      <Box>
                        <Typography variant="h5" component="span" color="text.secondary">
                          {recommendation.totalManDays.toFixed(1)}
                        </Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
                          MD
                        </Typography>
                      </Box>
                    </Stack>
                  </Paper>
                </Grid>
                <Grid item xs={6}>
                  <Paper variant="outlined" sx={{ p: 2, textAlign: 'center' }}>
                    <Typography variant="subtitle2" color="text.secondary">
                      Team Recommendation
                    </Typography>
                    <Typography variant="h4">{recommendation.recommendedTeamName}</Typography>
                  </Paper>
                </Grid>
              </Grid>



              <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                <TextField
                  label="Risk Buffer (%)"
                  type="number"
                  value={bufferPercent}
                  onChange={(e) => setBufferPercent(Number(e.target.value))}
                  inputProps={{ min: 0, max: 200 }}
                  size="small"
                  sx={{ width: 150 }}
                />
                <Typography variant="body2" color="text.secondary">
                  Applies {bufferPercent}% buffer to all estimates.
                </Typography>
              </Box>

              <Typography variant="h6">Adjust Team Composition</Typography>
              <TableContainer component={Paper} variant="outlined">
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Role</TableCell>
                      <TableCell align="right">Total Effort (MD)</TableCell>
                      <TableCell align="right" width={150}>Headcount</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {confirmedRoles.map((role, idx) => (
                      <TableRow key={role.role}>
                        <TableCell>{role.role}</TableCell>
                        <TableCell align="right">{role.totalManDays.toFixed(1)}</TableCell>
                        <TableCell align="right">
                          <TextField
                            type="number"
                            size="small"
                            value={role.estimatedHeadcount}
                            onChange={(e) => handleRoleChange(idx, 'estimatedHeadcount', e.target.value)}
                            inputProps={{ min: 0, step: 0.5, style: { textAlign: 'right' } }}
                          />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <Typography variant="caption" color="text.secondary">
                * Adjusting headcount will directly impact the duration of tasks assigned to that role.
              </Typography>

            </Stack>
          ) : (
            <Typography color="error">Failed to load recommendation.</Typography>
          )}
        </DialogContent>
        <DialogActions sx={{ p: 2 }}>
          <Button onClick={() => setWizardOpen(false)} disabled={wizardLoading}>Cancel</Button>
          <Button
            onClick={handleWizardConfirm}
            variant="contained"
            disabled={wizardLoading || !recommendation}
          >
            {wizardLoading ? 'Generating...' : 'Confirm & Generate Timeline'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box >
  );
}
