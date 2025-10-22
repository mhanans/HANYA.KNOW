import type { NextApiRequest, NextApiResponse } from 'next';

export const config = {
  api: {
    bodyParser: false,
  },
};

export default async function handler(req: NextApiRequest, res: NextApiResponse) {
  const base = (process.env.API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
  const apiKey =
    (req.headers['x-api-key'] as string | undefined) ||
    (req.query.apiKey as string | undefined) ||
    process.env.API_KEY ||
    process.env.NEXT_PUBLIC_API_KEY ||
    'dummy-api-key';
  const segments = req.query.path;
  const target = Array.isArray(segments) ? segments.join('/') : segments || '';
  const url = new URL(`${base}/api/${target}`);
  Object.entries(req.query).forEach(([k, v]) => {
    if (k === 'path') return;
    if (Array.isArray(v)) v.forEach(val => url.searchParams.append(k, val));
    else url.searchParams.append(k, v as string);
  });
  const headers: Record<string, string> = { 'X-API-KEY': apiKey };
  const auth = req.headers.authorization || (req.cookies.token ? `Bearer ${req.cookies.token}` : undefined);
  if (auth) headers['Authorization'] = auth;
  if (req.headers.cookie) {
    headers['Cookie'] = req.headers.cookie;
  }

  const init: RequestInit & { duplex?: 'half' } = { method: req.method, headers };
  if (req.method && req.method !== 'GET' && req.method !== 'HEAD') {
    if (req.headers['content-type']) headers['Content-Type'] = req.headers['content-type'] as string;
    init.duplex = 'half';
    init.body = req as any;
  }

  const response = await fetch(url.toString(), init);
  res.status(response.status);
  response.headers.forEach((value, key) => {
    if (key.toLowerCase() === 'transfer-encoding') return;
    res.setHeader(key, value);
  });
  const buf = Buffer.from(await response.arrayBuffer());
  res.send(buf);
}
