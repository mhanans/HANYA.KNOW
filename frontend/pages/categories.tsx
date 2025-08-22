import { useState, useEffect } from 'react';
import { apiFetch } from '../lib/api';

interface Category {
  id: number;
  name: string;
}

export default function Categories() {
  const [categories, setCategories] = useState<Category[]>([]);
  const [name, setName] = useState('');
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const res = await apiFetch('/api/categories');
      if (res.ok) setCategories(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const create = async () => {
    setError('');
    try {
      const res = await apiFetch('/api/categories', {
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
      const res = await apiFetch(`/api/categories/${id}`, {
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
      const res = await apiFetch(`/api/categories/${id}`, { method: 'DELETE' });
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
    <div className="page-container">
      <h1>Manage Categories</h1>
      <div className="controls">
        <input value={name} onChange={e => setName(e.target.value)} placeholder="New category" className="form-input" />
        <button onClick={create} className="btn btn-primary">Add</button>
      </div>
      <div className="card table-wrapper">
        <table className="table">
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
                  <input
                    value={c.name}
                    className="form-input"
                    onChange={e =>
                      setCategories(prev =>
                        prev.map(p => (p.id === c.id ? { ...p, name: e.target.value } : p))
                      )
                    }
                  />
                </td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button onClick={() => update(c.id, c.name)} className="btn btn-primary">Save</button>
                  <button onClick={() => remove(c.id)} className="btn btn-danger">Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {error && <p className="error">{error}</p>}
      </div>
    </div>
  );
}
