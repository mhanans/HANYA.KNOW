import Link from 'next/link';
import { useState, useEffect } from 'react';

interface Category {
  id: number;
  name: string;
}

export default function Categories() {
  const [categories, setCategories] = useState<Category[]>([]);
  const [name, setName] = useState('');
  const [error, setError] = useState('');

  const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');

  const load = async () => {
    try {
      const res = await fetch(`${base}/api/categories`);
      if (res.ok) setCategories(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const create = async () => {
    setError('');
    try {
      const res = await fetch(`${base}/api/categories`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name })
      });
      if (!res.ok) throw new Error(await res.text());
      setName('');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const update = async (id: number, newName: string) => {
    setError('');
    try {
      const res = await fetch(`${base}/api/categories/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: newName })
      });
      if (!res.ok) throw new Error(await res.text());
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const remove = async (id: number) => {
    setError('');
    try {
      const res = await fetch(`${base}/api/categories/${id}`, { method: 'DELETE' });
      if (!res.ok) {
        let msg = res.statusText;
        try { const data = await res.json(); if (data?.detail) msg = data.detail; } catch {}
        throw new Error(msg);
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="container">
      <div className="card">
        <h1>Categories</h1>
        <div className="new-cat">
          <input value={name} onChange={e => setName(e.target.value)} placeholder="New category" />
          <button onClick={create}>Add</button>
        </div>
        {categories.map(c => (
          <div key={c.id} className="row">
            <input value={c.name} onChange={e => setCategories(prev => prev.map(p => p.id === c.id ? { ...p, name: e.target.value } : p))} />
            <button onClick={() => update(c.id, c.name)}>Save</button>
            <button onClick={() => remove(c.id)}>Delete</button>
          </div>
        ))}
        {error && <p className="error">{error}</p>}
        <Link href="/"><button className="secondary">Back</button></Link>
      </div>
      <style jsx>{`
        .row { display: flex; gap: 0.5rem; margin: 0.25rem 0; }
        .new-cat { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
        .error { color: #e00; }
      `}</style>
    </div>
  );
}
