import { useEffect, useState } from 'react';
import {
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
  Typography,
} from '@mui/material';
import ArticleIcon from '@mui/icons-material/Article';
import RefreshIcon from '@mui/icons-material/Refresh';
import SummarizeIcon from '@mui/icons-material/Summarize';
import VisibilityIcon from '@mui/icons-material/Visibility';
import Modal from '../components/Modal';

interface Doc { source: string; hasSummary: boolean; }

export default function DocumentAnalytics() {
  const [docs, setDocs] = useState<Doc[]>([]);
  const [summary, setSummary] = useState<string | null>(null);

  const load = async () => {
    try {
      const res = await apiFetch('/api/documents');
      if (res.ok) setDocs(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const viewSummary = async (source: string) => {
    setSummary('Loading...');
    try {
      const res = await apiFetch(`/api/documents/summary?source=${encodeURIComponent(source)}`);
      if (res.ok) {
        const data = await res.json();
        setSummary(data.summary || 'No summary available');
      } else {
        setSummary('Failed to load summary');
      }
    } catch {
      setSummary('Failed to load summary');
    }
  };

  const generateSummary = async (source: string) => {
    if (!window.confirm('Generate summary for this document?')) return;
    setSummary('Loading...');
    try {
      const res = await apiFetch(`/api/documents/summary?source=${encodeURIComponent(source)}`, {
        method: 'POST'
      });
      if (res.ok) {
        const data = await res.json();
        setSummary(data.summary || 'No summary available');
        alert('Summary generated successfully');
        setDocs(prev => prev.map(p => p.source === source ? { ...p, hasSummary: true } : p));
      } else {
        setSummary(null);
        alert('Failed to generate summary');
      }
    } catch {
      setSummary(null);
      alert('Failed to generate summary');
    }
  };

  const regenerateSummary = async (source: string) => {
    if (!window.confirm('Regenerate summary for this document?')) return;
    setSummary('Loading...');
    try {
      const res = await apiFetch(`/api/documents/summary?source=${encodeURIComponent(source)}`, {
        method: 'POST'
      });
      if (res.ok) {
        const data = await res.json();
        setSummary(data.summary || 'No summary available');
        alert('Summary generated successfully');
      } else {
        setSummary(null);
        alert('Failed to generate summary');
      }
    } catch {
      setSummary(null);
      alert('Failed to generate summary');
    }
  };

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Typography variant="h1">Document Analytics</Typography>
      <Card>
        <CardContent>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>Document</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {docs.map(d => (
                <TableRow key={d.source} hover>
                  <TableCell>
                    <Stack direction="row" spacing={2} alignItems="center">
                      <ArticleIcon />
                      <Typography>{d.source}</Typography>
                    </Stack>
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                      {d.hasSummary ? (
                        <>
                          <Button
                            variant="outlined"
                            color="secondary"
                            startIcon={<VisibilityIcon />}
                            onClick={() => viewSummary(d.source)}
                          >
                            View Summary
                          </Button>
                          <Button
                            variant="contained"
                            color="primary"
                            startIcon={<RefreshIcon />}
                            onClick={() => regenerateSummary(d.source)}
                          >
                            Retry Summary
                          </Button>
                        </>
                      ) : (
                        <Button
                          variant="contained"
                          color="primary"
                          startIcon={<SummarizeIcon />}
                          onClick={() => generateSummary(d.source)}
                        >
                          Generate Summary
                        </Button>
                      )}
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <Modal isOpen={summary !== null} onClose={() => setSummary(null)} title="Document Summary">
        <Box component="pre" sx={{ whiteSpace: 'pre-wrap', fontFamily: 'inherit' }}>
          {summary}
        </Box>
      </Modal>
    </Box>
  );
}
