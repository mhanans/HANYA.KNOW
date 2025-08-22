import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Role { id: number; name: string; uiIds: number[]; }
interface UiPage { id: number; key: string; }

export default function RoleUi() {
  const [roles, setRoles] = useState<Role[]>([]);
  const [uiOptions, setUiOptions] = useState<UiPage[]>([]);
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const [r, u] = await Promise.all([
        apiFetch('/api/roles'),
        apiFetch('/api/ui')
      ]);
      if (r.ok) setRoles(await r.json());
      if (u.ok) setUiOptions(await u.json());
    } catch {}
  };

  useEffect(() => { load(); }, []);

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

  return (
    <div className="card">
      <h1>Manage Role to UI</h1>
      <div className="table-wrapper">
        <table className="role-table">
          <thead>
            <tr>
              <th>Role</th>
              <th>UI Access</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {roles.map(r => (
              <tr key={r.id}>
                <td>{r.name}</td>
                <td>
                  <select multiple value={r.uiIds.map(String)} onChange={e => {
                    const opts = Array.from(e.target.selectedOptions).map(o => Number(o.value));
                    setRoles(prev => prev.map(p => p.id === r.id ? { ...p, uiIds: opts } : p));
                  }}>
                    {uiOptions.map(u => (
                      <option key={u.id} value={u.id}>{u.key}</option>
                    ))}
                  </select>
                </td>
                <td className="actions"><button onClick={() => update(r)}>Save</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {error && <p className="error">{error}</p>}
      <style jsx>{`
        .table-wrapper { overflow-x: auto; }
        .role-table { width: 100%; border-collapse: collapse; }
        .role-table th, .role-table td { padding: 0.5rem; text-align: left; border-top: 1px solid #ddd; }
        .role-table thead { background: #e0e7ff; font-weight: 600; }
        .role-table .actions { display: flex; gap: 0.5rem; }
      `}</style>
    </div>
  );
}
