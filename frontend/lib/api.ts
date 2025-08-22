export async function apiFetch(path: string, options: RequestInit = {}) {
  const base = process.env.NEXT_PUBLIC_API_BASE_URL?.replace(/\/$/, '');
  const apiKey = process.env.NEXT_PUBLIC_API_KEY;
  if (!base || !apiKey) {
    throw new Error('NEXT_PUBLIC_API_BASE_URL and NEXT_PUBLIC_API_KEY must be set');
  }
  const headers: Record<string, string> = {
    ...((options.headers as Record<string, string>) || {}),
    'X-API-KEY': apiKey,
  };
  if (typeof window !== 'undefined') {
    const token = localStorage.getItem('token');
    if (token) headers['Authorization'] = `Bearer ${token}`;
  }
  return fetch(`${base}${path}`, { ...options, headers });
}
