import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Role { id: number; name: string; }
interface User { id: number; username: string; roleIds: number[]; password?: string; }

export default function Users() {
  const [users, setUsers] = useState<User[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [newUser, setNewUser] = useState({ username: '', password: '', roleIds: [] as number[] });
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const [u, r] = await Promise.all([
        apiFetch('/api/users'),
        apiFetch('/api/roles')
      ]);
      if (u.ok) setUsers(await u.json());
      if (r.ok) {
        const roleData: Role[] = await r.json();
        setRoles(roleData.map((x) => ({ id: x.id, name: x.name })));
      }
    } catch {}
  };

  useEffect(() => { load(); }, []);

  const create = async () => {
    setError('');
    try {
      const res = await apiFetch('/api/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newUser)
      });
      if (!res.ok) throw new Error(await res.text());
      setNewUser({ username: '', password: '', roleIds: [] });
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const update = async (user: User & { password?: string }) => {
    setError('');
    try {
      const res = await apiFetch(`/api/users/${user.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(user)
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
      const res = await apiFetch(`/api/users/${id}`, { method: 'DELETE' });
      if (!res.ok) throw new Error(await res.text());
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="card">
      <h1>Manage Users</h1>
      <div className="new-user">
        <input value={newUser.username} onChange={e => setNewUser({ ...newUser, username: e.target.value })} placeholder="Username" />
        <input type="password" value={newUser.password} onChange={e => setNewUser({ ...newUser, password: e.target.value })} placeholder="Password" />
        <select multiple value={newUser.roleIds.map(String)} onChange={e => {
          const opts = Array.from(e.target.selectedOptions).map(o => Number(o.value));
          setNewUser({ ...newUser, roleIds: opts });
        }}>
          {roles.map(r => (
            <option key={r.id} value={r.id}>{r.name}</option>
          ))}
        </select>
        <button onClick={create}>Add</button>
      </div>
      <div className="table-wrapper">
        <table className="role-table">
          <thead>
            <tr>
              <th>Username</th>
              <th>Password</th>
              <th>Roles</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {users.map(u => (
              <tr key={u.id}>
                <td><input value={u.username} onChange={e => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, username: e.target.value } : p))} /></td>
                <td><input type="password" value={u.password || ''} onChange={e => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, password: e.target.value } : p))} /></td>
                <td>
                  <select multiple value={u.roleIds.map(String)} onChange={e => {
                    const opts = Array.from(e.target.selectedOptions).map(o => Number(o.value));
                    setUsers(prev => prev.map(p => p.id === u.id ? { ...p, roleIds: opts } : p));
                  }}>
                    {roles.map(r => (
                      <option key={r.id} value={r.id}>{r.name}</option>
                    ))}
                  </select>
                </td>
                <td className="actions">
                  <button onClick={() => update(u)}>Save</button>
                  <button onClick={() => remove(u.id)}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {error && <p className="error">{error}</p>}
      <style jsx>{`
        .new-user { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; flex-wrap: wrap; }
        .new-user select { min-width: 10rem; }
        .table-wrapper { overflow-x: auto; }
        .role-table { width: 100%; border-collapse: collapse; }
        .role-table th, .role-table td { padding: 0.5rem; text-align: left; border-top: 1px solid #ddd; }
        .role-table thead { background: #e0e7ff; font-weight: 600; }
        .role-table .actions { display: flex; gap: 0.5rem; }
      `}</style>
    </div>
  );
}
