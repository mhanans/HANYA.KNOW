import { useState, useRef, useEffect, MouseEvent } from 'react';
import {
    Box,
    Button,
    Paper,
    Stack,
    TextField,
    Typography,
    Tooltip,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';

export interface TimelineItem {
    id: string;
    name: string;
    duration: number;
    startDayOffset: number;
}

export interface TimelinePhase {
    id: string;
    name: string;
    duration: number; // days
    startDay: number; // 1-indexed
    items: TimelineItem[];
}

interface TemplateItemSource {
    itemId: string;
    itemName: string;
    category: string;
    effort?: string[];
    roles?: string[]; // legacy
}

interface TemplateSectionSource {
    sectionName: string;
    type: string;
    items: TemplateItemSource[];
    effort?: string[];
    roles?: string[]; // legacy
}

interface TemplateTimelineEditorProps {
    phases: TimelinePhase[];
    sections?: TemplateSectionSource[];
    onChange: (phases: TimelinePhase[]) => void;
}

const CELL_WIDTH = 40;
const HEADER_ROW_HEIGHT = 32;
const HEADER_HEIGHT = HEADER_ROW_HEIGHT * 3; // Month, Week, Day
const ROW_HEIGHT = 42;
const LABEL_WIDTH = 240;

export default function TemplateTimelineEditor({ phases, sections = [], onChange }: TemplateTimelineEditorProps) {
    const [totalDays, setTotalDays] = useState(75); // Default 3 months (25 days * 3)
    const initRef = useRef(false);

    // Auto-populate phases from sections on mount or when sections change
    useEffect(() => {
        if (!sections || sections.length === 0) return;

        // Clone existing phases to avoid direct mutation
        let newPhases = [...phases];
        let hasChanges = false;

        sections.forEach((section, sIndex) => {
            // Find or create phase for section
            // We match by Name for now, or could use ID if available. Sections don't have stable IDs in this Type definition?
            // "sectionName" is usually unique enough for this template context.
            let phaseIndex = newPhases.findIndex(p => p.name === section.sectionName);
            let phase: TimelinePhase;

            if (phaseIndex === -1) {
                phase = {
                    id: crypto.randomUUID(),
                    name: section.sectionName,
                    startDay: 1 + (sIndex * 5), // Stagger starts slightly
                    duration: 10,
                    items: []
                };
                newPhases.push(phase);
                hasChanges = true;
            } else {
                phase = { ...newPhases[phaseIndex] };
                // Ensure items array is cloned
                phase.items = [...phase.items];
            }

            // Determine required items for this section (Item - Effort)
            const requiredItems = new Set<string>();
            const isAiGenerated = section.type === 'AI-Generated';

            if (isAiGenerated && (section.effort || section.roles)) {
                const efforts = section.effort || section.roles || [];
                efforts.forEach(e => requiredItems.add(`${section.sectionName} - ${e}`));
            } else if (!isAiGenerated && section.items) {
                section.items.forEach(item => {
                    const efforts = item.effort || item.roles || [];
                    if (efforts.length > 0) {
                        efforts.forEach(e => requiredItems.add(`${item.itemName} - ${e}`));
                    } else {
                        // User requirement: "no need of just the item (without effort) on timeline"
                        // So we SKIP items without effort? 
                        // "the effort so it's item (effort) like in unscheduled no need of just the item (without effort)"
                        // If we strictly follow this, we exclude pure items. 
                        // But just in case, let's include if purely generic? 
                        // prompt: "effort its not there (still using item, and section on timeline)... i want is the list like on the timeline (with effort)"
                        // prompt: "no need of just the item (without effort) on timeline template"
                        // => STRICTLY Item - Effort.
                    }
                });
            } else if (section.effort) {
                section.effort.forEach(e => requiredItems.add(`${section.sectionName} - ${e}`));
            }

            // Sync Items: Remove unneeded, Add missing
            const currentItems = phase.items.filter(i => {
                if (requiredItems.has(i.name)) return true;
                hasChanges = true; // Item removed
                return false;
            });

            const existingNames = new Set(currentItems.map(i => i.name));
            requiredItems.forEach(reqName => {
                if (!existingNames.has(reqName)) {
                    currentItems.push({
                        id: crypto.randomUUID(),
                        name: reqName,
                        duration: 3,
                        startDayOffset: 0
                    });
                    hasChanges = true; // Item added
                }
            });

            phase.items = currentItems;

            // Update needed?
            // Recalculate duration?
            const maxEnd = Math.max(phase.duration, ...phase.items.map(i => i.startDayOffset + i.duration));
            if (maxEnd > phase.duration) {
                phase.duration = maxEnd;
                hasChanges = true;
            }

            // If we modified a cloned phase, put it back
            if (phaseIndex !== -1) {
                newPhases[phaseIndex] = phase;
            }
        });

        if (hasChanges) {
            // Sort phases by section order?
            // Logic: Reorder newPhases to match sections order if possible?
            // Let's just append new ones.
            onChange(newPhases);
        }
    }, [sections, phases.length]); // Dependency on sections and phases count (to avoid loop if no changes)

    // Auto-expand total days if phases go beyond
    useEffect(() => {
        const maxDay = Math.max(...phases.map(p => p.startDay + p.duration), 70) + 5; // maintain at least ~75
        if (maxDay > totalDays) {
            setTotalDays(Math.ceil(maxDay / 25) * 25); // Snap to month
        }
    }, [phases, totalDays]);

    const addMonth = () => {
        setTotalDays(prev => prev + 25);
    };

    const updatePhase = (id: string, updates: Partial<TimelinePhase>) => {
        onChange(phases.map(p => (p.id === id ? { ...p, ...updates } : p)));
    };

    const updateItem = (phaseId: string, itemId: string, updates: Partial<TimelineItem>) => {
        onChange(phases.map(p => {
            if (p.id !== phaseId) return p;
            const newItems = p.items.map(i => i.id === itemId ? { ...i, ...updates } : i);

            // Recalculate phase duration
            let newDuration = p.duration;
            if (newItems.length > 0) {
                const maxEnd = Math.max(...newItems.map(i => i.startDayOffset + i.duration));
                newDuration = Math.max(newDuration, maxEnd);
            }

            return { ...p, items: newItems, duration: newDuration };
        }));
    };

    // ... Drag State for Move/Resize
    const [dragState, setDragState] = useState<{
        type: 'move' | 'resize';
        target: 'phase' | 'item';
        phaseId: string;
        itemId?: string;
        startX: number;
        initialStart: number;
        initialDuration: number;
    } | null>(null);

    const handleMouseDown = (e: MouseEvent, target: 'phase' | 'item', phaseId: string, itemId: string | undefined, type: 'move' | 'resize', initialVal: number, initialDur: number) => {
        if (e.button !== 0) return; // Only left click
        e.preventDefault();
        e.stopPropagation();
        setDragState({
            type,
            target,
            phaseId,
            itemId,
            startX: e.clientX,
            initialStart: initialVal,
            initialDuration: initialDur
        });
    };

    useEffect(() => {
        const handleMouseMove = (e: globalThis.MouseEvent) => {
            if (!dragState) return;

            const deltaPixels = e.clientX - dragState.startX;
            const deltaDays = Math.round(deltaPixels / CELL_WIDTH);

            if (dragState.target === 'phase') {
                const newStart = Math.max(1, dragState.initialStart + deltaDays);
                updatePhase(dragState.phaseId, { startDay: newStart });
            } else if (dragState.target === 'item' && dragState.itemId) {
                if (dragState.type === 'move') {
                    const newOffset = Math.max(0, dragState.initialStart + deltaDays);
                    updateItem(dragState.phaseId, dragState.itemId, { startDayOffset: newOffset });
                } else {
                    const newDuration = Math.max(1, dragState.initialDuration + deltaDays);
                    updateItem(dragState.phaseId, dragState.itemId, { duration: newDuration });
                }
            }
        };

        const handleMouseUp = () => {
            setDragState(null);
        };

        if (dragState) {
            window.addEventListener('mousemove', handleMouseMove);
            window.addEventListener('mouseup', handleMouseUp);
        }
        return () => {
            window.removeEventListener('mousemove', handleMouseMove);
            window.removeEventListener('mouseup', handleMouseUp);
        };
    }, [dragState, phases]);


    return (
        <Stack spacing={2}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Box>
                    <Typography variant="h2">Timeline Template</Typography>
                    <Typography variant="body2" color="text.secondary">
                        Adjust the relative timing and duration of items.
                    </Typography>
                </Box>
                <Button
                    variant="outlined"
                    startIcon={<AddIcon />}
                    onClick={addMonth}
                    sx={{ borderColor: 'divider', color: 'text.secondary', '&:hover': { bgcolor: 'action.hover', borderColor: 'text.primary' } }}
                >
                    Add Month
                </Button>
            </Stack>

            <Paper variant="outlined" sx={{ overflowX: 'auto', p: 2, bgcolor: 'background.paper', borderColor: 'divider' }}>
                <Box sx={{ display: 'flex' }}>
                    {/* Headers Column */}
                    <Box sx={{ width: LABEL_WIDTH, flexShrink: 0, pt: `${HEADER_HEIGHT}px`, borderRight: 1, borderColor: 'divider', bgcolor: 'background.paper', zIndex: 10 }}>
                        {phases.map((phase) => (
                            <Box key={phase.id} sx={{ borderBottom: 1, borderColor: 'divider' }}>
                                {/* Phase Header Row - Left */}
                                <Box sx={{ display: 'flex', alignItems: 'center', px: 1, height: ROW_HEIGHT, bgcolor: 'action.hover' }}>
                                    <TextField
                                        variant="standard"
                                        value={phase.name}
                                        onChange={(e) => updatePhase(phase.id, { name: e.target.value })}
                                        fullWidth
                                        InputProps={{ disableUnderline: true, style: { fontSize: '0.875rem', fontWeight: 600 } }}
                                        disabled
                                    />
                                </Box>
                                {/* Items List - Left */}
                                {phase.items && phase.items.map(item => (
                                    <Box key={item.id} sx={{ display: 'flex', alignItems: 'center', px: 1, pl: 4, height: ROW_HEIGHT, borderTop: 1, borderColor: 'divider' }}>
                                        <Tooltip title={item.name} placement="right">
                                            <Typography variant="body2" color="text.secondary" sx={{ fontSize: '0.8rem', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: 180 }}>
                                                {item.name}
                                            </Typography>
                                        </Tooltip>
                                        {/* No Delete Button Requested */}
                                    </Box>
                                ))}
                            </Box>
                        ))}
                    </Box>

                    {/* Gantt Grid Column */}
                    <Box sx={{ flexGrow: 1, position: 'relative', minWidth: totalDays * CELL_WIDTH }}>
                        {/* Date Headers (Unchanged) */}
                        <Box sx={{
                            height: HEADER_HEIGHT,
                            position: 'absolute',
                            top: 0,
                            left: 0,
                            right: 0,
                            zIndex: 5,
                            bgcolor: 'background.paper',
                            borderBottom: 1,
                            borderColor: 'divider'
                        }}>
                            {/* Months */}
                            <Box sx={{ display: 'flex', height: HEADER_ROW_HEIGHT }}>
                                {Array.from({ length: Math.ceil(totalDays / 25) }).map((_, m) => (
                                    <Box key={`m-${m}`} sx={{ width: 25 * CELL_WIDTH, flexShrink: 0, borderRight: 1, borderColor: 'divider', borderBottom: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.75rem', fontWeight: 'bold', color: 'text.secondary', bgcolor: 'background.paper' }}>
                                        Month {m + 1}
                                    </Box>
                                ))}
                            </Box>
                            {/* Weeks */}
                            <Box sx={{ display: 'flex', height: HEADER_ROW_HEIGHT }}>
                                {Array.from({ length: Math.ceil(totalDays / 5) }).map((_, w) => (
                                    <Box key={`w-${w}`} sx={{ width: 5 * CELL_WIDTH, flexShrink: 0, borderRight: 1, borderColor: 'divider', borderBottom: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.7rem', color: 'text.secondary' }}>
                                        W{w + 1}
                                    </Box>
                                ))}
                            </Box>
                            {/* Days */}
                            <Box sx={{ display: 'flex', height: HEADER_ROW_HEIGHT }}>
                                {Array.from({ length: totalDays }).map((_, i) => (
                                    <Box key={i} sx={{ width: CELL_WIDTH, flexShrink: 0, borderRight: 1, borderColor: 'divider', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.7rem', color: 'text.secondary', bgcolor: 'transparent' }}>
                                        {i + 1}
                                    </Box>
                                ))}
                            </Box>
                        </Box>

                        <Box sx={{ height: HEADER_HEIGHT }} />

                        {/* Rows & Bars */}
                        {phases.map((phase) => (
                            <Box
                                key={phase.id}
                                sx={{
                                    borderBottom: 1,
                                    borderColor: 'divider',
                                    position: 'relative',
                                    bgcolor: 'background.paper',
                                }}
                            >
                                {/* Phase Row (Summary Bar) */}
                                <Box
                                    sx={{ height: ROW_HEIGHT, position: 'relative', width: '100%', bgcolor: 'action.hover' }}
                                >
                                    {/* Grid Lines */}
                                    <Box sx={{ position: 'absolute', top: 0, left: 0, right: 0, bottom: 0, display: 'flex', pointerEvents: 'none' }}>
                                        {Array.from({ length: totalDays }).map((_, i) => (
                                            <Box key={i} sx={{ width: CELL_WIDTH, flexShrink: 0, borderRight: 1, borderColor: 'divider' }} />
                                        ))}
                                    </Box>

                                    <Box
                                        sx={{
                                            position: 'absolute',
                                            left: (phase.startDay - 1) * CELL_WIDTH,
                                            width: phase.duration * CELL_WIDTH,
                                            height: 24,
                                            top: (ROW_HEIGHT - 24) / 2,
                                            bgcolor: 'text.disabled',
                                            borderRadius: 1,
                                            cursor: dragState?.phaseId === phase.id ? 'grabbing' : 'grab',
                                            display: 'flex',
                                            alignItems: 'center',
                                            px: 1,
                                            color: 'background.default',
                                            fontSize: '0.75rem',
                                            userSelect: 'none',
                                            opacity: 0.6,
                                            zIndex: 2
                                        }}
                                        onMouseDown={(e) => handleMouseDown(e, 'phase', phase.id, undefined, 'move', phase.startDay, phase.duration)}
                                    >
                                        <Typography variant="caption" noWrap sx={{ color: 'inherit' }}>{phase.name}</Typography>
                                    </Box>
                                </Box>

                                {/* Item Rows - Right */}
                                {phase.items && phase.items.map(item => (
                                    <Box key={item.id} sx={{ height: ROW_HEIGHT, position: 'relative', width: '100%', borderTop: 1, borderColor: 'divider' }}>
                                        <Box sx={{ position: 'absolute', top: 0, left: 0, right: 0, bottom: 0, display: 'flex', pointerEvents: 'none' }}>
                                            {Array.from({ length: totalDays }).map((_, i) => (
                                                <Box key={i} sx={{ width: CELL_WIDTH, flexShrink: 0, borderRight: 1, borderColor: 'divider' }} />
                                            ))}
                                        </Box>

                                        <Box
                                            sx={{
                                                position: 'absolute',
                                                left: (phase.startDay + item.startDayOffset - 1) * CELL_WIDTH,
                                                width: item.duration * CELL_WIDTH,
                                                height: 20,
                                                top: (ROW_HEIGHT - 20) / 2,
                                                bgcolor: 'primary.main',
                                                borderRadius: 1,
                                                cursor: dragState?.itemId === item.id ? 'grabbing' : 'grab',
                                                display: 'flex',
                                                alignItems: 'center',
                                                justifyContent: 'space-between',
                                                px: 1,
                                                color: 'white',
                                                fontSize: '0.70rem',
                                                userSelect: 'none',
                                                boxShadow: 1,
                                                zIndex: 3
                                            }}
                                            onMouseDown={(e) => handleMouseDown(e, 'item', phase.id, item.id, 'move', item.startDayOffset, item.duration)}
                                        >
                                            <Tooltip title={item.name}>
                                                <Box component="span" sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', width: '100%' }}>
                                                    {/* Split Name Logic for clearer display */}
                                                    {item.name.includes(' - ') ? (
                                                        <>
                                                            <strong>{item.name.split(' - ')[0]}</strong>
                                                            <span style={{ opacity: 0.8 }}> - {item.name.split(' - ')[1]}</span>
                                                        </>
                                                    ) : item.name}
                                                </Box>
                                            </Tooltip>
                                            <Box
                                                sx={{ width: 8, height: '100%', cursor: 'ew-resize', position: 'absolute', right: 0, top: 0, '&:hover': { bgcolor: 'rgba(255,255,255,0.2)' } }}
                                                onMouseDown={(e) => handleMouseDown(e, 'item', phase.id, item.id, 'resize', item.startDayOffset, item.duration)}
                                            />
                                        </Box>
                                    </Box>
                                ))}
                            </Box>
                        ))}
                    </Box>
                </Box>
            </Paper >
        </Stack>
    );
}
