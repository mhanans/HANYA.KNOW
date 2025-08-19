import Link from 'next/link';
import { useState } from 'react';

export default function Ingest() {
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [status, setStatus] = useState('');

  const submit = async () => {
    setStatus('');
    if (!file && !text.trim()) {
      setStatus('Please provide a PDF file or some text to upload.');
      return;
    }
    const form = new FormData();
    if (file) form.append('file', file);
    if (title) form.append('title', title);
    if (text) form.append('text', text);
    try {
      const base = (process.env.NEXT_PUBLIC_API_BASE_URL || '').replace(/\/$/, '');
      const res = await fetch(`${base}/api/ingest`, {
        method: 'POST',
        body: form
      });
      if (!res.ok) {
        let msg = res.statusText;
        try {
          const text = await res.text();
          if (!text.startsWith('<')) msg = text || msg;
        } catch {
          /* ignore */
        }
        setStatus(`Upload failed: ${msg} (${res.status}). Ensure the API server is reachable.`);
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
        <p className="hint">Provide a PDF or paste text below to add it to the knowledge base.</p>
        <input type="file" onChange={e => setFile(e.target.files?.[0] ?? null)} />
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
        {status && <p className={status.startsWith('Error') ? 'error' : 'success'}>{status}</p>}
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
