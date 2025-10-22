import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import Swal from 'sweetalert2';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CardHeader,
  IconButton,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import FileCopyIcon from '@mui/icons-material/FileCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import { apiFetch } from '../../../lib/api';

interface ProjectTemplateMetadata {
  id: number;
  templateName: string;
  createdBy: string;
  lastModified: string;
}

export default function ProjectTemplates() {
  const [templates, setTemplates] = useState<ProjectTemplateMetadata[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [duplicatingId, setDuplicatingId] = useState<number | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await apiFetch('/api/templates');
      if (!res.ok) {
        throw new Error(await res.text());
      }
      const data: ProjectTemplateMetadata[] = await res.json();
      setTemplates(data);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to load templates';
      setError(message);
      await Swal.fire({
        icon: 'error',
        title: 'Unable to load templates',
        text: message,
      });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const remove = useCallback(
    async (id: number) => {
      const confirmation = await Swal.fire({
        title: 'Delete this template?',
        text: 'This action cannot be undone.',
        icon: 'warning',
        confirmButtonText: 'Delete',
        confirmButtonColor: '#ef4444',
        cancelButtonText: 'Cancel',
        showCancelButton: true,
        reverseButtons: true,
      });
      if (!confirmation.isConfirmed) return;

      setDeletingId(id);
      setError('');
      try {
        const res = await apiFetch(`/api/templates/${id}`, { method: 'DELETE' });
        if (!res.ok) {
          throw new Error(await res.text());
        }
        await load();
        await Swal.fire({
          icon: 'success',
          title: 'Template deleted',
          text: 'The project template was removed successfully.',
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to delete template';
        setError(message);
        await Swal.fire({
          icon: 'error',
          title: 'Delete failed',
          text: message,
        });
      } finally {
        setDeletingId(current => (current === id ? null : current));
      }
    },
    [load]
  );

  const duplicate = useCallback(
    async (id: number) => {
      setDuplicatingId(id);
      setError('');
      try {
        const res = await apiFetch(`/api/templates/${id}/duplicate`, { method: 'POST' });
        if (!res.ok) {
          throw new Error(await res.text());
        }
        const created: { templateName: string } = await res.json();
        await load();
        await Swal.fire({
          icon: 'success',
          title: 'Template duplicated',
          text: `Template "${created.templateName}" duplicated successfully.`,
        });
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to duplicate template';
        setError(message);
        await Swal.fire({
          icon: 'error',
          title: 'Duplicate failed',
          text: message,
        });
      } finally {
        setDuplicatingId(current => (current === id ? null : current));
      }
    },
    [load]
  );

  const formatDate = (input: string) => {
    if (!input) return '—';
    const date = new Date(input);
    if (Number.isNaN(date.getTime())) return input;
    return date.toLocaleString();
  };

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6 }}>
      <Stack spacing={4}>
        <Box>
          <Typography variant="h1" gutterBottom>Pre-Sales Project Templates</Typography>
          <Typography variant="body1" color="text.secondary">
            Manage the reusable estimation blueprints that power the pre-sales assessment workspace.
          </Typography>
        </Box>

        <Card>
          <CardHeader
            title="Templates"
            action={(
              <Button
                variant="contained"
                startIcon={<AddIcon />}
                component={Link}
                href="/pre-sales/project-templates/new"
              >
                New Template
              </Button>
            )}
          />
          {loading && <LinearProgress />}
          <CardContent>
            <Stack spacing={2}>
              {error && <Alert severity="error">{error}</Alert>}
              {templates.length === 0 ? (
                loading ? (
                  <Typography variant="body2" color="text.secondary">
                    Loading templates…
                  </Typography>
                ) : (
                  <Typography variant="body2" color="text.secondary">
                    You haven&apos;t created any project templates yet. Use the <strong>New Template</strong> button to add your
                    first blueprint.
                  </Typography>
                )
              ) : (
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Template Name</TableCell>
                        <TableCell>Created By</TableCell>
                        <TableCell>Last Modified</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {templates.map(template => (
                        <TableRow key={template.id} hover>
                          <TableCell>{template.templateName}</TableCell>
                          <TableCell>{template.createdBy || '—'}</TableCell>
                          <TableCell>{formatDate(template.lastModified)}</TableCell>
                          <TableCell align="right">
                            <Stack direction="row" spacing={1} justifyContent="flex-end">
                              <Tooltip title="Edit template">
                                <span>
                                  <IconButton
                                    color="primary"
                                    component={Link}
                                    href={`/pre-sales/project-templates/${template.id}`}
                                  >
                                    <EditIcon fontSize="small" />
                                  </IconButton>
                                </span>
                              </Tooltip>
                              <Tooltip title="Duplicate template">
                                <span>
                                  <IconButton
                                    color="info"
                                    onClick={() => duplicate(template.id)}
                                    disabled={duplicatingId === template.id || deletingId === template.id}
                                  >
                                    <FileCopyIcon fontSize="small" />
                                  </IconButton>
                                </span>
                              </Tooltip>
                              <Tooltip title="Delete template">
                                <span>
                                  <IconButton
                                    color="error"
                                    onClick={() => remove(template.id)}
                                    disabled={deletingId === template.id || duplicatingId === template.id}
                                  >
                                    <DeleteIcon fontSize="small" />
                                  </IconButton>
                                </span>
                              </Tooltip>
                            </Stack>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              )}
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </Box>
  );
}
