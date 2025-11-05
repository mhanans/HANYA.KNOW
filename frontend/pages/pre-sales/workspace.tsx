import { ChangeEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/router';
import {
  Alert,
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  Chip,
  Divider,
  FormControl,
  FormControlLabel,
  FormLabel,
  InputLabel,
  LinearProgress,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Paper,
  Select,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
  Checkbox,
  ListItemIcon,
  FormHelperText,
  IconButton,
  Tooltip,
  CircularProgress,
  Radio,
  RadioGroup,
} from '@mui/material';
import Autocomplete from '@mui/material/Autocomplete';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import GetAppIcon from '@mui/icons-material/GetApp';
import { LoadingButton } from '@mui/lab';
import AddCircleOutlineIcon from '@mui/icons-material/AddCircleOutline';
import DeleteIcon from '@mui/icons-material/Delete';
import type { AssessmentJobStatus } from '../../components/pre-sales/AssessmentHistory';
import { apiFetch } from '../../lib/api';
import Swal from 'sweetalert2';
import { SelectChangeEvent } from '@mui/material/Select';

const CATEGORY_OPTIONS = [
  'New UI',
  'New Interface',
  'New Backgrounder',
  'Adjust Existing UI',
  'Adjust Existing Logic',
] as const;

const EMPTY_CATEGORY = '' as const;

type CategoryOption = typeof EMPTY_CATEGORY | (typeof CATEGORY_OPTIONS)[number];
const DEFAULT_CATEGORY: CategoryOption = EMPTY_CATEGORY;

const normalizeCategory = (value?: string | null): CategoryOption => {
  if (value === undefined || value === null) return EMPTY_CATEGORY;
  const trimmed = value.trim();
  if (!trimmed) return EMPTY_CATEGORY;
  const match = CATEGORY_OPTIONS.find(option => option.toLowerCase() === trimmed.toLowerCase());
  return match ?? EMPTY_CATEGORY;
};

interface ProjectTemplateMetadata {
  id: number;
  templateName: string;
}

interface AssessmentItem {
  itemId: string;
  itemName: string;
  itemDetail?: string;
  category: CategoryOption;
  isNeeded: boolean;
  estimates: Record<string, number | null>;
}

interface AssessmentSection {
  sectionName: string;
  items: AssessmentItem[];
}

interface ProjectAssessment {
  id?: number;
  templateId: number;
  templateName?: string;
  projectName: string;
  status: AssessmentStatus;
  step: number;
  sections: AssessmentSection[];
  createdAt?: string;
  lastModifiedAt?: string;
}

type AssessmentStatus = 'Draft' | 'Completed';

interface SimilarAssessmentReference {
  id: number;
  projectName: string;
  templateName: string;
  status: AssessmentStatus;
  totalHours: number;
  lastModifiedAt?: string;
}

interface KnowledgeDocumentOption {
  source: string;
  categoryId: number | null;
  pages: number;
  hasSummary: boolean;
}

type OutputLanguageOption = 'indonesian' | 'english';

interface AssessmentJob {
  id: number;
  projectName: string;
  templateId: number;
  templateName: string;
  analysisMode?: 'Interpretive' | 'Strict';
  outputLanguage?: 'Indonesian' | 'English';
  status: AssessmentJobStatus;
  step?: number;
  lastError?: string | null;
  createdAt?: string;
  lastModifiedAt?: string;
}

interface AssessmentJobSummary {
  id: number;
  projectName: string;
  templateId: number;
  templateName?: string;
  outputLanguage?: 'Indonesian' | 'English';
  status: AssessmentJobStatus;
  step?: number;
  createdAt?: string;
  lastModifiedAt?: string;
}

type AnalysisModeOption = 'interpretive' | 'strict';

const statusOptions: AssessmentStatus[] = ['Draft', 'Completed'];

const formatHours = (value: number) => value.toLocaleString(undefined, { maximumFractionDigits: 2 });

const formatTimestamp = (value?: string) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
};

const normalizeAssessment = (assessment: ProjectAssessment): ProjectAssessment => ({
  ...assessment,
  step: Math.max(1, assessment.step ?? 1),
  sections: (assessment.sections ?? []).map(section => ({
    ...section,
    items: (section.items ?? []).map(item => ({
      ...item,
      itemName: item.itemName ?? '',
      itemDetail: item.itemDetail ?? '',
      category: normalizeCategory(item.category),
      isNeeded: true,
      estimates: item.estimates ?? {},
    })),
  })),
});

const terminalJobStatuses: AssessmentJobStatus[] = ['Complete', 'FailedGeneration', 'FailedEstimation'];
const failureJobStatuses: AssessmentJobStatus[] = ['FailedGeneration', 'FailedEstimation'];

const isTerminalJobStatus = (status: AssessmentJobStatus) => terminalJobStatuses.includes(status);
const isFailureJobStatus = (status: AssessmentJobStatus) => failureJobStatuses.includes(status);
const isOutstandingJobStatus = (status: AssessmentJobStatus) => status !== 'Complete';

const jobStatusStepNumber: Record<AssessmentJobStatus, number> = {
  Pending: 2,
  GenerationInProgress: 3,
  GenerationComplete: 4,
  FailedGeneration: 5,
  EstimationInProgress: 6,
  EstimationComplete: 7,
  FailedEstimation: 8,
  Complete: 9,
};

const highestJobStep = Math.max(...Object.values(jobStatusStepNumber));

interface ProjectTemplate {
  id?: number;
  templateName: string;
  estimationColumns: string[];
  sections: TemplateSection[];
}

interface TemplateSection {
  sectionName: string;
  type: string;
  items: TemplateItem[];
}

interface TemplateItem {
  itemId: string;
  itemName: string;
  itemDetail: string;
  category: CategoryOption;
}

interface AssessmentTreeGridProps {
  sections: AssessmentSection[];
  estimationColumns: string[];
  expandedSections: Record<string, boolean>;
  onToggleSection: (name: string) => void;
  onEstimateChange: (sectionIndex: number, itemIndex: number, column: string, value: number | null) => void;
  onItemNameChange: (sectionIndex: number, itemIndex: number, value: string) => void;
  onItemDetailChange: (sectionIndex: number, itemIndex: number, value: string) => void;
  onItemCategoryChange: (sectionIndex: number, itemIndex: number, value: CategoryOption) => void;
  onRemoveItem: (sectionIndex: number, itemIndex: number) => void;
  onAddItem: (sectionIndex: number) => void;
  computeItemTotal: (item: AssessmentItem) => number;
}

function AssessmentTreeGrid({
  sections,
  estimationColumns,
  expandedSections,
  onToggleSection,
  onEstimateChange,
  onItemNameChange,
  onItemDetailChange,
  onItemCategoryChange,
  onRemoveItem,
  onAddItem,
  computeItemTotal,
}: AssessmentTreeGridProps) {
  return (
    <Stack spacing={2}>
      {sections.map((section, sectionIndex) => {
        const isExpanded = expandedSections[section.sectionName] !== false;
        const columnTotals = estimationColumns.reduce<Record<string, number>>((acc, column) => {
          acc[column] = section.items.reduce((sum, item) => sum + (item.estimates[column] ?? 0), 0);
          return acc;
        }, {});
        const sectionTotal = Object.values(columnTotals).reduce((sum, value) => sum + value, 0);

        return (
          <Accordion
            key={section.sectionName}
            expanded={isExpanded}
            onChange={() => onToggleSection(section.sectionName)}
            disableGutters
          >
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Stack
                direction={{ xs: 'column', md: 'row' }}
                spacing={2}
                justifyContent="space-between"
                alignItems={{ xs: 'flex-start', md: 'center' }}
                sx={{ width: '100%' }}
              >
                <Typography variant="subtitle1" fontWeight={600}>
                  {section.sectionName}
                </Typography>
                <Stack direction="row" spacing={2} flexWrap="wrap">
                  {estimationColumns.map(column => (
                    <Typography key={column} variant="body2" color="text.secondary">
                      {column}: {formatHours(columnTotals[column])}
                    </Typography>
                  ))}
                  <Typography variant="body2" fontWeight={600}>
                    Total: {formatHours(sectionTotal)} hours
                  </Typography>
                </Stack>
              </Stack>
            </AccordionSummary>
            <AccordionDetails>
              <TableContainer component={Paper} variant="outlined">
                <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Item Name</TableCell>
              <TableCell>Detail</TableCell>
              <TableCell>Category</TableCell>
              {estimationColumns.map(column => (
                <TableCell key={column} align="right">{column}</TableCell>
              ))}
              <TableCell align="right">Total Hours</TableCell>
              <TableCell align="center">Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {section.items.map((item, itemIndex) => {
                      const itemTotal = computeItemTotal(item);
                      return (
                        <TableRow key={`${section.sectionName}-${item.itemId}-${itemIndex}`}>
                          <TableCell sx={{ minWidth: 220 }}>
                            <TextField
                              fullWidth
                              size="small"
                              value={item.itemName}
                              onChange={event => onItemNameChange(sectionIndex, itemIndex, event.target.value)}
                              placeholder="Item name"
                            />
                          </TableCell>
                          <TableCell sx={{ minWidth: 260 }}>
                            <TextField
                              fullWidth
                              size="small"
                        value={item.itemDetail ?? ''}
                        onChange={event => onItemDetailChange(sectionIndex, itemIndex, event.target.value)}
                        placeholder="Additional details"
                        multiline
                        minRows={1}
                      />
                    </TableCell>
                    <TableCell sx={{ minWidth: 200 }}>
                      <TextField
                        select
                        fullWidth
                        size="small"
                        value={item.category}
                        SelectProps={{
                          displayEmpty: true,
                          renderValue: value => {
                            if (typeof value !== 'string' || !value) {
                              return '';
                            }
                            return value;
                          },
                        }}
                        onChange={event =>
                          onItemCategoryChange(sectionIndex, itemIndex, normalizeCategory(event.target.value))
                        }
                      >
                        <MenuItem value={EMPTY_CATEGORY}>
                          <em>Select category</em>
                        </MenuItem>
                        {CATEGORY_OPTIONS.map(option => (
                          <MenuItem key={option} value={option}>
                            {option}
                          </MenuItem>
                        ))}
                      </TextField>
                    </TableCell>
                    {estimationColumns.map(column => (
                      <TableCell key={column} align="right">
                        <TextField
                          size="small"
                          type="number"
                                inputProps={{ min: 0, step: 0.25 }}
                                value={item.estimates[column] ?? ''}
                                onChange={(event: ChangeEvent<HTMLInputElement>) => {
                                  const raw = event.target.value;
                                  if (raw === '') {
                                    onEstimateChange(sectionIndex, itemIndex, column, null);
                                    return;
                                  }
                                  const numeric = Number(raw);
                                  if (!Number.isNaN(numeric)) {
                                    onEstimateChange(sectionIndex, itemIndex, column, numeric);
                                  }
                                }}
                                sx={{ width: 100 }}
                              />
                            </TableCell>
                          ))}
                          <TableCell align="right">{formatHours(itemTotal)}</TableCell>
                          <TableCell align="center" sx={{ whiteSpace: 'nowrap' }}>
                            <Tooltip title="Remove item">
                              <span>
                                <IconButton
                                  size="small"
                                  color="error"
                                  onClick={() => onRemoveItem(sectionIndex, itemIndex)}
                                >
                                  <DeleteIcon fontSize="small" />
                                </IconButton>
                              </span>
                            </Tooltip>
                    </TableCell>
                  </TableRow>
                );
              })}
              <TableRow>
                <TableCell colSpan={estimationColumns.length + 5}>
                  <Button
                    startIcon={<AddCircleOutlineIcon />}
                    size="small"
                    onClick={() => onAddItem(sectionIndex)}
                  >
                          Add Item
                        </Button>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </TableContainer>
            </AccordionDetails>
          </Accordion>
        );
      })}
    </Stack>
  );
}

export default function AssessmentWorkspace() {
  const router = useRouter();
  const [templates, setTemplates] = useState<ProjectTemplateMetadata[]>([]);
  const [selectedTemplate, setSelectedTemplate] = useState<number | ''>('');
  const [projectTitle, setProjectTitle] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [analysisLog, setAnalysisLog] = useState<string[]>([]);
  const [progress, setProgress] = useState(0);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [loadingAssessment, setLoadingAssessment] = useState(false);
  const [assessment, setAssessment] = useState<ProjectAssessment | null>(null);
  const [activeJob, setActiveJob] = useState<AssessmentJob | null>(null);
  const [expandedSections, setExpandedSections] = useState<Record<string, boolean>>({});
  const [saving, setSaving] = useState(false);
  const [similarAssessments, setSimilarAssessments] = useState<SimilarAssessmentReference[]>([]);
  const [similarLoading, setSimilarLoading] = useState(false);
  const [similarError, setSimilarError] = useState('');
  const [selectedReferenceIds, setSelectedReferenceIds] = useState<number[]>([]);
  const [availableDocuments, setAvailableDocuments] = useState<KnowledgeDocumentOption[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(false);
  const [documentsError, setDocumentsError] = useState('');
  const [selectedDocumentSources, setSelectedDocumentSources] = useState<string[]>([]);
  const [templateColumns, setTemplateColumns] = useState<string[]>([]);
  const [analysisMode, setAnalysisMode] = useState<AnalysisModeOption>('interpretive');
  const [outputLanguage, setOutputLanguage] = useState<OutputLanguageOption>('indonesian');
  const similarRequestId = useRef(0);
  const jobPollTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastJobStatusRef = useRef<AssessmentJobStatus | null>(null);
  const outstandingJobCheckRef = useRef(false);

  const resolveOutputLanguage = useCallback((language?: string | null): OutputLanguageOption => {
    if (!language) {
      return 'indonesian';
    }

    const normalized = language.trim().toLowerCase();
    if (normalized.startsWith('en')) {
      return 'english';
    }
    if (normalized.includes('english')) {
      return 'english';
    }
    if (normalized.startsWith('id')) {
      return 'indonesian';
    }
    if (normalized.includes('indonesia')) {
      return 'indonesian';
    }

    return 'indonesian';
  }, []);

  const showError = useCallback((message: string, title = 'Something went wrong') => {
    if (!message) return;
    void Swal.fire({ icon: 'error', title, text: message });
  }, []);

  const showSuccess = useCallback((title: string, text: string) => {
    void Swal.fire({
      icon: 'success',
      title,
      text,
      timer: 2600,
      showConfirmButton: false,
    });
  }, []);

  const stopJobPolling = useCallback(() => {
    if (jobPollTimer.current) {
      clearTimeout(jobPollTimer.current);
      jobPollTimer.current = null;
    }
  }, []);

  useEffect(() => () => {
    stopJobPolling();
  }, [stopJobPolling]);

  const applyJobStatus = useCallback(
    (job: AssessmentJob) => {
      setActiveJob(job);
      if (job.analysisMode) {
        setAnalysisMode(job.analysisMode === 'Strict' ? 'strict' : 'interpretive');
      }
      setOutputLanguage(resolveOutputLanguage(job.outputLanguage));

      const currentStepNumber = jobStatusStepNumber[job.status];
      const steps: string[] = [];

      if (currentStepNumber) {
        steps.push(`Current step: ${currentStepNumber} of ${highestJobStep}`);
      } else {
        steps.push('Preparing analysis…');
      }

      steps.push('Uploading scope document…');

      let progressValue = currentStepNumber
        ? Math.round((currentStepNumber / highestJobStep) * 100)
        : 10;

      switch (job.status) {
        case 'Pending':
          steps.push('Waiting for background processor to start…');
          break;
        case 'GenerationInProgress':
          steps.push('Generating assessment breakdown…');
          break;
        case 'GenerationComplete':
          steps.push('Generation complete.');
          steps.push('Queued for effort estimation…');
          break;
        case 'EstimationInProgress':
          steps.push('Generation complete.');
          steps.push('Estimating effort and costs…');
          break;
        case 'EstimationComplete':
          steps.push('Generation complete.');
          steps.push('Effort estimation completed.');
          break;
        case 'Complete':
          steps.push('Generation complete.');
          steps.push('Effort estimation completed.');
          steps.push('Analysis complete.');
          progressValue = 100;
          break;
        case 'FailedGeneration':
          steps.push('Generation step failed.');
          break;
        case 'FailedEstimation':
          steps.push('Generation complete.');
          steps.push('Estimation step failed.');
          break;
        default:
          break;
      }

      if (isFailureJobStatus(job.status)) {
        const failureMessage = job.lastError?.trim();
        if (failureMessage) {
          steps.push(`Error: ${failureMessage}`);
        }

        const resumeFromStep = currentStepNumber ?? Math.max(1, highestJobStep - 1);
        steps.push(`Resolve the issue and resume from step ${resumeFromStep}.`);

        const completedBeforeFailure = Math.max(1, (currentStepNumber ?? 1) - 1);
        progressValue = Math.round((completedBeforeFailure / highestJobStep) * 100);
      }

      setAnalysisLog(steps);
      setProgress(progressValue);
      setIsAnalyzing(!isTerminalJobStatus(job.status));

      const previousStatus = lastJobStatusRef.current;
      const statusChanged = previousStatus !== job.status;

      if (job.status === 'Complete' && statusChanged) {
        showSuccess('Analysis complete', 'Review the AI-generated estimates before saving.');
      } else if (isFailureJobStatus(job.status) && statusChanged) {
        const message = job.lastError || 'The AI analysis did not return valid data. Try again later.';
        showError(message, 'Analysis failed');
      }

      lastJobStatusRef.current = job.status;
      return { previousStatus, statusChanged };
    },
    [resolveOutputLanguage, setAnalysisMode, showError, showSuccess]
  );

  const refreshSimilarAssessments = useCallback(async () => {
    const requestToken = ++similarRequestId.current;
    setSimilarLoading(true);
    setSimilarError('');

    try {
      const res = await apiFetch('/api/assessment/references');
      if (!res.ok) throw new Error(await res.text());
      const data: SimilarAssessmentReference[] = await res.json();
      if (similarRequestId.current === requestToken) {
        setSimilarAssessments(data);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to load reference assessments.';
      if (similarRequestId.current === requestToken) {
        setSimilarAssessments([]);
        setSimilarError(message);
      }
    } finally {
      if (similarRequestId.current === requestToken) {
        setSimilarLoading(false);
      }
    }
  }, []);

  const loadAssessmentFromJob = useCallback(
    async (jobId: number) => {
      try {
        const res = await apiFetch(`/api/assessment/jobs/${jobId}/assessment`);
        if (res.status === 404) {
          setAssessment(null);
          return;
        }
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectAssessment = await res.json();
        setAssessment(normalizeAssessment(data));
        setSelectedTemplate(data.templateId);
        setProjectTitle(data.projectName);
        void refreshSimilarAssessments();
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to load assessment results.';
        showError(message, 'Unable to load assessment');
      }
    },
    [refreshSimilarAssessments, showError]
  );

  const refreshJobStatus = useCallback(
    async (jobId: number) => {
      stopJobPolling();
      try {
        const res = await apiFetch(`/api/assessment/jobs/${jobId}`);
        if (!res.ok) throw new Error(await res.text());
        const job: AssessmentJob = await res.json();
        const { statusChanged } = applyJobStatus(job);

        if (job.status === 'Complete') {
          if (statusChanged) {
            await loadAssessmentFromJob(jobId);
          }
          stopJobPolling();
        } else if (isFailureJobStatus(job.status)) {
          stopJobPolling();
        } else {
          jobPollTimer.current = setTimeout(() => {
            void refreshJobStatus(jobId);
          }, statusChanged ? 500 : 2500);
        }
      } catch (err) {
        stopJobPolling();
        const message = err instanceof Error ? err.message : 'Failed to check job status.';
        showError(message, 'Status check failed');
      }
    },
    [applyJobStatus, loadAssessmentFromJob, showError, stopJobPolling]
  );

  const resumeFailedJob = useCallback(
    async (jobId: number) => {
      stopJobPolling();
      setIsAnalyzing(true);
      setAnalysisLog(current => {
        const prefix = current.length > 0 ? current.slice(0, 1) : ['Preparing analysis…'];
        return [...prefix, 'Attempting to resume from the failed step…'];
      });
      try {
        const res = await apiFetch(`/api/assessment/jobs/${jobId}/resume`, { method: 'POST' });
        if (!res.ok) throw new Error(await res.text());
        const resumedJob: AssessmentJob = await res.json();
        const { statusChanged } = applyJobStatus(resumedJob);

        if (resumedJob.status === 'Complete') {
          await loadAssessmentFromJob(jobId);
          stopJobPolling();
        } else if (isFailureJobStatus(resumedJob.status)) {
          stopJobPolling();
        } else {
          setAssessment(null);
          jobPollTimer.current = setTimeout(() => {
            void refreshJobStatus(jobId);
          }, statusChanged ? 500 : 2000);
        }
        return true;
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to resume the assessment job.';
        showError(message, 'Resume failed');
        setIsAnalyzing(false);
        return false;
      }
    },
    [applyJobStatus, loadAssessmentFromJob, refreshJobStatus, showError, stopJobPolling]
  );

  const deleteJob = useCallback(
    async (jobId: number) => {
      stopJobPolling();
      try {
        const res = await apiFetch(`/api/assessment/jobs/${jobId}`, { method: 'DELETE' });
        if (!res.ok && res.status !== 404) {
          throw new Error(await res.text());
        }

        setActiveJob(current => (current && current.id === jobId ? null : current));
        setAssessment(null);
        setAnalysisLog([]);
        setProgress(0);
        setProjectTitle('');
        setSelectedTemplate('');
        setExpandedSections({});
        setSelectedReferenceIds([]);
        setSelectedDocumentSources([]);
        setOutputLanguage('indonesian');

        showSuccess('Assessment job deleted', 'The failed assessment job has been removed.');
        setIsAnalyzing(false);
        return true;
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to delete the assessment job.';
        showError(message, 'Delete failed');
        return false;
      }
    },
    [showError, showSuccess, stopJobPolling]
  );

  const promptJobRecovery = useCallback(
    async (job: AssessmentJob) => {
      const statusLabel = job.status.replace(/([a-z])([A-Z])/g, '$1 $2');
      const details: string[] = [
        `The assessment job ${job.projectName || 'Untitled Assessment'} is currently marked as ${statusLabel}.`,
      ];
      if (job.lastError?.trim()) {
        details.push(job.lastError.trim());
      }
      details.push('Would you like to resume the process or delete this job?');

      const result = await Swal.fire({
        icon: 'warning',
        title: 'Resume assessment job?',
        text: details.join('\n\n'),
        confirmButtonText: 'Resume',
        showDenyButton: true,
        denyButtonText: 'Delete job',
        showCancelButton: true,
        cancelButtonText: 'Cancel',
        reverseButtons: true,
        focusConfirm: true,
      });

      if (result.isDenied) {
        const deleted = await deleteJob(job.id);
        return deleted;
      }

      if (result.isConfirmed) {
        const resumed = await resumeFailedJob(job.id);
        return resumed;
      }

      return false;
    },
    [deleteJob, resumeFailedJob]
  );

  const loadJob = useCallback(
    async (jobId: number) => {
      stopJobPolling();
      setLoadingAssessment(true);
      try {
        const res = await apiFetch(`/api/assessment/jobs/${jobId}`);
        if (!res.ok) throw new Error(await res.text());
        const job: AssessmentJob = await res.json();
        applyJobStatus(job);
        setSelectedTemplate(job.templateId);
        setProjectTitle(job.projectName);
        void refreshSimilarAssessments();
        if (job.status === 'Complete') {
          await loadAssessmentFromJob(jobId);
        } else if (isFailureJobStatus(job.status)) {
          await promptJobRecovery(job);
          return;
        } else {
          setAssessment(null);
          jobPollTimer.current = setTimeout(() => {
            void refreshJobStatus(jobId);
          }, 1500);
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to load assessment job.';
        showError(message, 'Unable to load job');
      } finally {
        setLoadingAssessment(false);
      }
    },
    [
      applyJobStatus,
      loadAssessmentFromJob,
      promptJobRecovery,
      refreshJobStatus,
      showError,
      stopJobPolling,
    ]
  );

  useEffect(() => {
    const loadTemplates = async () => {
      try {
        const res = await apiFetch('/api/templates');
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectTemplateMetadata[] = await res.json();
        setTemplates(data);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to load templates.';
        showError(message, 'Unable to load templates');
      }
    };
    loadTemplates();
  }, []);

  useEffect(() => {
    let cancelled = false;

    const loadDocuments = async () => {
      setDocumentsLoading(true);
      setDocumentsError('');
      try {
        const res = await apiFetch('/api/documents');
        if (!res.ok) throw new Error(await res.text());
        const data: KnowledgeDocumentOption[] = await res.json();
        if (!cancelled) {
          setAvailableDocuments(data);
        }
      } catch (err) {
        if (!cancelled) {
          setAvailableDocuments([]);
          const message = err instanceof Error ? err.message : 'Failed to load reference documents.';
          setDocumentsError(message);
        }
      } finally {
        if (!cancelled) {
          setDocumentsLoading(false);
        }
      }
    };

    void loadDocuments();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    void refreshSimilarAssessments();
  }, [refreshSimilarAssessments]);

  useEffect(() => {
    setSelectedReferenceIds(prev => prev.filter(id => similarAssessments.some(reference => reference.id === id)));
  }, [similarAssessments]);

  useEffect(() => {
    setSelectedDocumentSources(prev =>
      prev.filter(source => availableDocuments.some(document => document.source === source))
    );
  }, [availableDocuments]);

  useEffect(() => {
    if (!selectedTemplate || typeof selectedTemplate !== 'number') {
      setTemplateColumns([]);
      return;
    }

    let cancelled = false;
    const fetchTemplate = async () => {
      try {
        const res = await apiFetch(`/api/templates/${selectedTemplate}`);
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectTemplate = await res.json();
        if (!cancelled) {
          setTemplateColumns(data.estimationColumns ?? []);
        }
      } catch (err) {
        if (!cancelled) {
          setTemplateColumns([]);
          const message = err instanceof Error ? err.message : 'Failed to load template details.';
          showError(message, 'Template load failed');
        }
      }
    };

    void fetchTemplate();

    return () => {
      cancelled = true;
    };
  }, [selectedTemplate, showError]);

  const loadAssessment = useCallback(
    async (id: number) => {
      stopJobPolling();
      lastJobStatusRef.current = null;
      setActiveJob(null);
      setIsAnalyzing(false);
      setProgress(0);
      setAnalysisLog([]);
      setLoadingAssessment(true);
      try {
        const res = await apiFetch(`/api/assessment/${id}`);
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectAssessment = await res.json();
        setAssessment(normalizeAssessment(data));
        setSelectedTemplate(data.templateId);
        setProjectTitle(data.projectName);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to load the assessment.';
        showError(message, 'Unable to load assessment');
      } finally {
        setLoadingAssessment(false);
      }
    },
    [showError, stopJobPolling]
  );

  useEffect(() => {
    if (!router.isReady) return;

    const jobParam = router.query.jobId;
    if (jobParam) {
      const jobId = Array.isArray(jobParam) ? Number(jobParam[0]) : Number(jobParam);
      if (Number.isFinite(jobId)) {
        loadJob(jobId);
        router.replace('/pre-sales/workspace', undefined, { shallow: true });
        return;
      }
    }

    const assessmentParam = router.query.assessmentId;
    if (assessmentParam) {
      const id = Array.isArray(assessmentParam) ? Number(assessmentParam[0]) : Number(assessmentParam);
      if (Number.isFinite(id)) {
        loadAssessment(id);
        router.replace('/pre-sales/workspace', undefined, { shallow: true });
      }
    }
  }, [loadAssessment, loadJob, router]);

  useEffect(() => {
    if (!router.isReady) return;
    if (router.query.jobId || router.query.assessmentId) return;
    if (outstandingJobCheckRef.current) return;
    outstandingJobCheckRef.current = true;

    const checkOutstandingJob = async () => {
      try {
        const res = await apiFetch('/api/assessment/jobs');
        if (!res.ok) return;
        const data: AssessmentJobSummary[] = await res.json();
        if (!Array.isArray(data) || data.length === 0) return;
        const outstanding = data.find(job => job?.status && isOutstandingJobStatus(job.status));
        if (!outstanding) return;
        await loadJob(outstanding.id);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to check for in-progress assessments.';
        showError(message, 'Unable to resume assessment');
      }
    };

    void checkOutstandingJob();
  }, [loadJob, router, showError]);

  useEffect(() => {
    if (!assessment) return;
    const defaults: Record<string, boolean> = {};
    assessment.sections.forEach(section => {
      defaults[section.sectionName] = true;
    });
    setExpandedSections(defaults);
  }, [assessment]);

  const estimationColumns = useMemo(() => {
    if (!assessment) return [];
    const seen = new Set<string>();
    const collected: string[] = [];
    assessment.sections.forEach(section => {
      section.items.forEach(item => {
        Object.keys(item.estimates ?? {}).forEach(column => {
          if (!seen.has(column)) {
            seen.add(column);
            collected.push(column);
          }
        });
      });
    });

    if (templateColumns.length === 0) {
      return collected;
    }

    const ordered = templateColumns.filter(column => seen.has(column));
    const extras = collected.filter(column => !templateColumns.includes(column));
    return [...ordered, ...extras];
  }, [assessment, templateColumns]);

  const computeItemTotal = (item: AssessmentItem) =>
    Object.values(item.estimates).reduce<number>((total, value) => total + (value ?? 0), 0);

  const grandTotal = useMemo(() => {
    if (!assessment) return 0;
    return assessment.sections.reduce(
      (sum, section) => sum + section.items.reduce((sectionSum, item) => sectionSum + computeItemTotal(item), 0),
      0
    );
  }, [assessment]);

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const nextFile = event.target.files?.[0] ?? null;
    setFile(nextFile);
  };

  const handleReferenceChange = (event: SelectChangeEvent<string[]>) => {
    const value = event.target.value;
    const ids = (typeof value === 'string' ? value.split(',') : value).map(item => Number(item));
    setSelectedReferenceIds(ids.filter(id => Number.isFinite(id)));
  };

  const toggleSection = (sectionName: string) => {
    setExpandedSections(prev => ({
      ...prev,
      [sectionName]: prev[sectionName] === false,
    }));
  };

  const updateAssessment = (updater: (current: ProjectAssessment) => ProjectAssessment) => {
    setAssessment(prev => (prev ? normalizeAssessment(updater(prev)) : prev));
  };

  const updateItem = (sectionIndex: number, itemIndex: number, updater: (item: AssessmentItem) => AssessmentItem) => {
    updateAssessment(current => ({
      ...current,
      sections: current.sections.map((section, index) => {
        if (index !== sectionIndex) return section;
        return {
          ...section,
          items: section.items.map((item, idx) => (idx === itemIndex ? updater(item) : item)),
        };
      }),
    }));
  };

  const onEstimateChange = (sectionIndex: number, itemIndex: number, column: string, value: number | null) => {
    updateItem(sectionIndex, itemIndex, item => ({
      ...item,
      estimates: { ...item.estimates, [column]: value },
    }));
  };

  const onItemNameChange = (sectionIndex: number, itemIndex: number, value: string) => {
    updateItem(sectionIndex, itemIndex, item => ({
      ...item,
      itemName: value,
    }));
  };

  const onItemDetailChange = (sectionIndex: number, itemIndex: number, value: string) => {
    updateItem(sectionIndex, itemIndex, item => ({
      ...item,
      itemDetail: value,
    }));
  };

  const onItemCategoryChange = (sectionIndex: number, itemIndex: number, value: CategoryOption) => {
    updateItem(sectionIndex, itemIndex, item => ({
      ...item,
      category: value,
    }));
  };

  const onRemoveItem = (sectionIndex: number, itemIndex: number) => {
    updateAssessment(current => ({
      ...current,
      sections: current.sections.map((section, index) => {
        if (index !== sectionIndex) return section;
        return {
          ...section,
          items: section.items.filter((_, idx) => idx !== itemIndex),
        };
      }),
    }));
  };

  const onAddItem = (sectionIndex: number) => {
    updateAssessment(current => {
      const columns = estimationColumns;
      const newItem: AssessmentItem = {
        itemId: `custom-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
        itemName: '',
        itemDetail: '',
        category: DEFAULT_CATEGORY,
        isNeeded: true,
        estimates: columns.reduce<Record<string, number | null>>((acc, column) => {
          acc[column] = null;
          return acc;
        }, {}),
      };

      return {
        ...current,
        sections: current.sections.map((section, index) => {
          if (index !== sectionIndex) return section;
          return {
            ...section,
            items: [...section.items, newItem],
          };
        }),
      };
    });
  };

  const startAnalysis = async () => {
    if (!selectedTemplate || !file) {
      showError('Select a template and upload a scope document first.', 'Missing information');
      return;
    }
    stopJobPolling();
    lastJobStatusRef.current = null;
    setAssessment(null);
    setIsAnalyzing(true);
    setAnalysisLog(['Uploading scope document…', 'Waiting for background processor to start…']);
    setProgress(15);
    try {
      const formData = new FormData();
      formData.append('templateId', String(selectedTemplate));
      formData.append('projectName', projectTitle.trim());
      formData.append('file', file);
      selectedReferenceIds.forEach(id => {
        formData.append('referenceAssessmentIds', String(id));
      });
      selectedDocumentSources.forEach(source => {
        formData.append('referenceDocumentSources', source);
      });
      formData.append('analysisMode', analysisMode === 'strict' ? 'Strict' : 'Interpretive');
      formData.append('outputLanguage', outputLanguage === 'english' ? 'English' : 'Indonesian');
      const res = await apiFetch('/api/assessment/analyze', { method: 'POST', body: formData });
      if (!res.ok) throw new Error(await res.text());
      const job: AssessmentJob = await res.json();
      const trimmedTitle = projectTitle.trim();
      if (trimmedTitle !== projectTitle) {
        setProjectTitle(trimmedTitle);
      }
      applyJobStatus(job);
      setSelectedTemplate(job.templateId);
      if (isTerminalJobStatus(job.status)) {
        if (job.status === 'Complete') {
          await loadAssessmentFromJob(job.id);
        }
      } else {
        jobPollTimer.current = setTimeout(() => {
          void refreshJobStatus(job.id);
        }, 2000);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to analyze the scope document.';
      showError(message, 'Analysis failed');
      setProgress(0);
      setIsAnalyzing(false);
      setActiveJob(null);
    }
  };

  const saveAssessment = async () => {
    if (!assessment) return;
    setSaving(true);
    try {
      const res = await apiFetch('/api/assessment/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(assessment),
      });
      if (!res.ok) throw new Error(await res.text());
      const saved: ProjectAssessment = await res.json();
      setAssessment(normalizeAssessment(saved));
      setProjectTitle(saved.projectName);
      showSuccess('Assessment saved', 'Assessment saved successfully.');
      void refreshSimilarAssessments();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to save the assessment.';
      showError(message, 'Save failed');
    } finally {
      setSaving(false);
    }
  };

  const exportAssessment = async () => {
    if (!assessment?.id) return;
    try {
      const res = await apiFetch(`/api/assessment/${assessment.id}/export`);
      if (!res.ok) throw new Error(await res.text());
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `assessment-${assessment.id}.xlsx`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to export the assessment.';
      showError(message, 'Export failed');
    }
  };

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Box>
        <Typography variant="h1" gutterBottom>Pre-Sales Assessment Workspace</Typography>
        <Typography variant="body1" color="text.secondary">
          Launch AI-assisted project assessments, review the results, and refine estimates collaboratively.
        </Typography>
      </Box>

      <Card>
        <CardHeader title="New Analysis" subheader="Provide the scope document and template to generate an initial assessment." />
        {isAnalyzing && <LinearProgress value={progress} variant={progress ? 'determinate' : 'indeterminate'} />}
        <CardContent>
          <Stack spacing={3}>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
              <FormControl fullWidth>
                <InputLabel id="template-select-label">Project Template</InputLabel>
                <Select
                  labelId="template-select-label"
                  label="Project Template"
                  value={selectedTemplate}
                  onChange={event => setSelectedTemplate(event.target.value ? Number(event.target.value) : '')}
                >
                  <MenuItem value="">Select a template</MenuItem>
                  {templates.map(template => (
                    <MenuItem key={template.id} value={template.id}>
                      {template.templateName}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
              <TextField
                fullWidth
                label="Project Name"
                placeholder="Input Project Name"
                value={projectTitle}
                onChange={event => setProjectTitle(event.target.value)}
              />
            </Stack>

            <FormControl
              fullWidth
              disabled={similarAssessments.length === 0}
            >
              <InputLabel id="reference-select-label">Reference Assessments</InputLabel>
              <Select
                labelId="reference-select-label"
                multiple
                value={selectedReferenceIds.map(id => String(id))}
                onChange={handleReferenceChange}
                label="Reference Assessments"
                displayEmpty
                renderValue={selected => {
                  const values = Array.isArray(selected) ? selected : [];
                  if (values.length === 0) {
                    return '';
                  }
                  const names = values
                    .map(value => {
                      const reference = similarAssessments.find(item => item.id === Number(value));
                      return reference?.projectName || `Assessment ${value}`;
                    })
                    .filter(Boolean);
                  return names.join(', ');
                }}
              >
                {similarAssessments.map(reference => (
                  <MenuItem key={reference.id} value={String(reference.id)}>
                    <ListItemIcon>
                      <Checkbox edge="start" checked={selectedReferenceIds.includes(reference.id)} />
                    </ListItemIcon>
                    <ListItemText
                      primary={reference.projectName || 'Untitled Assessment'}
                      secondary={`${reference.templateName} • ${formatHours(reference.totalHours)} hrs`}
                    />
                  </MenuItem>
                ))}
              </Select>
              <FormHelperText>
                Choose saved assessments to include as context for the AI estimation.
              </FormHelperText>
            </FormControl>

            <Autocomplete
              multiple
              options={availableDocuments}
              value={availableDocuments.filter(document => selectedDocumentSources.includes(document.source))}
              onChange={(_, newValue) =>
                setSelectedDocumentSources(newValue.map(option => option.source))
              }
              getOptionLabel={option => option.source}
              disableCloseOnSelect
              loading={documentsLoading}
              isOptionEqualToValue={(option, value) => option.source === value.source}
              filterSelectedOptions
              sx={{ width: '100%' }}
              renderOption={(props, option, { selected }) => (
                <li {...props}>
                  <Checkbox
                    edge="start"
                    checked={selected}
                    tabIndex={-1}
                    disableRipple
                    sx={{ mr: 1 }}
                  />
                  <ListItemText
                    primary={option.source}
                    secondary={option.hasSummary ? 'Summary available' : `${option.pages} pages`}
                  />
                </li>
              )}
              renderInput={params => (
                <TextField
                  {...params}
                  label="Reference Documents"
                  placeholder="Search documents"
                  error={Boolean(documentsError)}
                  helperText={
                    documentsError ||
                    'Select knowledge base documents to include as additional AI context.'
                  }
                  InputProps={{
                    ...params.InputProps,
                    endAdornment: (
                      <>
                        {documentsLoading ? <CircularProgress color="inherit" size={20} /> : null}
                        {params.InputProps.endAdornment}
                      </>
                    ),
                  }}
                />
              )}
            />

            <Paper variant="outlined" sx={{ p: 2 }}>
              <Stack
                direction={{ xs: 'column', lg: 'row' }}
                spacing={3}
                alignItems={{ xs: 'stretch', lg: 'center' }}
              >
                <Button
                  component="label"
                  variant="outlined"
                  startIcon={<UploadFileIcon />}
                  sx={{ alignSelf: { xs: 'stretch', lg: 'flex-start' } }}
                >
                  {file ? file.name : 'Upload scope document'}
                  <input hidden type="file" accept=".pdf,.doc,.docx" onChange={handleFileChange} />
                </Button>

                <Stack
                  direction={{ xs: 'column', md: 'row' }}
                  spacing={3}
                  flex={1}
                >
                  <FormControl component="fieldset" sx={{ flex: 1 }}>
                    <FormLabel component="legend">Analysis mode</FormLabel>
                    <RadioGroup
                      row
                      value={analysisMode}
                      onChange={event => setAnalysisMode(event.target.value as AnalysisModeOption)}
                    >
                      <FormControlLabel
                        value="interpretive"
                        control={<Radio />}
                        label="AI interpret scope"
                      />
                      <FormControlLabel
                        value="strict"
                        control={<Radio />}
                        label="Strict scope only"
                      />
                    </RadioGroup>
                    <FormHelperText>
                      Choose whether the AI can infer new items or must mirror the scope document.
                    </FormHelperText>
                  </FormControl>

                  <FormControl component="fieldset" sx={{ flex: 1 }}>
                    <FormLabel component="legend">Output language</FormLabel>
                    <RadioGroup
                      row
                      value={outputLanguage}
                      onChange={event =>
                        setOutputLanguage(event.target.value as OutputLanguageOption)
                      }
                    >
                      <FormControlLabel
                        value="indonesian"
                        control={<Radio />}
                        label="Bahasa Indonesia"
                      />
                      <FormControlLabel value="english" control={<Radio />} label="English" />
                    </RadioGroup>
                    <FormHelperText>
                      AI-generated items, descriptions, and notes will use the selected language.
                    </FormHelperText>
                  </FormControl>
                </Stack>

                <LoadingButton
                  variant="contained"
                  onClick={startAnalysis}
                  loading={isAnalyzing}
                  disabled={!selectedTemplate || !file}
                  sx={{ alignSelf: { xs: 'stretch', lg: 'flex-start' }, minWidth: { lg: 180 } }}
                >
                  Start Analysis
                </LoadingButton>
              </Stack>
            </Paper>

            {analysisLog.length > 0 && (
              <Stack spacing={1}>
                {analysisLog.map((entry, index) => (
                  <Typography key={`${entry}-${index}`} variant="body2" color="text.secondary">
                    {entry}
                  </Typography>
                ))}
              </Stack>
            )}
          </Stack>
        </CardContent>
      </Card>

      {assessment && (
        <Card>
          <CardHeader
            title="Assessment Results"
            subheader="Adjust the generated estimates and capture final man-hour totals."
          />
          {(saving || loadingAssessment) && <LinearProgress />}
          <CardContent>
            <Stack spacing={3}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
                <TextField
                  fullWidth
                  label="Project Name"
                  value={assessment.projectName}
                  onChange={event =>
                    setAssessment(prev => (prev ? normalizeAssessment({ ...prev, projectName: event.target.value }) : prev))
                  }
                />
                <FormControl sx={{ minWidth: 200 }}>
                  <InputLabel id="status-select-label">Status</InputLabel>
                  <Select
                    labelId="status-select-label"
                    label="Status"
                    value={assessment.status}
                    onChange={event =>
                      setAssessment(prev =>
                        prev
                          ? normalizeAssessment({ ...prev, status: event.target.value as AssessmentStatus })
                          : prev
                      )
                    }
                  >
                    {statusOptions.map(status => (
                      <MenuItem key={status} value={status}>
                        {status}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
              </Stack>

              <Divider />

              <AssessmentTreeGrid
                sections={assessment.sections}
                estimationColumns={estimationColumns}
                expandedSections={expandedSections}
                onToggleSection={toggleSection}
                onEstimateChange={onEstimateChange}
                onItemNameChange={onItemNameChange}
                onItemDetailChange={onItemDetailChange}
                onItemCategoryChange={onItemCategoryChange}
                onRemoveItem={onRemoveItem}
                onAddItem={onAddItem}
                computeItemTotal={computeItemTotal}
              />

              <Stack
                direction={{ xs: 'column', md: 'row' }}
                spacing={2}
                justifyContent="space-between"
                alignItems={{ xs: 'flex-start', md: 'center' }}
              >
                <Stack spacing={1}>
                  <Typography variant="subtitle2" color="text.secondary">Grand Total Man-hours</Typography>
                  <Typography variant="h4">{formatHours(grandTotal)}</Typography>
                </Stack>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
                  <LoadingButton
                    variant="contained"
                    onClick={saveAssessment}
                    loading={saving}
                  >
                    Save Assessment
                  </LoadingButton>
                  <Button
                    variant="outlined"
                    startIcon={<GetAppIcon />}
                    onClick={exportAssessment}
                    disabled={!assessment.id}
                  >
                    Export to Excel
                  </Button>
                </Stack>
              </Stack>
            </Stack>
          </CardContent>
        </Card>
      )}

    </Box>
  );
}
