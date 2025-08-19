import { useState } from 'react';

export default function Ingest() {
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [text, setText] = useState('');
  const [status, setStatus] = useState('');

  const submit = async () => {
    const form = new FormData();
    if (file) form.append('file', file);
    if (title) form.append('title', title);
    if (text) form.append('text', text);
    const res = await fetch(`${process.env.NEXT_PUBLIC_API_BASE_URL}/api/ingest`, { method: 'POST', body: form });
    setStatus(res.ok ? 'uploaded' : 'error');
  };

  return (
    <div>
      <h1>Ingest</h1>
      <input type="file" onChange={e => setFile(e.target.files?.[0] ?? null)} />
      <input placeholder="title" value={title} onChange={e => setTitle(e.target.value)} />
      <textarea placeholder="text" value={text} onChange={e => setText(e.target.value)} />
      <button onClick={submit}>Upload</button>
      <p>{status}</p>
    </div>
  );
}
