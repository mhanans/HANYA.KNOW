import { useState, useEffect } from 'react';
import { apiFetch } from '../lib/api';
import TagInput from '../components/TagInput';

interface Role {
  id: number;
  name: string;
  allCategories: boolean;
  categoryIds: number[];
}

interface Category {
  id: number;
  name: string;
}

export default function Roles() {
  const [roles, setRoles] = useState<Role[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [newRole, setNewRole] = useState({ name: '', allCategories: true, categoryIds: [] as number[] });
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const [r, c] = await Promise.all([
        apiFetch('/api/roles'),
        apiFetch('/api/categories')
      ]);
      if (r.ok) setRoles(await r.json());
      if (c.ok) setCategories(await c.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const create = async () => {
    setError('');
    try {
      const res = await apiFetch('/api/roles', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newRole)
      });
      if (!res.ok) throw new Error(await res.text());
      setNewRole({ name: '', allCategories: true, categoryIds: [] });
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const update = async (role: Role) => {
    setError('');
    try {
      const res = await apiFetch(`/api/roles/${role.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(role)
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
      const res = await apiFetch(`/api/roles/${id}`, { method: 'DELETE' });
      if (!res.ok) throw new Error(await res.text());
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="page-container">
      <h1>Manage Role to Category</h1>
      <div className="controls">
        <input className="form-input" value={newRole.name} onChange={e => setNewRole({ ...newRole, name: e.target.value })} placeholder="New role" />
        <label className="form-input" style={{ display: 'flex', alignItems: 'center', gap: '4px', background: 'transparent', border: 'none', padding: 0 }}>
          <input type="checkbox" checked={newRole.allCategories} onChange={e => setNewRole({ ...newRole, allCategories: e.target.checked })} /> All categories
        </label>
        {!newRole.allCategories && (
          <TagInput options={categories} selected={newRole.categoryIds} onChange={ids => setNewRole({ ...newRole, categoryIds: ids })} />
        )}
        <button onClick={create} className="btn btn-primary">Add</button>
      </div>
      <div className="card table-wrapper">
        <table className="table">
          <thead>
            <tr>
              <th>Name</th>
              <th>All</th>
              <th>Categories</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {roles.map(r => (
              <tr key={r.id}>
                <td><input className="form-input" value={r.name} onChange={e => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, name: e.target.value } : p))} /></td>
                <td><input type="checkbox" checked={r.allCategories} onChange={e => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, allCategories: e.target.checked } : p))} /></td>
                <td>
                  {!r.allCategories && (
                    <TagInput options={categories} selected={r.categoryIds} onChange={ids => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, categoryIds: ids } : p))} />
                  )}
                </td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button className="btn btn-primary" onClick={() => update(r)}>Save</button>
                  <button className="btn btn-danger" onClick={() => remove(r.id)}>Delete</button>
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
