import { useState, useEffect } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import CloudUploadIcon from '@mui/icons-material/CloudUpload';
import { apiFetch } from '../lib/api';

interface Category {
  id: number;
  name: string;
}

export default function Ingest() {
  const [files, setFiles] = useState<File[]>([]);
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [status, setStatus] = useState('');
  const [categories, setCategories] = useState<Category[]>([]);
  const [category, setCategory] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const load = async () => {
      try {
        const res = await apiFetch('/api/categories');
        if (res.ok) setCategories(await res.json());
      } catch {
        /* ignore */
      }
    };
    load();
  }, []);

  const submit = async () => {
    setStatus('');
    if (files.length === 0 && !text.trim()) {
      setStatus('Please provide one or more PDF files or some text to upload.');
      return;
    }
    const form = new FormData();
    files.forEach(f => form.append('files', f));
    if (title && text) form.append('title', title);
    if (text) form.append('text', text);
    if (category) form.append('categoryId', category);
    setLoading(true);
    setStatus('Uploading...');
    try {
      const res = await apiFetch('/api/ingest', {
        method: 'POST',
        body: form
      });
      if (!res.ok) {
        let msg = res.statusText;
        try {
          const data = await res.json();
          if (data?.detail) msg = data.detail;
        } catch {
          try {
            msg = await res.text();
          } catch {
            /* ignore */
          }
        }
        setStatus(`Upload failed: ${msg}`);
        return;
      }
      setStatus('Upload successful');
    } catch (err) {
      setStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Box sx={{ maxWidth: 960, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Typography variant="h1">Upload Document</Typography>
      <Card>
        <CardContent>
          <Stack spacing={3}>
            <Typography color="text.secondary">
              Provide PDF files or paste text below to add them to the knowledge base.
            </Typography>
            <Stack spacing={2}>
              <Button
                component="label"
                variant="outlined"
                startIcon={<CloudUploadIcon />}
                color="primary"
              >
                Choose PDF files
                <input
                  hidden
                  type="file"
                  multiple
                  accept="application/pdf"
                  onChange={e => setFiles(Array.from(e.target.files ?? []))}
                />
              </Button>
              {files.length > 0 && (
                <Typography variant="body2" color="text.secondary">
                  Selected: {files.map(f => f.name).join(', ')}
                </Typography>
              )}
              <TextField
                label="Title"
                placeholder="Document title (optional)"
                value={title}
                onChange={e => setTitle(e.target.value)}
                fullWidth
              />
              <TextField
                label="Text"
                placeholder="Text content (optional)"
                value={text}
                onChange={e => setText(e.target.value)}
                fullWidth
                multiline
                minRows={4}
              />
              <FormControl fullWidth>
                <InputLabel id="ingest-category">Category</InputLabel>
                <Select
                  labelId="ingest-category"
                  label="Category"
                  value={category}
                  onChange={e => setCategory(e.target.value)}
                >
                  <MenuItem value="">No category</MenuItem>
                  {categories.map(c => (
                    <MenuItem key={c.id} value={String(c.id)}>
                      {c.name}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Stack>
            <Button variant="contained" color="primary" onClick={submit} disabled={loading}>
              {loading ? 'Uploading...' : 'Upload'}
            </Button>
            {status && (
              <Alert severity={status.startsWith('Upload failed') || status.startsWith('Error') ? 'error' : 'success'}>
                {status}
              </Alert>
            )}
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}
