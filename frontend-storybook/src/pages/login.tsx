import { LoginForm } from '../stories/LoginForm';
import { useRouter } from 'next/router';
import { apiFetch } from '../lib/api';
import { useState } from 'react';

export default function LoginPage() {
  const router = useRouter();
  const [error, setError] = useState('');

  const handleSubmit = async (data: any) => {
    setError('');
    try {
      const res = await apiFetch('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      if (!res.ok) throw new Error(await res.text());
      router.push('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return <LoginForm onSubmit={handleSubmit} />;
}
