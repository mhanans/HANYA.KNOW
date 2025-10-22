import { useEffect, useState } from 'react';
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

export default function Upload() {
  const [files, setFiles] = useState<File[]>([]);
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [status, setStatus] = useState('');
  const [categories, setCategories] = useState<Category[]>([]);
  const [category, setCategory] = useState('');
  const [loading, setLoading] = useState(false);

  const load = async () => {
    try {
      const catRes = await apiFetch('/api/categories');
      if (catRes.ok) setCategories(await catRes.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const upload = async () => {
    setStatus('');
    if (files.length === 0 && !text.trim()) {
      setStatus('Please provide one or more PDF files or some text to upload.');
      return;
    }
    if (files.some(f => !f.name.toLowerCase().endsWith('.pdf'))) {
      setStatus('Only PDF files are allowed.');
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
      const res = await apiFetch('/api/ingest', { method: 'POST', body: form });
      if (!res.ok) {
        let msg = res.statusText;
        try {
          const data = await res.json();
          if (data?.detail) msg = data.detail;
        } catch {
          try { msg = await res.text(); } catch { /* ignore */ }
        }
        setStatus(`Upload failed: ${msg}`);
        return;
      }
      setStatus('Upload successful');
      setFiles([]);
      setTitle('');
      setText('');
      setCategory('');
      await load();
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
            <Typography color="text.secondary">Upload new PDF documents.</Typography>
            <Stack spacing={2}>
              <Button
                component="label"
                variant="outlined"
                color="primary"
                startIcon={<CloudUploadIcon />}
              >
                Choose PDF files
                <input
                  hidden
                  type="file"
                  multiple
                  accept="application/pdf"
                  onChange={e => {
                    const f = Array.from(e.target.files ?? []);
                    setFiles(f);
                    if (f.length > 0) setTitle(f[0].name.replace(/\.pdf$/i, ''));
                  }}
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
                <InputLabel id="upload-category">Category</InputLabel>
                <Select
                  labelId="upload-category"
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
            <Button
              variant="contained"
              color="primary"
              onClick={upload}
              disabled={loading}
            >
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

