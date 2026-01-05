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
    Tooltip,
    IconButton
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import DownloadIcon from '@mui/icons-material/Download';
import AutoFixHighIcon from '@mui/icons-material/AutoFixHigh';
import { apiFetch } from '../../../lib/api';
import Swal from 'sweetalert2';

interface PrototypeAssessmentSummary {
    assessmentId: number;
    projectName: string;
    templateName: string;
    status: string;
    lastModifiedAt?: string;
    hasPrototype: boolean;
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

export default function PrototypeListPage() {
    const router = useRouter();
    const [rows, setRows] = useState<PrototypeAssessmentSummary[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [generatingId, setGeneratingId] = useState<number | null>(null);
    const [downloadingId, setDownloadingId] = useState<number | null>(null);
    const [selectionModalOpen, setSelectionModalOpen] = useState(false);
    const [selectedAssessmentId, setSelectedAssessmentId] = useState<number | null>(null);
    const [availableItems, setAvailableItems] = useState<{ id: string, name: string }[]>([]);
    const [selectedItems, setSelectedItems] = useState<string[]>([]);
    const [itemFeedback, setItemFeedback] = useState<Record<string, string>>({});
    const [loadingItems, setLoadingItems] = useState(false);

    // Fetch items for selection
    const openSelectionModal = async (assessmentId: number) => {
        setSelectedAssessmentId(assessmentId);
        setSelectionModalOpen(true);
        setLoadingItems(true);
        setAvailableItems([]);
        setSelectedItems([]);
        setItemFeedback({});

        try {
            // We need an endpoint to get items? Or we leverage existing assessment detail endpoint.
            // Let's assume we can get assessment detail via existing API or add a new one. 
            // Reuse existing get assessment endpoint (not shown here but likely exists or we add one quickly)
            // Using existing store logic, we might not have a direct endpoint exposing items easily without full auth.
            // Actually, we can just use the generate endpoint with a "dry run" or "list items" flag?
            // Or better, let's just fetch the assessment detail if available.
            // Since we don't have a handy "Get Items" endpoint in PrototypeController, let's quickly check AssessmentController or just add one.
            // For now, let's assume we can't easily get them without backend change. 
            // Wait, the user asked for this feature. We should implement it properly.
            // Let's first add a helper to fetch items. 
            // Ideally we'd call /api/assessments/{id} but that might return full JSON. 
            // Let's Try to fetch assessment details.
            const res = await apiFetch(`/api/assessment/${assessmentId}`);
            if (res.ok) {
                const data = await res.json();
                console.log('--- DEBUG: RAW API RESPONSE ---', data);

                let items: { id: string, name: string }[] = [];
                const sections = data.sections || data.Sections || data.Assessment?.Sections || [];

                console.log('--- DEBUG: Extracted Sections ---', sections);

                if (sections && Array.isArray(sections)) {
                    // Helper to check tags
                    const isValidForGen = (i: any) => {
                        const detail = (i.itemDetail || i.ItemDetail || '').toLowerCase();
                        const cat = (i.category || i.Category || '').toLowerCase();
                        return detail.includes('[web]') || cat.includes('web') ||
                            detail.includes('[mobile]') || cat.includes('mobile');
                    };

                    // FIRST PASS: Strict search for Item Development
                    sections.forEach((s: any) => {
                        const sName = s.sectionName || s.SectionName;
                        if (sName && sName.toLowerCase().includes('item development')) {
                            const sItems = s.items || s.Items;
                            if (sItems && Array.isArray(sItems)) {
                                sItems.forEach((i: any) => {
                                    const id = i.itemId || i.ItemId;
                                    const name = i.itemName || i.ItemName;
                                    // STRICT FILTER: Match Backend Logic
                                    if (id && name && isValidForGen(i)) {
                                        items.push({ id, name });
                                    }
                                });
                            }
                        }
                    });

                    // SECOND PASS: If empty, grab ANY items from ANY section that match the tags
                    if (items.length === 0) {
                        console.warn('--- DEBUG: First pass empty (no tagged items in Item Dev), using fallback to search all sections ---');
                        sections.forEach((s: any) => {
                            const sItems = s.items || s.Items;
                            if (sItems && Array.isArray(sItems)) {
                                sItems.forEach((i: any) => {
                                    const id = i.itemId || i.ItemId;
                                    const name = i.itemName || i.ItemName;
                                    // STRICT FILTER: Match Backend Logic
                                    if (id && name && isValidForGen(i)) {
                                        items.push({ id, name });
                                    }
                                });
                            }
                        });
                    }
                } else {
                    console.error('--- DEBUG: Sections is NOT an array or is missing! ---', sections);
                }

                // Deduplicate
                const uniqueItems = Array.from(new Map(items.map(item => [item.id, item])).values());
                console.log('--- DEBUG: FINAL FOUND ITEMS ---', uniqueItems);

                setAvailableItems(uniqueItems);
                setSelectedItems(uniqueItems.map(i => i.id));
            } else {
                console.error('--- DEBUG: API Error ---', res.status, res.statusText);
            }
        } catch (e) {
            console.error(e);
            Swal.fire("Error", "Failed to load items for selection", "error");
            setSelectionModalOpen(false);
        } finally {
            setLoadingItems(false);
        }
    };

    const handleConfirmRegenerate = () => {
        if (selectedAssessmentId) {
            // Filter feedback to only include selected items
            const RelevantFeedback: Record<string, string> = {};
            selectedItems.forEach(id => {
                if (itemFeedback[id]) RelevantFeedback[id] = itemFeedback[id];
            });
            handleGenerate(selectedAssessmentId, selectedItems.length > 0 ? selectedItems : undefined, RelevantFeedback);
            setSelectionModalOpen(false);
        }
    };

    const loadData = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await apiFetch('/api/prototypes');
            if (!res.ok) {
                throw new Error(`Failed to load prototypes (${res.status})`);
            }
            const data = await res.json();
            setRows(Array.isArray(data) ? data : []);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to load prototypes');
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleGenerate = useCallback(
        async (assessmentId: number, itemIds?: string[], itemFeedback?: Record<string, string>) => {
            setGeneratingId(assessmentId);
            try {
                const body: any = { assessmentId };
                if (itemIds && itemIds.length > 0) {
                    body.itemIds = itemIds;
                }
                if (itemFeedback && Object.keys(itemFeedback).length > 0) {
                    body.itemFeedback = itemFeedback;
                }

                const res = await apiFetch('/api/prototypes/generate', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body),
                });
                if (!res.ok) {
                    const text = await res.text();
                    throw new Error(text || 'Failed to generate prototype');
                }
                const data = await res.json();

                // Refresh data to update status
                await loadData();

                // Open demo only if full regen? Or always?
                window.open(data.url, '_blank');
            } catch (err) {
                const message = err instanceof Error ? err.message : 'Failed to generate prototype';
                Swal.fire('Error', message, 'error');
            } finally {
                setGeneratingId(null);
            }
        },
        [loadData]
    );

    const handleView = useCallback(async (assessmentId: number) => {
        // Assuming standard URL pattern since we don't have it in summary (server doesn't return URL in list)
        // Or we can just call generate again which is idempotent-ish regarding URL return 
        // but re-generating is expensive. 
        // Better: construct URL client side based on ID or ask backend for URL.
        // Based on service: `/demos/${assessmentId}/index.html`
        window.open(`/demos/${assessmentId}/index.html`, '_blank');
    }, []);

    const handleDownload = useCallback(
        async (assessmentId: number) => {
            setDownloadingId(assessmentId);
            try {
                const res = await apiFetch(`/api/prototypes/${assessmentId}/download`);
                if (!res.ok) {
                    throw new Error('Failed to download');
                }
                const blob = await res.blob();
                const url = window.URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                // Filename is handled by Content-Disposition header usually, but we can guess or use default
                link.download = `prototype-${assessmentId}.zip`;
                document.body.appendChild(link);
                link.click();
                link.remove();
                window.URL.revokeObjectURL(url);
            } catch (err) {
                Swal.fire('Error', 'Failed to download prototype zip', 'error');
            } finally {
                setDownloadingId(null);
            }
        },
        []
    );

    const content = useMemo(() => {
        if (loading) {
            return (
                <Stack direction="row" alignItems="center" justifyContent="center" spacing={2} sx={{ py: 6 }}>
                    <CircularProgress size={32} />
                    <Typography variant="body1">Loading assessments…</Typography>
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
                    <Typography variant="h6">No assessments found.</Typography>
                    <Typography variant="body2" sx={{ mt: 1 }}>
                        Complete an assessment in the workspace to enable prototype generation.
                    </Typography>
                </Box>
            );
        }

        return (
            <TableContainer>
                <Table size="small">
                    <TableHead>
                        <TableRow>
                            <TableCell>Project</TableCell>
                            <TableCell>Template</TableCell>
                            <TableCell>Status</TableCell>
                            <TableCell>Last Modified</TableCell>
                            <TableCell>Prototype Status</TableCell>
                            <TableCell align="right">Actions</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {rows.map(row => {
                            const isGenerating = generatingId === row.assessmentId;
                            const isDownloading = downloadingId === row.assessmentId;

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
                                        {row.hasPrototype ? (
                                            <Chip color="success" size="small" label="Generated" icon={<AutoFixHighIcon />} />
                                        ) : (
                                            <Chip color="default" size="small" label="Not Generated" />
                                        )}
                                    </TableCell>
                                    <TableCell align="right">
                                        <Stack direction="row" spacing={1} justifyContent="flex-end">
                                            {!row.hasPrototype && (
                                                <Button
                                                    variant="contained"
                                                    color="primary"
                                                    size="small"
                                                    startIcon={isGenerating ? <CircularProgress size={20} color="inherit" /> : <AutoFixHighIcon />}
                                                    disabled={isGenerating}
                                                    onClick={() => handleGenerate(row.assessmentId)}
                                                >
                                                    Generate UI
                                                </Button>
                                            )}
                                            {row.hasPrototype && (
                                                <>
                                                    <Button
                                                        variant="outlined"
                                                        color="secondary"
                                                        size="small"
                                                        startIcon={<PlayArrowIcon />}
                                                        onClick={() => handleView(row.assessmentId)}
                                                    >
                                                        View Demo
                                                    </Button>

                                                    <Tooltip title="Regenerate All">
                                                        <IconButton
                                                            color="secondary"
                                                            disabled={isGenerating}
                                                            onClick={() => openSelectionModal(row.assessmentId)}
                                                            size="small"
                                                        >
                                                            {isGenerating ? <CircularProgress size={20} /> : <AutoFixHighIcon />}
                                                        </IconButton>
                                                    </Tooltip>

                                                    <Tooltip title="Download Zip">
                                                        <IconButton
                                                            color="primary"
                                                            disabled={isDownloading}
                                                            onClick={() => handleDownload(row.assessmentId)}>
                                                            {isDownloading ? <CircularProgress size={20} /> : <DownloadIcon />}
                                                        </IconButton>
                                                    </Tooltip>
                                                </>
                                            )}
                                        </Stack>
                                    </TableCell>
                                </TableRow>
                            );
                        })}
                    </TableBody>
                </Table>
            </TableContainer>
        );
    }, [loading, error, rows, generatingId, downloadingId, handleGenerate, handleView, handleDownload, loadData, openSelectionModal]);

    return (
        <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 3 }}>
            <Box>
                <Typography variant="h1" gutterBottom>
                    Pre-Sales Prototypes
                </Typography>
                <Typography variant="body1" color="text.secondary">
                    Generate fast front-end prototypes from saved and confirmed assessments.
                </Typography>
            </Box>
            <Paper variant="outlined" sx={{ p: 3, bgcolor: 'background.paper', borderRadius: 3 }}>
                {content}
            </Paper>

            {/* Selection Modal */}
            {selectionModalOpen && (
                <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, background: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 9999 }}>
                    <Paper sx={{ p: 4, maxWidth: 500, width: '100%', maxHeight: '80vh', overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
                        <Typography variant="h6" gutterBottom>Select Items to Regenerate</Typography>
                        {loadingItems ? <CircularProgress /> : (
                            <Box sx={{ flex: 1, overflowY: 'auto', my: 2 }}>
                                <Stack spacing={1}>
                                    {availableItems.length === 0 && <Typography>No items found available for generation.</Typography>}
                                    {availableItems.map(item => (
                                        <Box key={item.id} sx={{ display: 'flex', flexDirection: 'column', gap: 1, p: 1, borderBottom: '1px solid #eee' }}>
                                            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                                                <input
                                                    type="checkbox"
                                                    checked={selectedItems.includes(item.id)}
                                                    onChange={(e) => {
                                                        if (e.target.checked) setSelectedItems(prev => [...prev, item.id]);
                                                        else setSelectedItems(prev => prev.filter(id => id !== item.id));
                                                    }}
                                                />
                                                <Typography sx={{ fontWeight: 500 }}>{item.name}</Typography>
                                            </Box>
                                            {selectedItems.includes(item.id) && (
                                                <textarea
                                                    placeholder="Enter feedback or instructions for this item..."
                                                    style={{ width: '100%', padding: '8px', fontSize: '14px', borderRadius: '4px', border: '1px solid #ccc', minHeight: '60px' }}
                                                    value={itemFeedback[item.id] || ''}
                                                    onChange={(e) => setItemFeedback(prev => ({ ...prev, [item.id]: e.target.value }))}
                                                />
                                            )}
                                        </Box>
                                    ))}
                                </Stack>
                            </Box>
                        )}
                        <Stack direction="row" spacing={2} justifyContent="flex-end" sx={{ mt: 2 }}>
                            <Button onClick={() => setSelectionModalOpen(false)}>Cancel</Button>
                            <Button
                                variant="contained"
                                onClick={handleConfirmRegenerate}
                                disabled={loadingItems || availableItems.length === 0}
                            >
                                {selectedItems.length > 0 ? `Regenerate (${selectedItems.length})` : "Regenerate All"}
                            </Button>
                        </Stack>
                    </Paper>
                </div>
            )}
        </Box>
    );
}
