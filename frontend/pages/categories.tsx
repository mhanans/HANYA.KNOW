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
      <h1>Categories</h1>
      <div className="new-cat">
        <input value={name} onChange={e => setName(e.target.value)} placeholder="New category" />
        <button onClick={create}>Add</button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {categories.map(c => (
            <tr key={c.id}>
              <td>
                <input value={c.name} onChange={e => setCategories(prev => prev.map(p => p.id === c.id ? { ...p, name: e.target.value } : p))} />
              </td>
              <td>
                <button onClick={() => update(c.id, c.name)}>Save</button>
                <button onClick={() => remove(c.id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {error && <p className="error">{error}</p>}
      <style jsx>{`
        .categories-card { max-width: none; }
        .new-cat { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
        table { width: 100%; border-collapse: collapse; }
        th, td { border: 1px solid #ddd; padding: 0.5rem; }
        th { background: #e0e7ff; text-align: left; }
        td button { margin-right: 0.5rem; }
        .error { color: #e00; margin-top: 0.5rem; }
      `}</style>
    </div>
  );
}
