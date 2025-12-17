import { useState, useRef, useEffect } from 'react';
import {
    Box,
    Paper,
    Stack,
    TextField,
    Typography,
    Select,
    MenuItem,
    Chip,
    FormControl,
    InputLabel,
    OutlinedInput,
    SelectChangeEvent,
    Theme
} from '@mui/material';
import { useTheme } from '@mui/material/styles';

// Reuse types or import them if shared. For now redefining locally to avoid circular deps if not in a shared file.
// Ideally these should be in a shared `types.ts`.
export interface TimelineItem {
    id: string;
    name: string;
    duration: number;
    startDayOffset: number;
    roles?: string[]; // New property
}

export interface TimelinePhase {
    id: string;
    name: string;
    duration: number; // days
    startDay: number; // 1-indexed
    items: TimelineItem[];
}

interface TemplateRoleMapperProps {
    phases: TimelinePhase[];
    onChange: (phases: TimelinePhase[]) => void;
    availableRoles: string[];
}

const CELL_WIDTH = 40;
const HEADER_ROW_HEIGHT = 32;
const HEADER_HEIGHT = HEADER_ROW_HEIGHT * 3; // Month, Week, Day
const ROW_HEIGHT = 50; // Slightly taller to accommodate dropdowns if needed, or keep 42
const LABEL_WIDTH = 350; // Wider to fit Dropdown + Name

function getStyles(name: string, personName: readonly string[], theme: Theme) {
    return {
        fontWeight:
            personName.indexOf(name) === -1
                ? theme.typography.fontWeightRegular
                : theme.typography.fontWeightMedium,
    };
}

export default function TemplateRoleMapper({ phases, onChange, availableRoles }: TemplateRoleMapperProps) {
    const theme = useTheme();
    const [totalDays, setTotalDays] = useState(90);
    const containerRef = useRef<HTMLDivElement>(null);

    // Auto-expand total days if phases go beyond (Visual only)
    useEffect(() => {
        const maxDay = Math.max(...phases.map(p => p.startDay + p.duration), 85) + 5;
        if (maxDay > totalDays) {
            setTotalDays(Math.ceil(maxDay / 30) * 30);
        }
    }, [phases, totalDays]);

    const handleRoleChange = (phaseId: string, itemId: string, event: SelectChangeEvent<string[]>) => {
        const {
            target: { value },
        } = event;
        const newRoles = typeof value === 'string' ? value.split(',') : value;

        onChange(phases.map(p => {
            if (p.id !== phaseId) return p;
            const newItems = p.items.map(i => i.id === itemId ? { ...i, roles: newRoles } : i);
            return { ...p, items: newItems };
        }));
    };

    return (
        <Stack spacing={2}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Box>
                    <Typography variant="h2">Role Mapping</Typography>
                    <Typography variant="body2" color="text.secondary">
                        Assign roles to timeline items.
                    </Typography>
                </Box>
            </Stack>

            <Paper variant="outlined" sx={{ overflowX: 'auto', p: 2, bgcolor: 'background.paper', borderColor: 'divider' }}>
                <Box sx={{ display: 'flex' }}>
                    {/* Headers Column (Name + Role Dropdown) */}
                    <Box sx={{ width: LABEL_WIDTH, flexShrink: 0, pt: `${HEADER_HEIGHT}px`, borderRight: 1, borderColor: 'divider', bgcolor: 'background.paper', zIndex: 10 }}>
                        {phases.map((phase) => (
                            <Box key={phase.id} sx={{ borderBottom: 1, borderColor: 'divider' }}>
                                {/* Phase Header Row - Left */}
                                <Box sx={{ display: 'flex', alignItems: 'center', px: 1, height: ROW_HEIGHT, bgcolor: 'action.hover' }}>
                                    <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
                                        {phase.name}
                                    </Typography>
                                </Box>
                                {/* Items List - Left */}
                                {phase.items && phase.items.map(item => (
                                    <Box key={item.id} sx={{ display: 'flex', alignItems: 'center', px: 1, height: ROW_HEIGHT, borderTop: 1, borderColor: 'divider', justifyContent: 'space-between' }}>
                                        <Box sx={{ flex: 1, mr: 1, overflow: 'hidden' }}>
                                            <Typography variant="body2" color="text.secondary" sx={{ fontSize: '0.8rem', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                {item.name}
                                            </Typography>
                                        </Box>

                                        {/* Role Dropdown */}
                                        <FormControl size="small" sx={{ width: 140 }}>
                                            <Select
                                                multiple
                                                displayEmpty
                                                value={item.roles || []}
                                                onChange={(e) => handleRoleChange(phase.id, item.id, e)}
                                                input={<OutlinedInput />}
                                                renderValue={(selected) => {
                                                    if (selected.length === 0) {
                                                        return <em style={{ opacity: 0.5, fontSize: '0.75rem' }}>Select Role</em>;
                                                    }
                                                    return (
                                                        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                                                            {(selected as string[]).map((value) => (
                                                                <Chip key={value} label={value} size="small" sx={{ height: 20, fontSize: '0.65rem' }} />
                                                            ))}
                                                        </Box>
                                                    );
                                                }}
                                                MenuProps={{
                                                    PaperProps: {
                                                        style: {
                                                            maxHeight: 200,
                                                            width: 250,
                                                        },
                                                    },
                                                }}
                                                sx={{
                                                    height: 32,
                                                    fontSize: '0.8rem',
                                                    '& .MuiSelect-select': { py: 0.5, px: 1, display: 'flex', alignItems: 'center' }
                                                }}
                                            >
                                                <MenuItem disabled value="">
                                                    <em>Select Roles</em>
                                                </MenuItem>
                                                {availableRoles.map((role) => (
                                                    <MenuItem
                                                        key={role}
                                                        value={role}
                                                        style={getStyles(role, item.roles || [], theme)}
                                                    >
                                                        {role}
                                                    </MenuItem>
                                                ))}
                                            </Select>
                                        </FormControl>
                                    </Box>
                                ))}
                            </Box>
                        ))}
                    </Box>

                    {/* Gantt Grid Column (Read Only) */}
                    <Box sx={{ flexGrow: 1, position: 'relative' }}>
                        {/* Date Headers */}
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
                                {Array.from({ length: Math.ceil(totalDays / 30) }).map((_, m) => (
                                    <Box key={`m-${m}`} sx={{
                                        width: 30 * CELL_WIDTH,
                                        borderRight: 1,
                                        borderColor: 'divider',
                                        borderBottom: 1,
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center',
                                        fontSize: '0.75rem',
                                        fontWeight: 'bold',
                                        color: 'text.secondary',
                                        bgcolor: 'background.paper'
                                    }}>
                                        Month {m + 1}
                                    </Box>
                                ))}
                            </Box>
                            {/* Weeks */}
                            <Box sx={{ display: 'flex', height: HEADER_ROW_HEIGHT }}>
                                {Array.from({ length: Math.ceil(totalDays / 7) }).map((_, w) => (
                                    <Box key={`w-${w}`} sx={{
                                        width: 7 * CELL_WIDTH,
                                        borderRight: 1,
                                        borderColor: 'divider',
                                        borderBottom: 1,
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center',
                                        fontSize: '0.7rem',
                                        color: 'text.secondary'
                                    }}>
                                        W{w + 1}
                                    </Box>
                                ))}
                            </Box>
                            {/* Days */}
                            <Box sx={{ display: 'flex', height: HEADER_ROW_HEIGHT }}>
                                {Array.from({ length: totalDays }).map((_, i) => (
                                    <Box
                                        key={i}
                                        sx={{
                                            width: CELL_WIDTH,
                                            flexShrink: 0,
                                            borderRight: 1,
                                            borderColor: 'divider',
                                            display: 'flex',
                                            alignItems: 'center',
                                            justifyContent: 'center',
                                            fontSize: '0.7rem',
                                            color: 'text.secondary',
                                            bgcolor: (i + 1) % 7 === 0 || (i + 1) % 7 === 6 ? 'action.hover' : 'transparent'
                                        }}
                                    >
                                        {i + 1}
                                    </Box>
                                ))}
                            </Box>
                        </Box>

                        {/* Spacer for Headers */}
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
                                {/* Phase Row (Summary Bar) - Right */}
                                <Box sx={{ height: ROW_HEIGHT, position: 'relative', width: '100%', bgcolor: 'action.hover' }}>
                                    {/* Grid Lines Overlay */}
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
                                            display: 'flex',
                                            alignItems: 'center',
                                            px: 1,
                                            color: 'background.default',
                                            fontSize: '0.75rem',
                                            userSelect: 'none',
                                            opacity: 0.6,
                                            zIndex: 2
                                        }}
                                    >
                                        <Typography variant="caption" noWrap sx={{ color: 'inherit' }}>{phase.name}</Typography>
                                    </Box>
                                </Box>

                                {/* Item Rows - Right */}
                                {phase.items && phase.items.map(item => (
                                    <Box key={item.id} sx={{ height: ROW_HEIGHT, position: 'relative', width: '100%', borderTop: 1, borderColor: 'divider' }}>
                                        {/* Grid Lines Overlay */}
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
                                                display: 'flex',
                                                alignItems: 'center',
                                                justifyContent: 'space-between',
                                                px: 1,
                                                color: 'white',
                                                fontSize: '0.70rem',
                                                userSelect: 'none',
                                                boxShadow: 1,
                                                zIndex: 3,
                                                opacity: 0.8 // Read only feel
                                            }}
                                        >
                                            <Box component="span" sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                                {item.name}
                                            </Box>
                                        </Box>
                                    </Box>
                                ))}
                            </Box>
                        ))}
                    </Box>
                </Box>
            </Paper >
        </Stack >
    );
}
