import type { NextApiRequest, NextApiResponse } from 'next';

// Proxy the backend's Server-Sent Events stream so the browser can
// connect without exposing the API key. We avoid `Readable.fromWeb`
// because it buffers chunks and breaks live streaming; instead we
// manually read and forward each chunk as it arrives.

export const config = { api: { bodyParser: false } };

export default async function handler(req: NextApiRequest, res: NextApiResponse) {
  const base = (process.env.API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
  const apiKey =
    (req.headers['x-api-key'] as string | undefined) ||
    process.env.API_KEY ||
    process.env.NEXT_PUBLIC_API_KEY ||
    'dummy-api-key';

  const params = new URLSearchParams();
  Object.entries(req.query).forEach(([k, v]) => {
    if (Array.isArray(v)) v.forEach(val => params.append(k, val));
    else params.append(k, v as string);
  });

  const auth = req.headers.authorization || (req.cookies.token ? `Bearer ${req.cookies.token}` : undefined);
  const controller = new AbortController();
  req.on('close', () => controller.abort());

  const upstream = await fetch(`${base}/api/chat/stream?${params.toString()}`, {
    headers: {
      'X-API-KEY': apiKey,
      ...(auth ? { Authorization: auth } : {}),
    },
    signal: controller.signal,
  });

  res.status(upstream.status);
  upstream.headers.forEach((value, key) => res.setHeader(key, value));
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.setHeader('Content-Type', 'text/event-stream');

  if (!upstream.body) {
    res.end();
    return;
  }

  const reader = upstream.body.getReader();
  const decoder = new TextDecoder();

  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      res.write(decoder.decode(value));
    }
  } catch {
    // ignore errors from aborted requests
  } finally {
    res.end();
  }
}
