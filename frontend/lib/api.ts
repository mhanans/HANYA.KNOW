export async function apiFetch(path: string, options: RequestInit = {}) {
  const base = process.env.NEXT_PUBLIC_API_BASE_URL?.replace(/\/$/, '');
  const apiKey = process.env.NEXT_PUBLIC_API_KEY;
  if (!base || !apiKey) {
    throw new Error('NEXT_PUBLIC_API_BASE_URL and NEXT_PUBLIC_API_KEY must be set');
  }
  const headers: HeadersInit = {
    ...(options.headers || {}),
    'X-API-KEY': apiKey,
  };
  return fetch(`${base}${path}`, { ...options, headers, credentials: 'include' });
}
