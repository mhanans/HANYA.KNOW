import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import TagInput from '../components/TagInput';

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
      if (r.ok) setRoles((await r.json()).map((x: any) => ({ id: x.id, name: x.name })));
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
    <div className="page-container">
      <h1>Manage Users</h1>
      <div className="controls">
        <input value={newUser.username} onChange={e => setNewUser({ ...newUser, username: e.target.value })} placeholder="Username" className="form-input" />
        <input type="password" value={newUser.password} onChange={e => setNewUser({ ...newUser, password: e.target.value })} placeholder="Password" className="form-input" />
        <TagInput options={roles} selected={newUser.roleIds} onChange={ids => setNewUser({ ...newUser, roleIds: ids })} />
        <button onClick={create} className="btn btn-primary">Add</button>
      </div>
      <div className="card table-wrapper">
        <table className="table">
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
                <td><input value={u.username} onChange={e => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, username: e.target.value } : p))} className="form-input" /></td>
                <td><input type="password" value={u.password || ''} onChange={e => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, password: e.target.value } : p))} className="form-input" /></td>
                <td>
                  <TagInput options={roles} selected={u.roleIds} onChange={ids => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, roleIds: ids } : p))} />
                </td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button onClick={() => update(u)} className="btn btn-primary">Save</button>
                  <button onClick={() => remove(u.id)} className="btn btn-danger">Delete</button>
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
