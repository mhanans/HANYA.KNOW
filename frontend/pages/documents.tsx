import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Category {
  id: number;
  name: string;
}

interface Doc {
  source: string;
  categoryId: number | null;
  pages: number;
}

export default function Documents() {
  const [docs, setDocs] = useState<Doc[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [filter, setFilter] = useState('');
  const [catFilter, setCatFilter] = useState('');

  const load = async () => {
    try {
      const [d, c] = await Promise.all([
        apiFetch('/api/documents'),
        apiFetch('/api/categories')
      ]);
      if (d.ok) setDocs(await d.json());
      if (c.ok) setCategories(await c.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const save = async (doc: Doc) => {
    await apiFetch('/api/documents', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ source: doc.source, categoryId: doc.categoryId })
    });
    await load();
  };

  const remove = async (source: string) => {
    await apiFetch(`/api/documents?source=${encodeURIComponent(source)}`, { method: 'DELETE' });
    await load();
  };

  const filtered = docs.filter(d =>
    (!filter || d.source.toLowerCase().includes(filter.toLowerCase())) &&
    (!catFilter || String(d.categoryId ?? '') === catFilter)
  );

  return (
    <div className="page-container">
      <h1>All Documents</h1>
      <div className="filters">
        <input className="form-input" placeholder="Search" value={filter} onChange={e => setFilter(e.target.value)} />
        <select className="form-select" value={catFilter} onChange={e => setCatFilter(e.target.value)}>
          <option value="">All categories</option>
          {categories.map(c => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
      </div>
      <div className="card table-wrapper">
        <table className="table">
          <thead>
            <tr>
              <th>Document</th>
              <th>Category</th>
              <th>Pages</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map(d => (
              <tr key={d.source}>
                <td>{d.source}</td>
                <td>
                  <select
                    className="form-select"
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
                </td>
                <td>{d.pages}</td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button className="btn btn-primary" onClick={() => save(d)}>Save</button>
                  <button className="btn btn-danger" onClick={() => remove(d.source)}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
