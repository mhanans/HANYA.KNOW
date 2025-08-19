import { useState, useEffect } from 'react';

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

  useEffect(() => {
    const load = async () => {
      const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
      try {
        const res = await fetch(`${base}/api/categories`);
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
    try {
      const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
      const res = await fetch(`${base}/api/ingest`, {
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
    }
  };

  return (
    <div className="card ingest-card">
      <h1>Upload Document</h1>
      <p className="hint">Provide PDF files or paste text below to add them to the knowledge base.</p>
      <input type="file" multiple onChange={e => setFiles(Array.from(e.target.files ?? []))} />
      <input
        placeholder="Document title (optional)"
        value={title}
        onChange={e => setTitle(e.target.value)}
      />
      <textarea
        placeholder="Text content (optional)"
        value={text}
        onChange={e => setText(e.target.value)}
      />
      <select value={category} onChange={e => setCategory(e.target.value)}>
        <option value="">No category</option>
        {categories.map(c => (
          <option key={c.id} value={c.id}>{c.name}</option>
        ))}
      </select>
      <button onClick={submit}>Upload</button>
      {status && <p className={status.startsWith('Upload failed') || status.startsWith('Error') ? 'error' : 'success'}>{status}</p>}
      <style jsx>{`
        .ingest-card {
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
          align-items: center;
          width: 100%;
          max-width: 400px;
        }
        .hint {
          color: #666;
          text-align: center;
        }
        textarea {
          min-height: 100px;
          width: 100%;
        }
        select {
          width: 100%;
        }
        .error {
          color: #e00;
        }
        .success {
          color: #008000;
        }
      `}</style>
    </div>
  );
}
