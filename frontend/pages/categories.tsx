import { useState, useEffect } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
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
import AddIcon from '@mui/icons-material/Add';
import SaveIcon from '@mui/icons-material/Save';
import { apiFetch } from '../lib/api';

interface Category {
  id: number;
  name: string;
}

export default function Categories() {
  const [categories, setCategories] = useState<Category[]>([]);
  const [name, setName] = useState('');
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const res = await apiFetch('/api/categories');
      if (res.ok) setCategories(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const create = async () => {
    setError('');
    try {
      const res = await apiFetch('/api/categories', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name })
      });
      if (!res.ok) throw new Error(await res.text());
      setName('');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const update = async (id: number, newName: string) => {
    setError('');
    try {
      const res = await apiFetch(`/api/categories/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: newName })
      });
      if (!res.ok) throw new Error(await res.text());
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const remove = async (id: number) => {
    setError('');
    try {
      const res = await apiFetch(`/api/categories/${id}`, { method: 'DELETE' });
      if (!res.ok) {
        let msg = res.statusText;
        try { const data = await res.json(); if (data?.detail) msg = data.detail; } catch {}
        throw new Error(msg);
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <Box sx={{ maxWidth: 960, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Typography variant="h1">Manage Categories</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems="flex-start">
        <TextField
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder="New category"
          label="New category"
          fullWidth
        />
        <Button
          variant="contained"
          color="primary"
          startIcon={<AddIcon />}
          onClick={create}
          sx={{ whiteSpace: 'nowrap' }}
        >
          Add
        </Button>
      </Stack>
      <Card>
        <CardContent>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {categories.map(c => (
                <TableRow key={c.id} hover>
                  <TableCell>
                    <TextField
                      value={c.name}
                      onChange={e =>
                        setCategories(prev =>
                          prev.map(p => (p.id === c.id ? { ...p, name: e.target.value } : p))
                        )
                      }
                      fullWidth
                    />
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                      <Button
                        variant="contained"
                        color="primary"
                        startIcon={<SaveIcon />}
                        onClick={() => update(c.id, c.name)}
                      >
                        Save
                      </Button>
                      <Button
                        variant="outlined"
                        color="error"
                        startIcon={<DeleteIcon />}
                        onClick={() => remove(c.id)}
                      >
                        Delete
                      </Button>
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {error && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {error}
            </Alert>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}
