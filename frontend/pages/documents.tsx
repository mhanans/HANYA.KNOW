import { useEffect, useState } from 'react';
import {
  Box,
  Button,
  Card,
  CardContent,
  FormControl,
  Grid,
  InputLabel,
  MenuItem,
  Select,
  SelectChangeEvent,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import SaveIcon from '@mui/icons-material/Save';
import { apiFetch } from '../lib/api';

interface Category {
  id: number;
  name: string;
}

interface Doc {
  source: string;
  categoryId: number | null;
  pages: number;
}

export default function Documents() {
  const [docs, setDocs] = useState<Doc[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [filter, setFilter] = useState('');
  const [catFilter, setCatFilter] = useState('');

  const load = async () => {
    try {
      const [d, c] = await Promise.all([
        apiFetch('/api/documents'),
        apiFetch('/api/categories')
      ]);
      if (d.ok) setDocs(await d.json());
      if (c.ok) setCategories(await c.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => {
    load();
  }, []);

  const save = async (doc: Doc) => {
    if (!window.confirm('Anda ingin menyimpan data?')) return;
    try {
      const res = await apiFetch('/api/documents', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ source: doc.source, categoryId: doc.categoryId })
      });
      if (res.ok) {
        alert('Data berhasil disimpan');
      } else {
        alert('Gagal menyimpan data');
      }
    } catch {
      alert('Error menyimpan data');
    }
    await load();
  };

  const remove = async (source: string) => {
    if (!window.confirm('Anda ingin menghapus data?')) return;
    try {
      const res = await apiFetch(`/api/documents?source=${encodeURIComponent(source)}`, {
        method: 'DELETE'
      });
      if (res.ok) {
        alert('Data berhasil dihapus');
      } else {
        alert('Gagal menghapus data');
      }
    } catch {
      alert('Error menghapus data');
    }
    await load();
  };

  const filtered = docs.filter(d => {
    const matchesText =
      !filter || d.source.toLowerCase().includes(filter.toLowerCase());
    const matchesCategory =
      !catFilter || String(d.categoryId ?? '') === catFilter;
    return matchesText && matchesCategory;
  });

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Typography variant="h1">All Documents</Typography>
      <Grid container spacing={2}>
        <Grid item xs={12} md={6} lg={4}>
          <TextField
            fullWidth
            label="Search"
            value={filter}
            onChange={e => setFilter(e.target.value)}
          />
        </Grid>
        <Grid item xs={12} md={6} lg={4}>
          <FormControl fullWidth>
            <InputLabel id="category-filter-label">Category</InputLabel>
            <Select
              labelId="category-filter-label"
              label="Category"
              value={catFilter}
              onChange={e => setCatFilter(e.target.value)}
            >
              <MenuItem value="">All categories</MenuItem>
              {categories.map(c => (
                <MenuItem key={c.id} value={String(c.id)}>
                  {c.name}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        </Grid>
      </Grid>
      <Card>
        <CardContent>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>Document</TableCell>
                <TableCell>Category</TableCell>
                <TableCell>Pages</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {filtered.map(d => (
                <TableRow key={d.source} hover>
                  <TableCell>{d.source}</TableCell>
                  <TableCell>
                    <FormControl fullWidth size="small">
                      <InputLabel id={`category-${d.source}`}>Category</InputLabel>
                      <Select
                        labelId={`category-${d.source}`}
                        label="Category"
                        value={d.categoryId !== null ? String(d.categoryId) : ''}
                        onChange={(event: SelectChangeEvent<string>) =>
                          setDocs(prev =>
                            prev.map(p =>
                              p.source === d.source
                                ? {
                                    ...p,
                                    categoryId: event.target.value ? Number(event.target.value) : null,
                                  }
                                : p
                            )
                          )
                        }
                      >
                        <MenuItem value="">No category</MenuItem>
                        {categories.map(c => (
                          <MenuItem key={c.id} value={String(c.id)}>
                            {c.name}
                          </MenuItem>
                        ))}
                      </Select>
                    </FormControl>
                  </TableCell>
                  <TableCell>{d.pages}</TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                      <Button
                        variant="contained"
                        color="primary"
                        startIcon={<SaveIcon />}
                        onClick={() => save(d)}
                      >
                        Save
                      </Button>
                      <Button
                        variant="outlined"
                        color="error"
                        startIcon={<DeleteIcon />}
                        onClick={() => remove(d.source)}
                      >
                        Delete
                      </Button>
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </Box>
  );
}

