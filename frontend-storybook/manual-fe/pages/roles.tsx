import { useState, useEffect } from 'react';
import { apiFetch } from '../lib/api';

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
    <div className="card roles-card">
      <h1>Manage Role to Category</h1>
      <div className="new-role">
        <input value={newRole.name} onChange={e => setNewRole({ ...newRole, name: e.target.value })} placeholder="New role" />
        <label>
          <input type="checkbox" checked={newRole.allCategories} onChange={e => setNewRole({ ...newRole, allCategories: e.target.checked })} /> All categories
        </label>
        {!newRole.allCategories && (
          <select multiple value={newRole.categoryIds.map(String)} onChange={e => {
            const opts = Array.from(e.target.selectedOptions).map(o => Number(o.value));
            setNewRole({ ...newRole, categoryIds: opts });
          }}>
            {categories.map(c => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
        )}
        <button onClick={create}>Add</button>
      </div>
      <div className="table-wrapper">
        <table className="role-table">
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
                <td>
                  <input value={r.name} onChange={e => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, name: e.target.value } : p))} />
                </td>
                <td>
                  <input type="checkbox" checked={r.allCategories} onChange={e => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, allCategories: e.target.checked } : p))} />
                </td>
                <td>
                  {!r.allCategories && (
                    <select multiple value={r.categoryIds.map(String)} onChange={e => {
                      const opts = Array.from(e.target.selectedOptions).map(o => Number(o.value));
                      setRoles(prev => prev.map(p => p.id === r.id ? { ...p, categoryIds: opts } : p));
                    }}>
                      {categories.map(c => (
                        <option key={c.id} value={c.id}>{c.name}</option>
                      ))}
                    </select>
                  )}
                </td>
                <td className="actions">
                  <button onClick={() => update(r)}>Save</button>
                  <button onClick={() => remove(r.id)}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {error && <p className="error">{error}</p>}
      <style jsx>{`
        .roles-card { max-width: none; }
        .new-role { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; flex-wrap: wrap; }
        .new-role select { min-width: 10rem; }
        .table-wrapper { overflow-x: auto; }
        .role-table { width: 100%; border-collapse: collapse; }
        .role-table th, .role-table td { padding: 0.5rem; text-align: left; border-top: 1px solid #ddd; }
        .role-table thead { background: #e0e7ff; font-weight: 600; }
        .role-table .actions { display: flex; gap: 0.5rem; }
        @media (max-width: 600px) {
          .new-role { flex-direction: column; }
        }
      `}</style>
    </div>
  );
}
