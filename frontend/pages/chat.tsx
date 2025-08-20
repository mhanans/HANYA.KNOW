import { useState, useEffect, useRef } from 'react';

interface Source {
  index: number;
  file: string;
  page?: number;
  content: string;
  score: number;
}

interface Message {
  role: 'user' | 'assistant';
  content: string;
  sources?: Source[];
}

interface Category {
  id: number;
  name: string;
}

export default function Chat() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selected, setSelected] = useState<number[]>([]);
  const [error, setError] = useState('');
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const load = async () => {
      const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
      try {
        const res = await fetch(`${base}/api/categories`);
        if (res.ok) setCategories(await res.json());
      } catch {
        /* ignore */
      }
    };
    load();
  }, []);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const send = async () => {
    const text = query.trim();
    if (!text || loading) return;
    setQuery('');
    setError('');
    const user: Message = { role: 'user', content: text };
    const assistant: Message = { role: 'assistant', content: '' };
    const assistantIndex = messages.length + 1;
    setMessages(prev => [...prev, user, assistant]);
    setLoading(true);
    try {
      const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
      const res = await fetch(`${base}/api/chat/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: text, categoryIds: selected })
      });
      if (!res.ok || !res.body) {
        const msg = await res.text();
        throw new Error(msg || 'Request failed');
      }
      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        const parts = buffer.split('\n\n');
        buffer = parts.pop() || '';
        for (const part of parts) {
          const lines = part.split(/\r?\n/);
          let event = '';
          let data = '';
          for (const line of lines) {
            if (line.startsWith('event:')) event = line.slice(6).trim();
            else if (line.startsWith('data:')) data += line.slice(5).trim();
          }
          if (event === 'token') {
            const token = JSON.parse(data);
            setMessages(prev => {
              const ms = [...prev];
              ms[assistantIndex].content += token;
              return ms;
            });
          } else if (event === 'sources') {
            const src = JSON.parse(data) as Source[];
            setMessages(prev => {
              const ms = [...prev];
              ms[assistantIndex].sources = src;
              return ms;
            });
          } else if (event === 'error') {
            setError(data);
          }
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="chat-page">
      <div className="messages">
        {messages.map((m, i) => (
          <div key={i} className={`message ${m.role}`}>
            <div className="avatar" />
            <div className="bubble">
              {m.content}
              {m.role === 'assistant' && m.sources && (
                <ul className="sources">
                  {m.sources.map(s => (
                    <li key={s.index}>
                      [{s.index}] {s.file}
                      {s.page !== undefined && ` (p.${s.page})`}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        ))}
        <div ref={endRef} />
      </div>
      <div className="input" onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }}}>
        {error && <p className="error">{error}</p>}
        <textarea
          placeholder="Send a message..."
          value={query}
          onChange={e => setQuery(e.target.value)}
          disabled={loading}
        />
        <div className="actions">
          <select multiple value={selected.map(String)} onChange={e => {
            const opts = Array.from(e.target.selectedOptions).map(o => parseInt(o.value));
            setSelected(opts);
          }}>
            {categories.map(c => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
          <button onClick={send} disabled={loading || !query.trim()}>Send</button>
        </div>
      </div>
      <style jsx>{`
        .chat-page {
          height: 100%;
          display: flex;
          flex-direction: column;
          background: #f7f7f8;
        }
        .messages {
          flex: 1;
          overflow-y: auto;
          padding: 1rem;
        }
        .message {
          display: flex;
          padding: 1rem;
          border-bottom: 1px solid #e5e5e5;
        }
        .message.user {
          background: #fff;
        }
        .message.assistant {
          background: #f7f7f8;
        }
        .avatar {
          width: 32px;
          height: 32px;
          border-radius: 4px;
          background: #ccc;
          margin-right: 1rem;
          flex-shrink: 0;
        }
        .bubble {
          white-space: pre-wrap;
          flex: 1;
        }
        .input {
          border-top: 1px solid #e5e5e5;
          padding: 1rem;
          background: #f7f7f8;
        }
        .input textarea {
          width: 100%;
          border-radius: 8px;
          padding: 0.75rem;
          resize: none;
          height: 80px;
        }
        .actions {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-top: 0.5rem;
          gap: 0.5rem;
        }
        .actions select {
          min-width: 160px;
        }
        .actions button {
          padding: 0.5rem 1rem;
        }
        .sources {
          font-size: 0.8rem;
          margin-top: 0.5rem;
          color: #555;
        }
        .error {
          color: #c00;
          margin-bottom: 0.5rem;
        }
      `}</style>
    </div>
  );
}
