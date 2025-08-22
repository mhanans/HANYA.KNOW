import type { NextApiRequest, NextApiResponse } from 'next';

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
  let body: any = undefined;
  if (req.method && req.method !== 'GET' && req.method !== 'HEAD') {
    body = typeof req.body === 'string' ? req.body : JSON.stringify(req.body);
    if (req.headers['content-type']) headers['Content-Type'] = req.headers['content-type'] as string;
  }
  const response = await fetch(url.toString(), { method: req.method, headers, body });
  res.status(response.status);
  response.headers.forEach((value, key) => {
    if (key.toLowerCase() === 'transfer-encoding') return;
    res.setHeader(key, value);
  });
  const buf = Buffer.from(await response.arrayBuffer());
  res.send(buf);
}
