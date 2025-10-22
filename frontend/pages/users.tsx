import { useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import SaveIcon from '@mui/icons-material/Save';
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
    <Box sx={{ maxWidth: 1200, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Typography variant="h1">Manage Users</Typography>
      <Card>
        <CardContent>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems="flex-start">
            <TextField
              value={newUser.username}
              onChange={e => setNewUser({ ...newUser, username: e.target.value })}
              placeholder="Username"
              label="Username"
              fullWidth
            />
            <TextField
              type="password"
              value={newUser.password}
              onChange={e => setNewUser({ ...newUser, password: e.target.value })}
              placeholder="Password"
              label="Password"
              fullWidth
            />
            <Box sx={{ flexGrow: 1, minWidth: 220 }}>
              <TagInput options={roles} selected={newUser.roleIds} onChange={ids => setNewUser({ ...newUser, roleIds: ids })} />
            </Box>
            <Button variant="contained" color="primary" startIcon={<AddIcon />} onClick={create} sx={{ whiteSpace: 'nowrap' }}>
              Add
            </Button>
          </Stack>
        </CardContent>
      </Card>
      <Card>
        <CardContent>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>Username</TableCell>
                <TableCell>Password</TableCell>
                <TableCell>Roles</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {users.map(u => (
                <TableRow key={u.id} hover>
                  <TableCell sx={{ minWidth: 200 }}>
                    <TextField
                      value={u.username}
                      onChange={e =>
                        setUsers(prev => prev.map(p => (p.id === u.id ? { ...p, username: e.target.value } : p)))
                      }
                      fullWidth
                    />
                  </TableCell>
                  <TableCell sx={{ minWidth: 200 }}>
                    <TextField
                      type="password"
                      value={u.password || ''}
                      onChange={e =>
                        setUsers(prev => prev.map(p => (p.id === u.id ? { ...p, password: e.target.value } : p)))
                      }
                      fullWidth
                    />
                  </TableCell>
                  <TableCell sx={{ minWidth: 260 }}>
                    <TagInput
                      options={roles}
                      selected={u.roleIds}
                      onChange={ids =>
                        setUsers(prev => prev.map(p => (p.id === u.id ? { ...p, roleIds: ids } : p)))
                      }
                    />
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                      <Button variant="contained" startIcon={<SaveIcon />} onClick={() => update(u)}>
                        Save
                      </Button>
                      <Button variant="outlined" color="error" startIcon={<DeleteIcon />} onClick={() => remove(u.id)}>
                        Delete
                      </Button>
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {error && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {error}
            </Alert>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}
