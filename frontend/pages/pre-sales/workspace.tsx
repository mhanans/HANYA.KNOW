import { ChangeEvent, useEffect, useMemo, useState } from 'react';
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
  Checkbox,
  Divider,
  FormControl,
  InputLabel,
  LinearProgress,
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
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import GetAppIcon from '@mui/icons-material/GetApp';
import { LoadingButton } from '@mui/lab';
import AssessmentHistory, { AssessmentStatus } from '../../components/pre-sales/AssessmentHistory';
import { apiFetch } from '../../lib/api';

interface ProjectTemplateMetadata {
  id: number;
  templateName: string;
}

interface AssessmentItem {
  itemId: string;
  itemName: string;
  itemDetail?: string;
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
  sections: AssessmentSection[];
  createdAt?: string;
  lastModifiedAt?: string;
}

const statusOptions: AssessmentStatus[] = ['Draft', 'Completed'];

const formatHours = (value: number) => value.toLocaleString(undefined, { maximumFractionDigits: 2 });

interface AssessmentTreeGridProps {
  sections: AssessmentSection[];
  estimationColumns: string[];
  expandedSections: Record<string, boolean>;
  onToggleSection: (name: string) => void;
  onToggleNeeded: (sectionIndex: number, itemIndex: number, value: boolean) => void;
  onEstimateChange: (sectionIndex: number, itemIndex: number, column: string, value: number | null) => void;
  computeItemTotal: (item: AssessmentItem) => number;
}

function AssessmentTreeGrid({
  sections,
  estimationColumns,
  expandedSections,
  onToggleSection,
  onToggleNeeded,
  onEstimateChange,
  computeItemTotal,
}: AssessmentTreeGridProps) {
  return (
    <Stack spacing={2}>
      {sections.map((section, sectionIndex) => {
        const isExpanded = expandedSections[section.sectionName] !== false;
        const columnTotals = estimationColumns.reduce<Record<string, number>>((acc, column) => {
          acc[column] = section.items.reduce((sum, item) => {
            if (!item.isNeeded) return sum;
            return sum + (item.estimates[column] ?? 0);
          }, 0);
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
                      <TableCell padding="checkbox">Needed</TableCell>
                      <TableCell>Item ID</TableCell>
                      <TableCell>Item Name</TableCell>
                      <TableCell>Detail</TableCell>
                      {estimationColumns.map(column => (
                        <TableCell key={column} align="right">{column}</TableCell>
                      ))}
                      <TableCell align="right">Total Hours</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {section.items.map((item, itemIndex) => {
                      const itemTotal = computeItemTotal(item);
                      return (
                        <TableRow key={`${section.sectionName}-${item.itemId}-${itemIndex}`}>
                          <TableCell padding="checkbox">
                            <Checkbox
                              checked={item.isNeeded}
                              onChange={event => onToggleNeeded(sectionIndex, itemIndex, event.target.checked)}
                            />
                          </TableCell>
                          <TableCell>{item.itemId}</TableCell>
                          <TableCell>{item.itemName}</TableCell>
                          <TableCell>{item.itemDetail}</TableCell>
                          {estimationColumns.map(column => (
                            <TableCell key={column} align="right">
                              <TextField
                                size="small"
                                type="number"
                                inputProps={{ min: 0, step: 0.25 }}
                                disabled={!item.isNeeded}
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
                        </TableRow>
                      );
                    })}
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
  const [expandedSections, setExpandedSections] = useState<Record<string, boolean>>({});
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [saving, setSaving] = useState(false);
  const [historyRefresh, setHistoryRefresh] = useState(0);

  useEffect(() => {
    const loadTemplates = async () => {
      try {
        const res = await apiFetch('/api/templates');
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectTemplateMetadata[] = await res.json();
        setTemplates(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load templates.');
      }
    };
    loadTemplates();
  }, []);

  useEffect(() => {
    const idParam = router.query.assessmentId;
    if (!idParam) return;
    const id = Array.isArray(idParam) ? Number(idParam[0]) : Number(idParam);
    if (!Number.isFinite(id)) return;
    loadAssessment(id);
    router.replace('/pre-sales/workspace', undefined, { shallow: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router.query.assessmentId]);

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
    const firstItem = assessment.sections.flatMap(section => section.items)[0];
    return firstItem ? Object.keys(firstItem.estimates) : [];
  }, [assessment]);

  const computeItemTotal = (item: AssessmentItem) => {
    if (!item.isNeeded) return 0;
    return Object.values(item.estimates).reduce<number>((total, value) => total + (value ?? 0), 0);
  };

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

  const toggleSection = (sectionName: string) => {
    setExpandedSections(prev => ({
      ...prev,
      [sectionName]: prev[sectionName] === false,
    }));
  };

  const updateAssessment = (updater: (current: ProjectAssessment) => ProjectAssessment) => {
    setAssessment(prev => (prev ? updater(prev) : prev));
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

  const onToggleNeeded = (sectionIndex: number, itemIndex: number, value: boolean) => {
    updateItem(sectionIndex, itemIndex, item => ({
      ...item,
      isNeeded: value,
    }));
  };

  const onEstimateChange = (sectionIndex: number, itemIndex: number, column: string, value: number | null) => {
    updateItem(sectionIndex, itemIndex, item => ({
      ...item,
      estimates: { ...item.estimates, [column]: value },
    }));
  };

  const startAnalysis = async () => {
    if (!selectedTemplate || !file) {
      setError('Select a template and upload a scope document first.');
      return;
    }
    setError('');
    setNotice('');
    setIsAnalyzing(true);
    setAnalysisLog(['Uploading scope document…']);
    setProgress(10);
    try {
      const formData = new FormData();
      formData.append('templateId', String(selectedTemplate));
      formData.append('projectName', projectTitle.trim());
      formData.append('file', file);
      const res = await apiFetch('/api/assessment/analyze', { method: 'POST', body: formData });
      if (!res.ok) throw new Error(await res.text());
      const data: ProjectAssessment = await res.json();
      const enriched: ProjectAssessment = {
        ...data,
        projectName: projectTitle.trim(),
        status: 'Draft',
      };
      setAssessment(enriched);
      setAnalysisLog(['Uploading scope document…', 'Running AI estimation…', 'Analysis complete.']);
      setProgress(100);
      setNotice('AI analysis is complete. Review the results and adjust the estimates before saving.');
    } catch (err) {
      setAnalysisLog(prev => [...prev, 'Analysis failed.']);
      setError(err instanceof Error ? err.message : 'Failed to analyze the scope document.');
      setProgress(0);
    } finally {
      setIsAnalyzing(false);
    }
  };

  const saveAssessment = async () => {
    if (!assessment) return;
    setSaving(true);
    setError('');
    setNotice('');
    try {
      const res = await apiFetch('/api/assessment/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(assessment),
      });
      if (!res.ok) throw new Error(await res.text());
      const saved: ProjectAssessment = await res.json();
      setAssessment(saved);
      setProjectTitle(saved.projectName);
      setNotice('Assessment saved successfully.');
      setHistoryRefresh(token => token + 1);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save the assessment.');
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
      setError(err instanceof Error ? err.message : 'Failed to export the assessment.');
    }
  };

  const loadAssessment = async (id: number) => {
    setLoadingAssessment(true);
    setError('');
    setNotice('');
    try {
      const res = await apiFetch(`/api/assessment/${id}`);
      if (!res.ok) throw new Error(await res.text());
      const data: ProjectAssessment = await res.json();
      setAssessment(data);
      setSelectedTemplate(data.templateId);
      setProjectTitle(data.projectName);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load the assessment.');
    } finally {
      setLoadingAssessment(false);
    }
  };

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Box>
        <Typography variant="h1" gutterBottom>Assessment Workspace</Typography>
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
                placeholder="AI Discovery Sprint"
                value={projectTitle}
                onChange={event => setProjectTitle(event.target.value)}
              />
            </Stack>

            <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems="center">
              <Button
                component="label"
                variant="outlined"
                startIcon={<UploadFileIcon />}
              >
                {file ? file.name : 'Upload scope document'}
                <input hidden type="file" accept=".pdf,.doc,.docx" onChange={handleFileChange} />
              </Button>
              <Box flexGrow={1} />
              <LoadingButton
                variant="contained"
                onClick={startAnalysis}
                loading={isAnalyzing}
                disabled={!selectedTemplate || !file}
              >
                Start Analysis
              </LoadingButton>
            </Stack>

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

      {error && <Alert severity="error">{error}</Alert>}
      {notice && <Alert severity="success">{notice}</Alert>}

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
                    setAssessment(prev => (prev ? { ...prev, projectName: event.target.value } : prev))
                  }
                />
                <FormControl sx={{ minWidth: 200 }}>
                  <InputLabel id="status-select-label">Status</InputLabel>
                  <Select
                    labelId="status-select-label"
                    label="Status"
                    value={assessment.status}
                    onChange={event =>
                      setAssessment(prev => (prev ? { ...prev, status: event.target.value as AssessmentStatus } : prev))
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
                onToggleNeeded={onToggleNeeded}
                onEstimateChange={onEstimateChange}
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

      <AssessmentHistory
        refreshToken={historyRefresh}
        onSelect={loadAssessment}
      />
    </Box>
  );
}
