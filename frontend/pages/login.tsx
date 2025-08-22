import { useState } from 'react';
import { useRouter } from 'next/router';
import { apiFetch } from '../lib/api';

export default function Login() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const router = useRouter();

  const submit = async () => {
    setError('');
    try {
      const res = await apiFetch('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      if (typeof window !== 'undefined') {
        localStorage.setItem('token', data.token);
      }
      router.push('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="page-container">
      <div className="card" style={{ maxWidth: '400px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <h1>Login</h1>
        <input value={username} onChange={e => setUsername(e.target.value)} placeholder="Username" className="form-input" />
        <input type="password" value={password} onChange={e => setPassword(e.target.value)} placeholder="Password" className="form-input" />
        <button onClick={submit} className="btn btn-primary">Login</button>
        {error && <p className="error">{error}</p>}
      </div>
    </div>
  );
}
