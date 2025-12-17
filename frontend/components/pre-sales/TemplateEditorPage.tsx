import { useRouter } from 'next/router';
import { useEffect, useMemo, useState } from 'react';
import Swal from 'sweetalert2';
import type { DragEvent as ReactDragEvent, MouseEvent as ReactMouseEvent } from 'react';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Chip,
  SelectChangeEvent,
  Divider,
  FormControl,
  IconButton,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Stepper,
  Step,
  StepLabel,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import { LoadingButton } from '@mui/lab';
import AddIcon from '@mui/icons-material/Add';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import CloseIcon from '@mui/icons-material/Close';
import DeleteIcon from '@mui/icons-material/Delete';
import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { apiFetch } from '../../lib/api';
import TemplateTimelineEditor, { TimelinePhase } from './TemplateTimelineEditor';
import TemplateColumnMapper from './TemplateColumnMapper';

const CATEGORY_OPTIONS = [
  'New UI',
  'New Interface',
  'New Backgrounder',
  'Adjust Existing UI',
  'Adjust Existing Logic',
] as const;

type CategoryOption = (typeof CATEGORY_OPTIONS)[number];
const DEFAULT_CATEGORY: CategoryOption = CATEGORY_OPTIONS[0];

const normalizeCategory = (value?: string | null): CategoryOption => {
  if (!value) return DEFAULT_CATEGORY;
  const trimmed = value.trim();
  if (!trimmed) return DEFAULT_CATEGORY;
  const match = CATEGORY_OPTIONS.find(option => option.toLowerCase() === trimmed.toLowerCase());
  return match ?? DEFAULT_CATEGORY;
};

interface TemplateItem {
  itemId: string;
  itemName: string;
  itemDetail: string;
  category: CategoryOption;
  uid?: string;
  effort?: string[];
}

interface TemplateSection {
  sectionName: string;
  type: string;
  items: TemplateItem[];
  uid?: string;
  effort?: string[];
}

interface ProjectTemplate {
  id?: number;
  templateName: string;
  estimationColumns: string[];
  sections: TemplateSection[];
  timelinePhases: TimelinePhase[];
  effortRoleMapping?: Record<string, string>;
}

const createEmptyTemplate = (): ProjectTemplate => ({
  templateName: '',
  estimationColumns: [],
  sections: [],
  timelinePhases: [],
  effortRoleMapping: {},
});

const generateUid = () => Math.random().toString(36).slice(2, 10);

const generateItemId = () => {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }
  return `item-${generateUid()}`;
};

const withGeneratedIds = (template: ProjectTemplate): ProjectTemplate => ({
  ...template,
  sections: (template.sections ?? []).map(section => ({
    ...section,
    uid: section.uid ?? generateUid(),
    items: (section.items ?? []).map(item => ({
      ...item,
      category: normalizeCategory(item.category),
      uid: item.uid ?? generateUid(),
      effort: (item as any).effort ?? (item as any).roles ?? (item as any).Roles ?? [],
    })),
    effort: (section as any).effort ?? (section as any).roles ?? (section as any).Roles ?? [],
  })),
  timelinePhases: (template.timelinePhases ?? []).map(phase => ({
    ...phase,
    id: phase.id ?? generateItemId(),
  })),
});

interface TemplateEditorPageProps {
  templateId?: number;
  mode: 'create' | 'edit';
}

export default function TemplateEditorPage({ templateId, mode }: TemplateEditorPageProps) {
  const router = useRouter();
  const isCreate = mode === 'create';

  const [template, setTemplate] = useState<ProjectTemplate>(createEmptyTemplate);
  const [loading, setLoading] = useState(!isCreate);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [dirty, setDirty] = useState(false);
  const [columnInput, setColumnInput] = useState('');
  const [expandedSections, setExpandedSections] = useState<Set<string>>(new Set());
  const [activeStep, setActiveStep] = useState(0);
  const [dragSectionUid, setDragSectionUid] = useState<string | null>(null);
  const [dragSectionOverUid, setDragSectionOverUid] = useState<string | null>(null);
  const [dragItemRef, setDragItemRef] = useState<{ sectionUid: string; itemUid: string } | null>(null);
  const [availableRoles, setAvailableRoles] = useState<string[]>([]);

  useEffect(() => {
    // Fetch roles for mapping
    apiFetch('/api/presales/config')
      .then(res => res.json())
      .then(data => {
        const roles = data.roles?.map((r: any) => r.roleName) || [];
        setAvailableRoles(Array.from(new Set(roles)));
      })
      .catch(console.error);
  }, []);

  const [dragItemOverRef, setDragItemOverRef] = useState<
    { sectionUid: string; itemUid: string | 'end' } | null
  >(null);

  useEffect(() => {
    if (isCreate) {
      setTemplate(createEmptyTemplate());
      setLoading(false);
      setDirty(false);
      return;
    }
    if (!templateId) {
      setLoading(true);
      return;
    }
    const load = async () => {
      setLoading(true);
      setError('');
      try {
        const res = await apiFetch(`/api/templates/${templateId}`);
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectTemplate = await res.json();
        const next = withGeneratedIds({
          id: data.id,
          templateName: data.templateName,
          estimationColumns: data.estimationColumns ?? [],
          sections: data.sections ?? [],
          timelinePhases: data.timelinePhases ?? [],
          effortRoleMapping: data.effortRoleMapping ?? {},
        });
        setTemplate(next);
        setDirty(false);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to load template';
        setError(message);
        await Swal.fire({
          icon: 'error',
          title: 'Unable to load template',
          text: message,
        });
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [isCreate, templateId]);

  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (dirty) {
        e.preventDefault();
        e.returnValue = '';
      }
    };
    if (typeof window !== 'undefined') {
      window.addEventListener('beforeunload', handler);
      return () => window.removeEventListener('beforeunload', handler);
    }
  }, [dirty]);

  useEffect(() => {
    setExpandedSections(prev => {
      const next = new Set<string>();
      template.sections.forEach(section => {
        if (!section.uid) return;
        if (prev.has(section.uid)) {
          next.add(section.uid);
        }
      });
      if (template.sections.length > prev.size) {
        template.sections.forEach(section => {
          if (!section.uid) return;
          if (!prev.has(section.uid)) {
            next.add(section.uid);
          }
        });
      }
      return next;
    });
  }, [template.sections]);

  const updateTemplate = (updater: (current: ProjectTemplate) => ProjectTemplate) => {
    setTemplate(prev => {
      const next = updater(prev);
      const normalised = withGeneratedIds(next);
      if (normalised !== prev) setDirty(true);
      return normalised;
    });
  };

  const addColumn = () => {
    if (!columnInput.trim()) return;
    const value = columnInput.trim();
    updateTemplate(prev => ({
      ...prev,
      estimationColumns: [...prev.estimationColumns, value],
    }));
    setColumnInput('');
  };

  const moveColumn = (index: number, delta: number) => {
    updateTemplate(prev => {
      const next = [...prev.estimationColumns];
      const target = index + delta;
      if (target < 0 || target >= next.length) return prev;
      [next[index], next[target]] = [next[target], next[index]];
      return { ...prev, estimationColumns: next };
    });
  };

  const removeColumn = (index: number) => {
    updateTemplate(prev => ({
      ...prev,
      estimationColumns: prev.estimationColumns.filter((_, i) => i !== index),
    }));
  };

  const addSection = () => {
    updateTemplate(prev => ({
      ...prev,
      sections: [
        ...prev.sections,
        { sectionName: 'New Section', type: 'Project-Level', items: [], uid: generateUid() },
      ],
    }));
  };

  const updateSection = (index: number, updater: (section: TemplateSection) => TemplateSection) => {
    updateTemplate(prev => {
      const sections = prev.sections.map((section, idx) => (idx === index ? updater(section) : section));
      return { ...prev, sections };
    });
  };

  const updateSectionByUid = (uid: string, updater: (section: TemplateSection) => TemplateSection) => {
    const sectionIndex = template.sections.findIndex(section => section.uid === uid);
    if (sectionIndex === -1) return;
    updateSection(sectionIndex, updater);
  };

  const moveSection = (index: number, delta: number) => {
    updateTemplate(prev => {
      const sections = [...prev.sections];
      const target = index + delta;
      if (target < 0 || target >= sections.length) return prev;
      [sections[index], sections[target]] = [sections[target], sections[index]];
      return { ...prev, sections };
    });
  };

  const removeSection = (index: number) => {
    updateTemplate(prev => ({
      ...prev,
      sections: prev.sections.filter((_, i) => i !== index),
    }));
  };



  const addItem = (sectionIndex: number) => {
    updateSection(sectionIndex, section => ({
      ...section,
      items: [
        ...section.items,
        {
          itemId: generateItemId(),
          itemName: 'New Item',
          itemDetail: '',
          category: DEFAULT_CATEGORY,
          uid: generateUid(),
        },
      ],
    }));
  };

  const updateItem = (sectionIndex: number, itemIndex: number, updater: (item: TemplateItem) => TemplateItem) => {
    updateSection(sectionIndex, section => ({
      ...section,
      items: section.items.map((item, idx) => (idx === itemIndex ? updater(item) : item)),
    }));
  };

  const moveItem = (sectionIndex: number, itemIndex: number, delta: number) => {
    updateSection(sectionIndex, section => {
      const items = [...section.items];
      const target = itemIndex + delta;
      if (target < 0 || target >= items.length) return section;
      [items[itemIndex], items[target]] = [items[target], items[itemIndex]];
      return { ...section, items };
    });
  };

  const removeItem = (sectionIndex: number, itemIndex: number) => {
    updateSection(sectionIndex, section => ({
      ...section,
      items: section.items.filter((_, idx) => idx !== itemIndex),
    }));
  };

  const toggleSection = (uid?: string) => {
    if (!uid) return;
    setExpandedSections(prev => {
      const next = new Set(prev);
      if (next.has(uid)) {
        next.delete(uid);
      } else {
        next.add(uid);
      }
      return next;
    });
  };

  const canSave = useMemo(() => template.templateName.trim().length > 0, [template.templateName]);

  const preparePayload = (input: ProjectTemplate) => ({
    ...input,
    sections: input.sections.map(section => ({
      sectionName: section.sectionName,
      type: section.type,
      items: section.items.map(item => ({
        itemId: item.itemId,
        itemName: item.itemName,
        itemDetail: item.itemDetail,
        category: item.category,
        effort: item.effort || [],
      })),
      effort: section.effort || [],
    })),
    timelinePhases: input.timelinePhases,
    effortRoleMapping: input.effortRoleMapping || {},
  });

  const handleSectionDragStart = (uid?: string) => (event: ReactDragEvent) => {
    if (!uid) {
      event.preventDefault();
      return;
    }
    const handle = (event.target as HTMLElement).closest('.section-drag-handle');
    if (!handle) {
      event.preventDefault();
      return;
    }
    setDragSectionUid(uid);
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', uid);
  };

  const handleSectionDragOver = (uid?: string) => (event: ReactDragEvent) => {
    if (!dragSectionUid || !uid || dragSectionUid === uid) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    if (dragSectionOverUid !== uid) {
      setDragSectionOverUid(uid);
    }
  };

  const handleSectionDragLeave = (uid?: string) => () => {
    setDragSectionOverUid(prev => (prev === uid ? null : prev));
  };

  const handleSectionDrop = (uid?: string) => (event: ReactDragEvent) => {
    if (!dragSectionUid || !uid) return;
    event.preventDefault();
    event.stopPropagation();
    setDragSectionOverUid(null);
    const fromIndex = template.sections.findIndex(section => section.uid === dragSectionUid);
    const toIndex = template.sections.findIndex(section => section.uid === uid);
    if (fromIndex === -1 || toIndex === -1 || fromIndex === toIndex) {
      setDragSectionUid(null);
      return;
    }
    updateTemplate(prev => {
      const sections = [...prev.sections];
      const [moved] = sections.splice(fromIndex, 1);
      sections.splice(toIndex, 0, moved);
      return { ...prev, sections };
    });
    setDragSectionUid(null);
  };

  const handleSectionDragEnd = () => {
    setDragSectionUid(null);
    setDragSectionOverUid(null);
  };

  const handleItemDragStart = (sectionUid?: string, itemUid?: string) => (event: ReactDragEvent) => {
    if (!sectionUid || !itemUid) {
      event.preventDefault();
      return;
    }
    const handle = (event.target as HTMLElement).closest('.item-drag-handle');
    if (!handle) {
      event.preventDefault();
      return;
    }
    setDragItemRef({ sectionUid, itemUid });
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', `${sectionUid}:${itemUid}`);
  };

  const handleItemDragOver = (sectionUid?: string, itemUid?: string | 'end') => (event: ReactDragEvent) => {
    if (!dragItemRef || !sectionUid || dragItemRef.sectionUid !== sectionUid) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    const targetUid = itemUid ?? 'end';
    if (!dragItemOverRef || dragItemOverRef.sectionUid !== sectionUid || dragItemOverRef.itemUid !== targetUid) {
      setDragItemOverRef({ sectionUid, itemUid: targetUid });
    }
  };

  const handleItemDragLeave = (sectionUid?: string, itemUid?: string | 'end') => () => {
    setDragItemOverRef(prev => {
      if (!prev) return prev;
      const targetUid = itemUid ?? 'end';
      if (prev.sectionUid === sectionUid && prev.itemUid === targetUid) {
        return null;
      }
      return prev;
    });
  };

  const handleItemDrop = (sectionUid?: string, itemUid?: string | 'end') => (event: ReactDragEvent) => {
    if (!dragItemRef || !sectionUid || dragItemRef.sectionUid !== sectionUid) return;
    event.preventDefault();
    event.stopPropagation();
    const targetUid = itemUid ?? 'end';
    if (targetUid !== 'end' && targetUid === dragItemRef.itemUid) {
      setDragItemOverRef(null);
      setDragItemRef(null);
      return;
    }
    updateSectionByUid(sectionUid, section => {
      const items = [...section.items];
      const fromIndex = items.findIndex(item => item.uid === dragItemRef.itemUid);
      if (fromIndex === -1) return section;
      const [moved] = items.splice(fromIndex, 1);
      const targetIndex = items.findIndex(item => item.uid === targetUid);
      const insertionIndex = targetUid === 'end' ? items.length : targetIndex === -1 ? items.length : targetIndex;
      items.splice(insertionIndex, 0, moved);
      return { ...section, items };
    });
    setDragItemRef(null);
    setDragItemOverRef(null);
  };

  const handleItemDragEnd = () => {
    setDragItemRef(null);
    setDragItemOverRef(null);
  };

  const save = async () => {
    if (!canSave) return;
    setSaving(true);
    setError('');
    try {
      const payload = JSON.stringify(preparePayload(template));
      const url = isCreate ? '/api/templates' : `/api/templates/${templateId}`;
      const method = isCreate ? 'POST' : 'PUT';
      const res = await apiFetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: payload,
      });
      if (!res.ok) {
        throw new Error(await res.text());
      }
      if (isCreate) {
        const created = await res.json();
        setTemplate(withGeneratedIds(created));
        setDirty(false);
        await Swal.fire({
          icon: 'success',
          title: 'Template created',
          text: 'Template created successfully.',
        });
        router.replace('/pre-sales/project-templates');
      } else {
        setDirty(false);
        await Swal.fire({
          icon: 'success',
          title: 'Template saved',
          text: 'Template saved successfully.',
        });
        router.push('/pre-sales/project-templates');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to save template';
      setError(message);
      await Swal.fire({
        icon: 'error',
        title: 'Save failed',
        text: message,
      });
    } finally {
      setSaving(false);
    }
  };

  const cancel = async () => {
    if (dirty) {
      const confirmation = await Swal.fire({
        title: 'Discard unsaved changes?',
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Discard',
        cancelButtonText: 'Keep editing',
        reverseButtons: true,
        confirmButtonColor: '#ef4444',
      });
      if (!confirmation.isConfirmed) return;
    }
    router.push('/pre-sales/project-templates');
  };

  const stopPropagation = (event: ReactMouseEvent) => {
    event.stopPropagation();
  };

  const handleNext = () => {
    setActiveStep(prev => prev + 1);
  };

  const handleBack = () => {
    setActiveStep(prev => prev - 1);
  };

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 6 }}>
      <Stack
        direction={{ xs: 'column', md: 'row' }}
        spacing={3}
        justifyContent="space-between"
        alignItems={{ xs: 'flex-start', md: 'center' }}
      >
        <Box>
          <Typography variant="h1" gutterBottom>
            {isCreate ? 'Create Project Template' : 'Edit Project Template'}
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Build a complete estimation structure with dynamic sections, items, and columns.
          </Typography>
        </Box>
        <Stack direction="row" spacing={2} width={{ xs: '100%', md: 'auto' }}>
          <Button
            variant="outlined"
            color="inherit"
            onClick={cancel}
            sx={{ flexGrow: { xs: 1, md: 0 } }}
          >
            Cancel
          </Button>
          {(activeStep === 3) && (
            <LoadingButton
              variant="contained"
              color="primary"
              onClick={save}
              loading={saving}
              disabled={!canSave}
              sx={{ flexGrow: { xs: 1, md: 0 } }}
            >
              Save Template
            </LoadingButton>
          )}
        </Stack>
      </Stack>

      <Stepper activeStep={activeStep} alternativeLabel>
        <Step>
          <StepLabel>Define Structure</StepLabel>
        </Step>
        <Step>
          <StepLabel>Column Mapping</StepLabel>
        </Step>
        <Step>
          <StepLabel>Define Timeline</StepLabel>
        </Step>
        <Step>
          <StepLabel>Role Mapping</StepLabel>
        </Step>
      </Stepper>

      {loading ? (
        <Paper sx={{ p: 4 }}>
          <Typography variant="body1">Loading template…</Typography>
        </Paper>
      ) : (
        <Stack spacing={4}>
          {activeStep === 0 && (
            <>
              <Paper sx={{ p: { xs: 3, md: 4 } }}>
                <Stack spacing={3}>
                  <TextField
                    label="Template Name"
                    placeholder="Insert Name Here"
                    value={template.templateName}
                    onChange={e => updateTemplate(prev => ({ ...prev, templateName: e.target.value }))}
                    fullWidth
                  />

                  <Box>
                    <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
                      <Typography variant="subtitle1">Estimation Columns</Typography>
                      {template.estimationColumns.length === 0 && (
                        <Typography variant="body2" color="text.secondary">
                          No estimation columns yet.
                        </Typography>
                      )}
                    </Stack>
                    <Box
                      sx={{
                        display: 'flex',
                        gap: 1.5,
                        overflowX: 'auto',
                        pb: 1,
                        pr: 1,
                      }}
                    >
                      {template.estimationColumns.map((column, index) => (
                        <Paper
                          key={`${column}-${index}`}
                          sx={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: 1,
                            px: 2,
                            py: 1,
                            borderRadius: 999,
                            bgcolor: 'rgba(59,130,246,0.15)',
                            border: '1px solid rgba(59,130,246,0.35)',
                            flexShrink: 0,
                            minWidth: 160,
                          }}
                          elevation={0}
                        >
                          <Typography variant="body2" sx={{ fontWeight: 600 }}>
                            {column}
                          </Typography>
                          <Stack direction="row" spacing={0.5}>
                            <Tooltip title="Move left">
                              <span>
                                <IconButton
                                  size="small"
                                  onClick={() => moveColumn(index, -1)}
                                  disabled={index === 0}
                                  sx={{ color: 'text.secondary' }}
                                >
                                  <ArrowUpwardIcon fontSize="inherit" />
                                </IconButton>
                              </span>
                            </Tooltip>
                            <Tooltip title="Move right">
                              <span>
                                <IconButton
                                  size="small"
                                  onClick={() => moveColumn(index, 1)}
                                  disabled={index === template.estimationColumns.length - 1}
                                  sx={{ color: 'text.secondary' }}
                                >
                                  <ArrowDownwardIcon fontSize="inherit" />
                                </IconButton>
                              </span>
                            </Tooltip>
                            <Tooltip title="Remove column">
                              <IconButton
                                size="small"
                                onClick={() => removeColumn(index)}
                                sx={{ color: 'error.light' }}
                              >
                                <CloseIcon fontSize="inherit" />
                              </IconButton>
                            </Tooltip>
                          </Stack>
                        </Paper>
                      ))}
                    </Box>
                    <Stack
                      direction={{ xs: 'column', sm: 'row' }}
                      spacing={2}
                      alignItems={{ xs: 'stretch', sm: 'center' }}
                      mt={3}
                    >
                      <TextField
                        label="Add estimation column"
                        placeholder="e.g. Backend Hours"
                        value={columnInput}
                        onChange={e => setColumnInput(e.target.value)}
                        onKeyDown={e => {
                          if (e.key === 'Enter') {
                            e.preventDefault();
                            addColumn();
                          }
                        }}
                        fullWidth
                      />
                      <Button
                        variant="contained"
                        onClick={addColumn}
                        disabled={!columnInput.trim()}
                        startIcon={<AddIcon />}
                        sx={{ minWidth: 160, height: 56 }}
                      >
                        Add Column
                      </Button>
                    </Stack>
                  </Box>
                </Stack>
              </Paper>

              <Stack spacing={2}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Box>
                    <Typography variant="h2">Sections & Items</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Group the template structure into cards for easier navigation.
                    </Typography>
                  </Box>
                  <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={addSection}>
                    Add Section
                  </Button>
                </Stack>

                {template.sections.length === 0 ? (
                  <Paper sx={{ p: 4, textAlign: 'center', bgcolor: 'transparent', borderStyle: 'dashed' }} variant="outlined">
                    <Typography variant="body1" color="text.secondary">
                      No sections added yet. Click &quot;Add Section&quot; to start.
                    </Typography>
                  </Paper>
                ) : (
                  template.sections.map((section, sectionIndex) => (
                    <Accordion
                      key={section.uid}
                      expanded={expandedSections.has(section.uid || '')}
                      onChange={() => toggleSection(section.uid)}
                      disableGutters
                      sx={{
                        '&:before': { display: 'none' },
                        border: '1px solid',
                        borderColor: 'divider',
                        borderRadius: 1,
                        overflow: 'hidden',
                        mb: 2,
                      }}
                    >
                      <AccordionSummary
                        expandIcon={<ExpandMoreIcon />}
                        sx={{ bgcolor: 'action.hover', px: 2 }}
                      >
                        <Stack direction="row" alignItems="center" spacing={2} width="100%">
                          <Box
                            className="section-drag-handle"
                            draggable
                            onDragStart={handleSectionDragStart(section.uid)}
                            onDragOver={handleSectionDragOver(section.uid)}
                            onDragLeave={handleSectionDragLeave(section.uid)}
                            onDrop={handleSectionDrop(section.uid)}
                            onDragEnd={handleSectionDragEnd}
                            onClick={stopPropagation}
                            sx={{
                              cursor: 'grab',
                              display: 'flex',
                              alignItems: 'center',
                              color: 'text.secondary',
                              '&:hover': { color: 'text.primary' },
                              p: 0.5,
                            }}
                          >
                            <DragIndicatorIcon fontSize="small" />
                          </Box>

                          <TextField
                            label="Section Name"
                            variant="standard"
                            value={section.sectionName}
                            onChange={e => updateSection(sectionIndex, s => ({ ...s, sectionName: e.target.value }))}
                            onClick={stopPropagation}
                            onKeyDown={e => e.stopPropagation()}
                            sx={{ flexGrow: 1 }}
                            InputProps={{ disableUnderline: true, style: { fontSize: '1.1rem', fontWeight: 500 } }}
                          />

                          <FormControl variant="standard" sx={{ minWidth: 120 }}>
                            <InputLabel>Type</InputLabel>
                            <Select
                              value={section.type}
                              onChange={e => updateSection(sectionIndex, s => ({ ...s, type: e.target.value }))}
                              label="Type"
                              onClick={stopPropagation}
                            >
                              <MenuItem value="Project-Level">Project-Level</MenuItem>
                              <MenuItem value="App-Level">App-Level</MenuItem>
                              <MenuItem value="AI-Generated">AI-Generated</MenuItem>
                            </Select>
                          </FormControl>

                          <Tooltip title="Delete Section">
                            <IconButton
                              onClick={e => {
                                e.stopPropagation();
                                removeSection(sectionIndex);
                              }}
                              color="error"
                              size="small"
                            >
                              <DeleteIcon />
                            </IconButton>
                          </Tooltip>
                        </Stack>
                      </AccordionSummary>
                      <AccordionDetails sx={{ p: 0 }}>
                        <Divider />
                        {section.type === 'AI-Generated' ? (
                          <Box sx={{ p: 4, textAlign: 'center', bgcolor: 'background.default' }}>
                            <Typography variant="body2" color="text.secondary" fontStyle="italic">
                              Items in this section will be automatically generated by AI based on project scope.
                            </Typography>
                          </Box>
                        ) : (
                          <Box>
                            {section.items.map((item, itemIndex) => (
                              <Box
                                key={item.uid}
                                draggable
                                onDragStart={handleItemDragStart(section.uid, item.uid)}
                                onDragOver={handleItemDragOver(section.uid, item.uid)}
                                onDragLeave={handleItemDragLeave(section.uid, item.uid)}
                                onDrop={handleItemDrop(section.uid, item.uid)}
                                onDragEnd={handleItemDragEnd}
                                sx={{
                                  display: 'flex',
                                  alignItems: 'start',
                                  gap: 2,
                                  p: 2,
                                  borderBottom: '1px solid',
                                  borderColor: 'divider',
                                  bgcolor: 'background.paper',
                                  position: 'relative',
                                  borderTop: dragItemOverRef?.sectionUid === section.uid && dragItemOverRef?.itemUid === item.uid ? '2px solid #3b82f6' : undefined,
                                }}
                              >
                                <Box
                                  className="item-drag-handle"
                                  sx={{ mt: 2, cursor: 'grab', color: 'text.disabled', '&:hover': { color: 'text.primary' } }}
                                >
                                  <DragIndicatorIcon fontSize="small" />
                                </Box>

                                <Stack spacing={2} flexGrow={1}>
                                  <TextField
                                    label="Item Name"
                                    value={item.itemName}
                                    onChange={e => updateItem(sectionIndex, itemIndex, i => ({ ...i, itemName: e.target.value }))}
                                    fullWidth
                                    size="small"
                                  />
                                  <TextField
                                    label="Item Detail / Instruction"
                                    value={item.itemDetail}
                                    onChange={e => updateItem(sectionIndex, itemIndex, i => ({ ...i, itemDetail: e.target.value }))}
                                    fullWidth
                                    multiline
                                    rows={2}
                                    size="small"
                                    placeholder="Describe what needs to be done..."
                                  />
                                </Stack>

                                <IconButton
                                  onClick={() => removeItem(sectionIndex, itemIndex)}
                                  size="small"
                                  sx={{ mt: 1, color: 'text.disabled', '&:hover': { color: 'error.main' } }}
                                >
                                  <CloseIcon fontSize="small" />
                                </IconButton>
                              </Box>
                            ))}
                            {/* Drop target for end of list */}
                            <Box
                              onDragOver={handleItemDragOver(section.uid, 'end')}
                              onDragLeave={handleItemDragLeave(section.uid, 'end')}
                              onDrop={handleItemDrop(section.uid, 'end')}
                              sx={{
                                height: 10,
                                bgcolor: dragItemOverRef?.sectionUid === section.uid && dragItemOverRef?.itemUid === 'end' ? 'primary.light' : 'transparent',
                                transition: 'background-color 0.2s'
                              }}
                            />
                            <Box sx={{ p: 2, bgcolor: 'action.hover' }}>
                              <Button
                                startIcon={<AddIcon />}
                                onClick={() => addItem(sectionIndex)}
                                size="small"
                              >
                                Add Item
                              </Button>
                            </Box>
                          </Box>
                        )}
                      </AccordionDetails>
                    </Accordion>
                  ))
                )}
              </Stack>
              <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
                <Button variant="contained" onClick={handleNext}>
                  Next Step
                </Button>
              </Box>
            </>
          )}

          {activeStep === 1 && (
            <>
              <Paper sx={{ p: { xs: 3, md: 4 } }}>
                <TemplateColumnMapper
                  sections={template.sections as any}
                  estimationColumns={template.estimationColumns}
                  onChange={(sections) => updateTemplate(prev => ({ ...prev, sections: sections as any }))}
                />
              </Paper>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', pt: 2 }}>
                <Button variant="outlined" onClick={handleBack}>
                  Back
                </Button>
                <Button variant="contained" onClick={handleNext}>
                  Next Step
                </Button>
              </Box>
            </>
          )}

          {activeStep === 2 && (
            <>
              <TemplateTimelineEditor
                phases={template.timelinePhases}
                sections={template.sections as any}
                onChange={phases => updateTemplate(prev => ({ ...prev, timelinePhases: phases }))}
              />
              <Box sx={{ display: 'flex', justifyContent: 'space-between', pt: 2 }}>
                <Button variant="outlined" onClick={handleBack}>
                  Back
                </Button>
                <Button variant="contained" onClick={handleNext}>
                  Next Step
                </Button>
              </Box>
            </>
          )}

          {activeStep === 3 && (
            <Paper sx={{ p: 4 }}>
              <Typography variant="h4" gutterBottom>Role Mapping</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 4 }}>
                Map the &quot;Efforts&quot; identified in the timeline to specific Roles (and Rates) defined in the configuration.
              </Typography>

              <Stack spacing={4}>
                {template.sections && template.sections.length > 0 ? (
                  template.sections.map((section, sIndex) => {
                    // Collect mappable keys for this section
                    const keys = new Set<string>();

                    // Type A: AI-Generated Section (Uses Section Name + Effort)
                    if (section.type === 'AI-Generated' && section.effort && section.effort.length > 0) {
                      section.effort.forEach(e => keys.add(`${section.sectionName} - ${e}`));
                    }
                    // Type B: Standard Section (Item Name + Effort)
                    else if (section.items && section.items.length > 0) {
                      section.items.forEach(item => {
                        (item.effort || []).forEach(e => keys.add(`${item.itemName} - ${e}`));
                      });
                    }
                    // Type C: Fallback Section level effort (if no items)
                    else if (section.effort && section.effort.length > 0) {
                      section.effort.forEach(e => keys.add(`${section.sectionName} - ${e}`));
                    }

                    if (keys.size === 0) return null;

                    return (
                      <Box key={sIndex} sx={{ border: 1, borderColor: 'divider', borderRadius: 1, p: 2 }}>
                        <Typography variant="h6" sx={{ mb: 2, fontWeight: 600 }}>
                          {section.sectionName}
                        </Typography>
                        <Stack spacing={2}>
                          {Array.from(keys).map((key) => (
                            <Stack key={key} direction="row" alignItems="center" spacing={2} sx={{ pb: 2, borderBottom: '1px solid', borderColor: 'divider', '&:last-child': { borderBottom: 0 } }}>
                              <Typography sx={{ width: 350, fontWeight: 500, fontSize: '0.9rem' }}>{key}</Typography>
                              <FormControl fullWidth size="small">
                                <InputLabel>Mapped Role(s)</InputLabel>
                                <Select
                                  label="Mapped Role(s)"
                                  multiple
                                  value={(template.effortRoleMapping?.[key] || '').split(',').filter(Boolean)}
                                  onChange={(e: SelectChangeEvent<string[]>) => {
                                    const val = e.target.value;
                                    const newValue = typeof val === 'string' ? val : val.join(',');

                                    updateTemplate(prev => ({
                                      ...prev,
                                      effortRoleMapping: {
                                        ...(prev.effortRoleMapping || {}),
                                        [key]: newValue
                                      }
                                    }));
                                  }}
                                  renderValue={(selected) => (
                                    <Stack direction="row" gap={0.5} flexWrap="wrap">
                                      {selected.map((value) => (
                                        <Chip key={value} label={value} size="small" />
                                      ))}
                                    </Stack>
                                  )}
                                >
                                  {availableRoles.map(r => (
                                    <MenuItem key={r} value={r}>{r}</MenuItem>
                                  ))}
                                </Select>
                              </FormControl>
                            </Stack>
                          ))}
                        </Stack>
                      </Box>
                    );
                  })
                ) : (
                  <Typography color="text.secondary">No sections defined.</Typography>
                )}
              </Stack>

              <Box sx={{ display: 'flex', justifyContent: 'space-between', pt: 4 }}>
                <Button variant="outlined" onClick={handleBack}>
                  Back
                </Button>
              </Box>
            </Paper>
          )}
        </Stack>
      )
      }
    </Box >
  );
}
