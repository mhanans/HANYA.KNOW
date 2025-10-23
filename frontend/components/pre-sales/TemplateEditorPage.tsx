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
  Divider,
  FormControl,
  IconButton,
  InputLabel,
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
}

interface TemplateSection {
  sectionName: string;
  type: string;
  items: TemplateItem[];
  uid?: string;
}

interface ProjectTemplate {
  id?: number;
  templateName: string;
  estimationColumns: string[];
  sections: TemplateSection[];
}

const createEmptyTemplate = (): ProjectTemplate => ({
  templateName: '',
  estimationColumns: [],
  sections: [],
});

const generateUid = () => Math.random().toString(36).slice(2, 10);

const withGeneratedIds = (template: ProjectTemplate): ProjectTemplate => ({
  ...template,
  sections: (template.sections ?? []).map(section => ({
    ...section,
    uid: section.uid ?? generateUid(),
    items: (section.items ?? []).map(item => ({
      ...item,
      category: normalizeCategory(item.category),
      uid: item.uid ?? generateUid(),
    })),
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
  const [dragSectionUid, setDragSectionUid] = useState<string | null>(null);
  const [dragSectionOverUid, setDragSectionOverUid] = useState<string | null>(null);
  const [dragItemRef, setDragItemRef] = useState<{ sectionUid: string; itemUid: string } | null>(null);
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

  const generateItemId = () => {
    if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
      return crypto.randomUUID();
    }
    return `item-${generateUid()}`;
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
      })),
    })),
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
        router.replace(`/pre-sales/project-templates/${created.id}`);
      } else {
        setDirty(false);
        await Swal.fire({
          icon: 'success',
          title: 'Template saved',
          text: 'Template saved successfully.',
        });
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
        </Stack>
      </Stack>

      {loading ? (
        <Paper sx={{ p: 4 }}>
          <Typography variant="body1">Loading template…</Typography>
        </Paper>
      ) : (
        <Stack spacing={4}>
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
                    color="secondary"
                    startIcon={<AddIcon />}
                    onClick={addColumn}
                    sx={{ whiteSpace: 'nowrap' }}
                  >
                    Add Column
                  </Button>
                </Stack>
              </Box>
            </Stack>
          </Paper>

          <Paper sx={{ p: { xs: 3, md: 4 } }}>
            <Stack spacing={3}>
              <Stack
                direction={{ xs: 'column', md: 'row' }}
                spacing={2}
                justifyContent="space-between"
                alignItems={{ xs: 'flex-start', md: 'center' }}
              >
                <Box>
                  <Typography variant="h2">Sections &amp; Items</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Group the template structure into cards for easier navigation.
                  </Typography>
                </Box>
                <Button
                  variant="contained"
                  color="secondary"
                  startIcon={<AddIcon />}
                  onClick={addSection}
                >
                  Add Section
                </Button>
              </Stack>
              <Divider sx={{ borderColor: 'rgba(148, 163, 184, 0.2)' }} />
              {template.sections.length === 0 ? (
                <Box sx={{ textAlign: 'center', py: 6 }}>
                  <Typography variant="body1" color="text.secondary" gutterBottom>
                    No sections yet. Start by adding your first section.
                  </Typography>
                  <Button variant="outlined" startIcon={<AddIcon />} onClick={addSection}>
                    Add Section
                  </Button>
                </Box>
              ) : (
                <Stack spacing={2}>
                  {template.sections.map((section, sectionIndex) => {
                    const sectionUid = section.uid ?? `${sectionIndex}`;
                    const isAiGenerated = section.type === 'AI-Generated';
                    const isExpanded = expandedSections.has(sectionUid);
                    const isDragOver = dragSectionOverUid === sectionUid;
                    return (
                      <Box
                        key={sectionUid}
                        draggable
                        onDragStart={handleSectionDragStart(sectionUid)}
                        onDragOver={handleSectionDragOver(sectionUid)}
                        onDrop={handleSectionDrop(sectionUid)}
                        onDragLeave={handleSectionDragLeave(sectionUid)}
                        onDragEnd={handleSectionDragEnd}
                        sx={{
                          borderRadius: 3,
                          border: '1px solid',
                          borderColor: isDragOver ? 'primary.main' : 'rgba(148, 163, 184, 0.2)',
                          boxShadow: isDragOver ? '0 0 0 2px rgba(59,130,246,0.35)' : 'none',
                          transition: 'all 0.2s ease',
                          overflow: 'hidden',
                          backgroundColor: 'background.paper',
                        }}
                      >
                        <Accordion
                          expanded={isExpanded}
                          onChange={() => toggleSection(sectionUid)}
                          disableGutters
                          square={false}
                          sx={{
                            backgroundColor: 'transparent',
                            '&:before': { display: 'none' },
                          }}
                        >
                          <AccordionSummary
                            expandIcon={<ExpandMoreIcon htmlColor="#94a3b8" />}
                            sx={{ px: 3, py: 2 }}
                          >
                            <Stack
                              direction={{ xs: 'column', lg: 'row' }}
                              spacing={2}
                              alignItems={{ xs: 'flex-start', lg: 'center' }}
                              sx={{ width: '100%' }}
                            >
                              <Stack
                                direction="row"
                                spacing={1}
                                alignItems="center"
                                sx={{ flexGrow: 1, width: '100%' }}
                              >
                                <IconButton
                                  className="section-drag-handle"
                                  onClick={stopPropagation}
                                  size="small"
                                  sx={{ color: 'text.secondary', cursor: 'grab' }}
                                >
                                  <DragIndicatorIcon />
                                </IconButton>
                                <TextField
                                  label="Section Name"
                                  value={section.sectionName}
                                  onChange={e =>
                                    updateSection(sectionIndex, current => ({
                                      ...current,
                                      sectionName: e.target.value,
                                    }))
                                  }
                                  onClick={stopPropagation}
                                  fullWidth
                                />
                              </Stack>
                              <Stack direction="row" spacing={1} alignItems="center" sx={{ minWidth: { lg: 300 } }}>
                                <FormControl fullWidth onClick={stopPropagation}>
                                  <InputLabel id={`section-type-${sectionUid}`}>Type</InputLabel>
                                  <Select
                                    labelId={`section-type-${sectionUid}`}
                                    label="Type"
                                    value={section.type}
                                    onChange={e =>
                                      updateSection(sectionIndex, current => ({
                                        ...current,
                                        type: e.target.value,
                                      }))
                                    }
                                  >
                                    <MenuItem value="Project-Level">Project-Level</MenuItem>
                                    <MenuItem value="App-Level">App-Level</MenuItem>
                                    <MenuItem value="AI-Generated">AI-Generated</MenuItem>
                                  </Select>
                                </FormControl>
                                <Tooltip title="Delete section">
                                  <IconButton
                                    color="error"
                                    onClick={event => {
                                      event.stopPropagation();
                                      removeSection(sectionIndex);
                                    }}
                                  >
                                    <DeleteIcon />
                                  </IconButton>
                                </Tooltip>
                              </Stack>
                            </Stack>
                          </AccordionSummary>
                          <AccordionDetails sx={{ px: 0, pb: 3 }}>
                            <Box sx={{ px: 3, pt: 1 }}>
                              {isAiGenerated ? (
                                <Alert severity="info">
                                  Items in this section are generated automatically when running the AI-assisted assessment.
                                  Manual editing is disabled.
                                </Alert>
                              ) : (
                                <>
                                  <TableContainer component={Paper} variant="outlined">
                                    <Table size="small">
                                      <TableHead>
                                        <TableRow>
                                          <TableCell width={56}></TableCell>
                                          <TableCell width="30%">Item Name</TableCell>
                                          <TableCell>Item Detail</TableCell>
                                          <TableCell width="20%">Category</TableCell>
                                          <TableCell align="right" width={120}>
                                            Actions
                                          </TableCell>
                                        </TableRow>
                                      </TableHead>
                                      <TableBody>
                                        {section.items.length === 0 ? (
                                          <TableRow>
                                            <TableCell colSpan={5}>
                                              <Typography variant="body2" color="text.secondary">
                                                No items yet. Add items to this section to build your estimation grid.
                                              </Typography>
                                            </TableCell>
                                          </TableRow>
                                        ) : (
                                          section.items.map((item, itemIndex) => {
                                            const itemUid = item.uid ?? `${sectionUid}-${itemIndex}`;
                                            return (
                                              <TableRow
                                                key={itemUid}
                                                draggable
                                                onDragStart={handleItemDragStart(sectionUid, itemUid)}
                                                onDragOver={handleItemDragOver(sectionUid, itemUid)}
                                                onDrop={handleItemDrop(sectionUid, itemUid)}
                                                onDragLeave={handleItemDragLeave(sectionUid, itemUid)}
                                                onDragEnd={handleItemDragEnd}
                                              >
                                                <TableCell width={56}>
                                                  <IconButton
                                                    className="item-drag-handle"
                                                    size="small"
                                                    sx={{ color: 'text.secondary', cursor: 'grab' }}
                                                  >
                                                    <DragIndicatorIcon fontSize="small" />
                                                  </IconButton>
                                                </TableCell>
                                                <TableCell width="30%">
                                                  <TextField
                                                    variant="outlined"
                                                    size="small"
                                                    value={item.itemName}
                                                    onChange={e =>
                                                      updateItem(sectionIndex, itemIndex, current => ({
                                                        ...current,
                                                        itemName: e.target.value,
                                                      }))
                                                    }
                                                    placeholder="User Authentication"
                                                    fullWidth
                                                  />
                                                </TableCell>
                                                <TableCell>
                                                  <TextField
                                                    variant="outlined"
                                                    size="small"
                                                    value={item.itemDetail}
                                                    onChange={e =>
                                                      updateItem(sectionIndex, itemIndex, current => ({
                                                        ...current,
                                                        itemDetail: e.target.value,
                                                      }))
                                                    }
                                                    placeholder="Implement secure login, JWT, and password reset"
                                                    fullWidth
                                                  />
                                                </TableCell>
                                                <TableCell width="20%">
                                                  <TextField
                                                    select
                                                    variant="outlined"
                                                    size="small"
                                                    value={item.category}
                                                    onChange={e =>
                                                      updateItem(sectionIndex, itemIndex, current => ({
                                                        ...current,
                                                        category: normalizeCategory(e.target.value),
                                                      }))
                                                    }
                                                    fullWidth
                                                  >
                                                    {CATEGORY_OPTIONS.map(option => (
                                                      <MenuItem key={option} value={option}>
                                                        {option}
                                                      </MenuItem>
                                                    ))}
                                                  </TextField>
                                                </TableCell>
                                                <TableCell align="right" width={120}>
                                                  <Stack direction="row" spacing={0.5} justifyContent="flex-end">
                                                    <Tooltip title="Move up">
                                                      <span>
                                                        <IconButton
                                                          size="small"
                                                          onClick={() => moveItem(sectionIndex, itemIndex, -1)}
                                                          disabled={itemIndex === 0}
                                                        >
                                                          <ArrowUpwardIcon fontSize="inherit" />
                                                        </IconButton>
                                                      </span>
                                                    </Tooltip>
                                                    <Tooltip title="Move down">
                                                      <span>
                                                        <IconButton
                                                          size="small"
                                                          onClick={() => moveItem(sectionIndex, itemIndex, 1)}
                                                          disabled={itemIndex === section.items.length - 1}
                                                        >
                                                          <ArrowDownwardIcon fontSize="inherit" />
                                                        </IconButton>
                                                      </span>
                                                    </Tooltip>
                                                    <Tooltip title="Delete item">
                                                      <IconButton
                                                        size="small"
                                                        color="error"
                                                        onClick={() => removeItem(sectionIndex, itemIndex)}
                                                      >
                                                        <DeleteIcon fontSize="small" />
                                                      </IconButton>
                                                    </Tooltip>
                                                  </Stack>
                                                </TableCell>
                                              </TableRow>
                                            );
                                          })
                                        )}
                                      </TableBody>
                                    </Table>
                                  </TableContainer>
                                  <Button
                                    variant="outlined"
                                    startIcon={<AddIcon />}
                                    onClick={() => addItem(sectionIndex)}
                                    sx={{ mt: 2 }}
                                  >
                                    Add Item
                                  </Button>
                                </>
                              )}
                            </Box>
                          </AccordionDetails>
                        </Accordion>
                      </Box>
                    );
                  })}
                </Stack>
              )}
            </Stack>
          </Paper>

          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      )}
    </Box>
  );
}

