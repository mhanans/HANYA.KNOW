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
    <div className="card categories-card">
      <h1>Manage Categories</h1>
      <div className="new-cat">
        <input value={name} onChange={e => setName(e.target.value)} placeholder="New category" />
        <button onClick={create}>Add</button>
      </div>
      <div className="cat-grid head">
        <div>Name</div>
        <div>Actions</div>
      </div>
      {categories.map(c => (
        <div className="cat-grid row" key={c.id}>
          <input
            value={c.name}
            onChange={e =>
              setCategories(prev =>
                prev.map(p => (p.id === c.id ? { ...p, name: e.target.value } : p))
              )
            }
          />
          <div className="actions">
            <button onClick={() => update(c.id, c.name)}>Save</button>
            <button onClick={() => remove(c.id)}>Delete</button>
          </div>
        </div>
      ))}
      {error && <p className="error">{error}</p>}
      <style jsx>{`
        .categories-card { max-width: none; }
        .new-cat { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
        .cat-grid { display: grid; grid-template-columns: 1fr auto; align-items: center; gap: 0.5rem; }
        .head { background: #e0e7ff; font-weight: 600; padding: 0.5rem; }
        .row { border-top: 1px solid #ddd; padding: 0.5rem 0; }
        .row .actions button { margin-right: 0.5rem; }
        .error { color: #e00; margin-top: 0.5rem; }
      `}</style>
    </div>
  );
}
