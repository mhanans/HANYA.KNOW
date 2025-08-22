import type { NextApiRequest, NextApiResponse } from 'next';
import { Readable } from 'stream';

export const config = { api: { bodyParser: false } };

export default async function handler(req: NextApiRequest, res: NextApiResponse) {
  const base = (process.env.API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
  const apiKey =
    (req.headers['x-api-key'] as string | undefined) ||
    (req.query.apiKey as string | undefined) ||
    process.env.API_KEY ||
    process.env.NEXT_PUBLIC_API_KEY ||
    'dummy-api-key';
  const params = new URLSearchParams();
  Object.entries(req.query).forEach(([k, v]) => {
    if (Array.isArray(v)) v.forEach(val => params.append(k, val));
    else params.append(k, v as string);
  });
  const auth = req.headers.authorization || (req.cookies.token ? `Bearer ${req.cookies.token}` : undefined);
  const response = await fetch(`${base}/api/chat/stream?${params.toString()}`, {
    headers: {
      'X-API-KEY': apiKey,
      ...(auth ? { Authorization: auth } : {}),
    },
  });
  res.status(response.status);
  response.headers.forEach((value, key) => res.setHeader(key, value));
  if (response.body) {
    const stream = Readable.fromWeb(response.body as any);
    stream.pipe(res);
  } else {
    res.end();
  }
}
