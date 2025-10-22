import Link from 'next/link';
import { useEffect, useState } from 'react';
import Swal from 'sweetalert2';
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

  const load = async () => {
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
  };

  useEffect(() => {
    load();
  }, []);

  const remove = async (id: number) => {
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
    }
  };

  const duplicate = async (id: number) => {
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
    }
  };

  const formatDate = (input: string) => {
    if (!input) return '-';
    const date = new Date(input);
    if (Number.isNaN(date.getTime())) return input;
    return date.toLocaleString();
  };

  return (
    <div className="page-container">
      <div className="page-header">
        <div>
          <h1>Project Templates</h1>
          <p>Kelola blueprint estimasi proyek untuk modul Pre-Sales.</p>
        </div>
        <Link href="/pre-sales/project-templates/new" legacyBehavior>
          <a className="btn btn-primary">Create New Template</a>
        </Link>
      </div>

      <div className="card table-wrapper">
        {loading ? (
          <p>Loading templates…</p>
        ) : templates.length === 0 ? (
          <p>
            You haven&apos;t created any project templates yet. Click <strong>&quot;Create New Template&quot;</strong> to build
            your first project blueprint.
          </p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Template Name</th>
                <th>Created By</th>
                <th>Last Modified</th>
                <th style={{ width: '240px' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {templates.map(t => (
                <tr key={t.id}>
                  <td>{t.templateName}</td>
                  <td>{t.createdBy || '—'}</td>
                  <td>{formatDate(t.lastModified)}</td>
                  <td style={{ display: 'flex', gap: '8px' }}>
                    <Link href={`/pre-sales/project-templates/${t.id}`} legacyBehavior>
                      <a className="btn btn-secondary">Edit</a>
                    </Link>
                    <button className="btn btn-secondary" onClick={() => duplicate(t.id)}>
                      Duplicate
                    </button>
                    <button className="btn btn-danger" onClick={() => remove(t.id)}>Delete</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {error && <p className="error">{error}</p>}
      </div>
    </div>
  );
}
