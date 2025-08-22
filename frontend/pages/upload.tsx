import { useEffect, useState } from 'react';
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
    <div className="card docs-card">
      <h1>Upload Document</h1>
      <p className="hint">Upload new PDF documents.</p>

      <div className="upload-grid">
        <label>Files</label>
        <input type="file" multiple accept="application/pdf" onChange={e => {
          const f = Array.from(e.target.files ?? []);
          setFiles(f);
          if (f.length > 0) setTitle(f[0].name.replace(/\.pdf$/i, ''));
        }} />
        <label>Title</label>
        <input
          placeholder="Document title (optional)"
          value={title}
          onChange={e => setTitle(e.target.value)}
        />
        <label>Text</label>
        <textarea
          placeholder="Text content (optional)"
          value={text}
          onChange={e => setText(e.target.value)}
        />
        <label>Category</label>
        <select value={category} onChange={e => setCategory(e.target.value)}>
          <option value="">No category</option>
          {categories.map(c => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
      </div>
      <div className="actions">
        <button onClick={upload} disabled={loading}>{loading ? 'Uploading...' : 'Upload'}</button>
      </div>
      {status && <p className={status.startsWith('Upload failed') || status.startsWith('Error') ? 'error' : 'success'}>{status}</p>}

      <style jsx>{`
        .docs-card { max-width: none; }
        .upload-grid {
          display: grid;
          grid-template-columns: 150px 1fr;
          gap: 0.5rem 1rem;
          align-items: start;
        }
        .upload-grid textarea {
          min-height: 100px;
          width: 100%;
        }
        .upload-grid select { width: 100%; }
        .actions { margin-top: 0.5rem; }
        @media (max-width: 600px) {
          .upload-grid {
            grid-template-columns: 1fr;
          }
        }
      `}</style>
    </div>
  );
}

