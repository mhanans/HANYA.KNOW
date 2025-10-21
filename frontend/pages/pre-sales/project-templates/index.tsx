import Link from 'next/link';
import { useEffect, useState } from 'react';
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
      setError(err instanceof Error ? err.message : 'Failed to load templates');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const remove = async (id: number) => {
    if (!confirm('Delete this template? This action cannot be undone.')) return;
    setError('');
    try {
      const res = await apiFetch(`/api/templates/${id}`, { method: 'DELETE' });
      if (!res.ok) {
        throw new Error(await res.text());
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete template');
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
          <p>Tidak ada template yang tersimpan.</p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Template Name</th>
                <th>Created By</th>
                <th>Last Modified</th>
                <th style={{ width: '160px' }}>Actions</th>
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
