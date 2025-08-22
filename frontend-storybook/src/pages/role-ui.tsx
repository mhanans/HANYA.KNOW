import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import { DataTable } from '../stories/DataTable';

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

  const columns = [
    { header: 'Role', accessor: 'name' },
    {
      header: 'UI Access',
      accessor: 'uiIds',
      render: (r: Role) => (
        <select
          multiple
          value={r.uiIds.map(String)}
          onChange={e => {
            const opts = Array.from(e.target.selectedOptions).map(o => Number(o.value));
            setRoles(prev => prev.map(p => p.id === r.id ? { ...p, uiIds: opts } : p));
          }}
        >
          {uiOptions.map(u => (
            <option key={u.id} value={u.id}>{u.key}</option>
          ))}
        </select>
      )
    },
    {
      header: 'Actions',
      accessor: 'id',
      render: (r: Role) => (
        <button onClick={() => update(r)}>Save</button>
      )
    }
  ];

  return (
    <div className="card">
      <h1>Access Control</h1>
      <DataTable columns={columns} data={roles} />
      {error && <p className="error">{error}</p>}
    </div>
  );
}

