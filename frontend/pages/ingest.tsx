import Link from 'next/link';
import { useState } from 'react';

export default function Ingest() {
  const [files, setFiles] = useState<File[]>([]);
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [status, setStatus] = useState('');

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
    <div className="container">
      <div className="card ingest-card">
        <h1>Upload Document</h1>
        <p className="hint">Provide PDF files or paste text below to add them to the knowledge base.</p>
        <input type="file" multiple onChange={e => setFiles(Array.from(e.target.files ?? []))} />
        <input
          placeholder="Title (optional)"
          value={title}
          onChange={e => setTitle(e.target.value)}
        />
        <textarea
          placeholder="Fallback text"
          value={text}
          onChange={e => setText(e.target.value)}
        />
        <button onClick={submit}>Upload</button>
        {status && <p className={status.startsWith('Upload failed') || status.startsWith('Error') ? 'error' : 'success'}>{status}</p>}
        <Link href="/"><button className="secondary">Back</button></Link>
      </div>
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
