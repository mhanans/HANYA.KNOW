import Link from 'next/link';
import { useState } from 'react';

export default function Ingest() {
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [status, setStatus] = useState('');

  const submit = async () => {
    setStatus('');
    const form = new FormData();
    if (file) form.append('file', file);
    if (title) form.append('title', title);
    if (text) form.append('text', text);
    try {
      const res = await fetch(`${process.env.NEXT_PUBLIC_API_BASE_URL}/api/ingest`, {
        method: 'POST',
        body: form
      });
      if (res.ok) {
        setStatus('Upload successful');
      } else {
        const msg = await res.text();
        setStatus(`Error: ${msg}`);
      }
    } catch (err) {
      setStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  };

  return (
    <div className="container">
      <h1>Upload Document</h1>
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
      {status && <p>{status}</p>}
      <Link href="/"><button>Back</button></Link>
      <style jsx>{`
        .container {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 0.5rem;
          padding: 2rem;
        }
        input, textarea {
          width: 100%;
          max-width: 400px;
          padding: 0.5rem;
        }
        textarea {
          min-height: 100px;
        }
        button {
          padding: 0.5rem 1rem;
        }
      `}</style>
    </div>
  );
}
