import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import TagInput from '../components/TagInput';

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
    <div className="page-container">
      <h1>Access Control</h1>
      <div className="card table-wrapper">
        <table className="table">
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
                  <TagInput options={uiOptions.map(u => ({ id: u.id, name: u.key }))} selected={r.uiIds} onChange={ids => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, uiIds: ids } : p))} />
                </td>
                <td style={{ display: 'flex', gap: '8px' }}><button className="btn btn-primary" onClick={() => update(r)}>Save</button></td>
              </tr>
            ))}
          </tbody>
        </table>
        {error && <p className="error">{error}</p>}
      </div>
    </div>
  );
}
