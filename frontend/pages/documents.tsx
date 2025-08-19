import { useEffect, useState } from 'react';

interface Category {
  id: number;
  name: string;
}

interface Doc {
  source: string;
  categoryId: number | null;
}

export default function Documents() {
  const [files, setFiles] = useState<File[]>([]);
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [status, setStatus] = useState('');
  const [categories, setCategories] = useState<Category[]>([]);
  const [category, setCategory] = useState('');
  const [docs, setDocs] = useState<Doc[]>([]);

  const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');

  const load = async () => {
    try {
      const [catRes, docRes] = await Promise.all([
        fetch(`${base}/api/categories`),
        fetch(`${base}/api/documents`)
      ]);
      if (catRes.ok) setCategories(await catRes.json());
      if (docRes.ok) setDocs(await docRes.json());
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
    const form = new FormData();
    files.forEach(f => form.append('files', f));
    if (title && text) form.append('title', title);
    if (text) form.append('text', text);
    if (category) form.append('categoryId', category);
    try {
      const res = await fetch(`${base}/api/ingest`, { method: 'POST', body: form });
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
    }
  };

  const save = async (doc: Doc) => {
    await fetch(`${base}/api/documents`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ source: doc.source, categoryId: doc.categoryId })
    });
    await load();
  };

  const remove = async (source: string) => {
    await fetch(`${base}/api/documents?source=${encodeURIComponent(source)}`, { method: 'DELETE' });
    await load();
  };

  return (
    <div className="card docs-card">
      <h1>Manage Documents</h1>
      <p className="hint">Upload new documents or manage existing ones.</p>

      <div className="upload-grid">
        <label>Files</label>
        <input type="file" multiple onChange={e => setFiles(Array.from(e.target.files ?? []))} />
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
        <button onClick={upload}>Upload</button>
      </div>
      {status && <p className={status.startsWith('Upload failed') || status.startsWith('Error') ? 'error' : 'success'}>{status}</p>}

      <h2>Existing Documents</h2>
      <div className="doc-grid head">
        <div>Document</div>
        <div>Category</div>
        <div>Actions</div>
      </div>
      {docs.map(d => (
        <div className="doc-grid row" key={d.source}>
          <div className="name">{d.source}</div>
          <select
            value={d.categoryId ?? ''}
            onChange={e =>
              setDocs(prev => prev.map(p => p.source === d.source ? { ...p, categoryId: e.target.value ? Number(e.target.value) : null } : p))
            }
          >
            <option value="">No category</option>
            {categories.map(c => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
          <div className="actions">
            <button onClick={() => save(d)}>Save</button>
            <button onClick={() => remove(d.source)}>Delete</button>
          </div>
        </div>
      ))}

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
        .doc-grid {
          display: grid;
          grid-template-columns: 1fr 200px auto;
          gap: 0.5rem;
          align-items: center;
          padding: 0.25rem 0;
        }
        .doc-grid.head { font-weight: 600; background: #e0e7ff; padding: 0.5rem; }
        .doc-grid.row { border-top: 1px solid #ddd; }
        .doc-grid .name { overflow-wrap: anywhere; }
        @media (max-width: 600px) {
          .upload-grid {
            grid-template-columns: 1fr;
          }
          .doc-grid {
            grid-template-columns: 1fr;
          }
          .doc-grid.head {
            display: none;
          }
        }
      `}</style>
    </div>
  );
}

