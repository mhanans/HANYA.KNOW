import { LoginForm } from '../stories/LoginForm';
import { useRouter } from 'next/router';
import { apiFetch } from '../lib/api';
export default function LoginPage() {
  const router = useRouter();

  const handleSubmit = async (data: { username: string; password: string }) => {
    try {
      const res = await apiFetch('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      if (!res.ok) throw new Error(await res.text());
      router.push('/');
    } catch {
      /* ignore */
    }
  };

  return <LoginForm onSubmit={handleSubmit} />;
}
