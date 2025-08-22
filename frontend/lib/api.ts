export async function apiFetch(path: string, options: RequestInit = {}) {
  const cleaned = path.startsWith('/api') ? path.slice(4) : path;
  const headers: Record<string, string> = {
    ...((options.headers as Record<string, string>) || {}),
  };
  if (typeof window !== 'undefined') {
    const token = localStorage.getItem('token');
    if (token) headers['Authorization'] = `Bearer ${token}`;
  }
  return fetch(`/api/proxy${cleaned}`, { ...options, headers });
}
