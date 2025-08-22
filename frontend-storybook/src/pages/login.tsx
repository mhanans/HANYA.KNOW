import { LoginPage } from '../stories/LoginPage';
import { useRouter } from 'next/router';
import { apiFetch } from '../lib/api';
export default function LoginPageWrapper() {
  const router = useRouter();

  const handleSubmit = async (data: { username: string; password: string }) => {
    try {
      const res = await apiFetch('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      if (!res.ok) throw new Error(await res.text());
      const result = await res.json();
      if (typeof window !== 'undefined') {
        localStorage.setItem('token', result.token);
      }
      router.push('/');
    } catch {
      /* ignore */
    }
  };

  return <LoginPage onSubmit={handleSubmit} />;
}
