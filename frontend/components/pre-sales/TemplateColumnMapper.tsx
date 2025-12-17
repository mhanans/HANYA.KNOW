import React from 'react';
import {
    Box,
    Paper,
    Stack,
    Typography,
    FormControl,
    Select,
    MenuItem,
    Checkbox,
    ListItemText,
    OutlinedInput,
    Chip,
    Divider,
    IconButton,
    Collapse
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';

// Interfaces mirroring those in TemplateEditorPage
interface TemplateItem {
    itemId: string;
    itemName: string;
    itemDetail: string;
    category: string;
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

interface TemplateColumnMapperProps {
    sections: TemplateSection[];
    estimationColumns: string[];
    onChange: (sections: TemplateSection[]) => void;
}

const ITEM_HEIGHT = 48;
const ITEM_PADDING_TOP = 8;
const MenuProps = {
    PaperProps: {
        style: {
            maxHeight: ITEM_HEIGHT * 4.5 + ITEM_PADDING_TOP,
            width: 250,
        },
    },
};

export default function TemplateColumnMapper({ sections, estimationColumns, onChange }: TemplateColumnMapperProps) {
    // Determine if a section should be expanded. Visual state only.
    // Default all expanded for visibility.
    const [expandedIds, setExpandedIds] = React.useState<Set<string>>(new Set(sections.map(s => s.uid || s.sectionName)));

    const handleToggleExpand = (id: string) => {
        setExpandedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            return next;
        });
    };

    const handleSectionEffortChange = (sectionIndex: number, event: any) => {
        const { target: { value } } = event;
        const newEffort = typeof value === 'string' ? value.split(',') : value;

        const newSections = [...sections];
        newSections[sectionIndex] = {
            ...newSections[sectionIndex],
            effort: newEffort
        };
        onChange(newSections);
    };

    const handleItemEffortChange = (sectionIndex: number, itemIndex: number, event: any) => {
        const { target: { value } } = event;
        const newEffort = typeof value === 'string' ? value.split(',') : value;

        const newSections = [...sections];
        const newItems = [...newSections[sectionIndex].items];
        newItems[itemIndex] = {
            ...newItems[itemIndex],
            effort: newEffort
        };
        newSections[sectionIndex] = {
            ...newSections[sectionIndex],
            items: newItems
        };
        onChange(newSections);
    };

    return (
        <Stack spacing={4}>
            <Box>
                <Typography variant="h2" gutterBottom>Column Mapping</Typography>
                <Typography variant="body1" color="text.secondary">
                    Map sections and items to estimation columns.
                    For AI-Generated sections, the mapping applies to all generated items.
                    For Project-Level sections, map individual items.
                </Typography>
            </Box>

            {sections.map((section, sIndex) => {
                const isAiGenerated = section.type === 'AI-Generated';
                const sectionId = section.uid || section.sectionName;
                const isExpanded = expandedIds.has(sectionId);

                return (
                    <Paper key={sectionId} variant="outlined" sx={{ overflow: 'hidden' }}>
                        {/* Section Header */}
                        <Box sx={{
                            p: 2,
                            bgcolor: 'action.hover',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            borderBottom: isExpanded ? 1 : 0,
                            borderColor: 'divider'
                        }}>
                            <Stack direction="row" alignItems="center" spacing={2} flexGrow={1}>
                                <IconButton size="small" onClick={() => handleToggleExpand(sectionId)}>
                                    {isExpanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
                                </IconButton>
                                <Box>
                                    <Typography variant="subtitle1" fontWeight={600}>
                                        {section.sectionName}
                                    </Typography>
                                    <Chip
                                        label={section.type}
                                        size="small"
                                        color={isAiGenerated ? 'secondary' : 'default'}
                                        variant="outlined"
                                        sx={{ mt: 0.5, height: 20, fontSize: '0.65rem' }}
                                    />
                                </Box>
                            </Stack>

                            {/* If AI Generated, Mapping is here */}
                            {isAiGenerated && (
                                <Box sx={{ width: 300 }}>
                                    <FormControl size="small" fullWidth>
                                        <Select
                                            multiple
                                            displayEmpty
                                            value={section.effort || (section as any).roles || []}
                                            onChange={(e) => handleSectionEffortChange(sIndex, e)}
                                            input={<OutlinedInput />}
                                            renderValue={(selected) => {
                                                if (selected.length === 0) {
                                                    return <Typography variant="body2" color="text.secondary">Select Columns</Typography>;
                                                }
                                                return (
                                                    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                                                        {(selected as string[]).map((value) => (
                                                            <Chip key={value} label={value} size="small" sx={{ height: 20 }} />
                                                        ))}
                                                    </Box>
                                                );
                                            }}
                                            MenuProps={MenuProps}
                                        >
                                            {estimationColumns.map((col) => (
                                                <MenuItem key={col} value={col}>
                                                    <Checkbox checked={(section.effort || (section as any).roles || []).indexOf(col) > -1} />
                                                    <ListItemText primary={col} />
                                                </MenuItem>
                                            ))}
                                        </Select>
                                    </FormControl>
                                </Box>
                            )}
                        </Box>

                        <Collapse in={isExpanded}>
                            {!isAiGenerated && (
                                <Stack divider={<Divider />}>
                                    {section.items.map((item, iIndex) => (
                                        <Box key={item.uid || item.itemId} sx={{ p: 2, display: 'flex', alignItems: 'center', gap: 2 }}>
                                            <Box sx={{ flexGrow: 1 }}>
                                                <Typography variant="body2" fontWeight={500}>{item.itemName}</Typography>
                                            </Box>
                                            <Box sx={{ width: 300 }}>
                                                <FormControl size="small" fullWidth>
                                                    <Select
                                                        multiple
                                                        displayEmpty
                                                        value={item.effort || (item as any).roles || []}
                                                        onChange={(e) => handleItemEffortChange(sIndex, iIndex, e)}
                                                        input={<OutlinedInput />}
                                                        renderValue={(selected) => {
                                                            if (selected.length === 0) {
                                                                return <Typography variant="body2" color="text.secondary">Select Columns</Typography>;
                                                            }
                                                            return (
                                                                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                                                                    {(selected as string[]).map((value) => (
                                                                        <Chip key={value} label={value} size="small" sx={{ height: 20 }} />
                                                                    ))}
                                                                </Box>
                                                            );
                                                        }}
                                                        MenuProps={MenuProps}
                                                    >
                                                        {estimationColumns.map((col) => (
                                                            <MenuItem key={col} value={col}>
                                                                <Checkbox checked={(item.effort || (item as any).roles || []).indexOf(col) > -1} />
                                                                <ListItemText primary={col} />
                                                            </MenuItem>
                                                        ))}
                                                    </Select>
                                                </FormControl>
                                            </Box>
                                        </Box>
                                    ))}
                                    {section.items.length === 0 && (
                                        <Box sx={{ p: 2, textAlign: 'center' }}>
                                            <Typography variant="body2" color="text.secondary">No items in this section.</Typography>
                                        </Box>
                                    )}
                                </Stack>
                            )}
                            {isAiGenerated && (
                                <Box sx={{ p: 2, bgcolor: 'background.default' }}>
                                    <Typography variant="body2" color="text.secondary" fontStyle="italic">
                                        For AI-Generated sections, all generated items will automatically be mapped to the columns selected above.
                                    </Typography>
                                </Box>
                            )}
                        </Collapse>
                    </Paper>
                );
            })}

            {sections.length === 0 && (
                <Typography variant="body1" color="text.secondary" align="center" sx={{ py: 4 }}>
                    No sections defined. Please go back and add structure.
                </Typography>
            )}
        </Stack>
    );
}
