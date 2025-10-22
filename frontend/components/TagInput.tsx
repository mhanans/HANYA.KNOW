import React, { useId } from 'react';
import { Box, Chip, FormControl, InputLabel, MenuItem, Select } from '@mui/material';

interface Option {
  id: number;
  name: string;
}

interface Props {
  options: Option[];
  selected: number[];
  onChange: (ids: number[]) => void;
  label?: string;
}

export default function TagInput({ options, selected, onChange, label = 'Select role' }: Props) {
  const available = options.filter(o => !selected.includes(o.id));
  const add = (id: number) => onChange([...selected, id]);
  const remove = (id: number) => onChange(selected.filter(x => x !== id));
  const selectId = useId();
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
        {selected.map(id => {
          const opt = options.find(o => o.id === id);
          if (!opt) return null;
          return <Chip key={id} label={opt.name} onDelete={() => remove(id)} color="secondary" variant="outlined" />;
        })}
      </Box>
      {available.length > 0 && (
        <FormControl fullWidth size="small">
          <InputLabel id={selectId}>{label}</InputLabel>
          <Select
            labelId={selectId}
            value=""
            label={label}
            onChange={e => {
              const val = Number(e.target.value);
              if (val) add(val);
            }}
          >
            <MenuItem value="">Select...</MenuItem>
            {available.map(o => (
              <MenuItem key={o.id} value={o.id}>
                {o.name}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      )}
    </Box>
  );
}
