import Link from 'next/link';
import { useState } from 'react';

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
  lowConfidence?: boolean;
}

export default function Chat() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [query, setQuery] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [lastQuery, setLastQuery] = useState('');

  const send = async (text: string, addUser: boolean) => {
    if (!text.trim()) {
      setError('Please enter a question.');
      return;
    }
    if (addUser) {
      const userMessage: Message = { role: 'user', content: text };
      setMessages(prev => [...prev, userMessage]);
    }
    setLastQuery(text);
    setError('');
    setLoading(true);
    try {
      const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
      const res = await fetch(`${base}/api/chat/query`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: text })
      });
      if (!res.ok) {
        let msg = res.statusText;
        try {
          const data = await res.json();
          if (data?.detail) msg = data.detail;
        } catch {
          try {
            msg = await res.text();
          } catch {
            /* ignore */
          }
        }
        throw new Error(`Request failed: ${msg}`);
      }
      const data = await res.json();
      setMessages(prev => [...prev, { role: 'assistant', content: data.answer, sources: data.sources, lowConfidence: data.lowConfidence }]);
      setLastQuery('');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  };

  const submit = () => {
    const currentQuery = query;
    setQuery('');
    send(currentQuery, true);
  };

  const retry = () => send(lastQuery, false);

  return (
    <div className="container">
      <div className="card chat-card">
        <div className="messages">
          {messages.map((m, i) => (
            <div key={i} className={`msg ${m.role}`}>
              <div className="bubble">{m.content}</div>
              {m.role === 'assistant' && m.lowConfidence && (
                <p className="warn">Low relevance of retrieved articles.</p>
              )}
          {m.role === 'assistant' && m.sources && m.sources.length > 0 && (
                <ul className="sources">
                  {m.sources.map((s) => (
                    <li key={s.index}>
                      <strong>
                        [{s.index}] {s.file}
                        {s.page !== undefined && ` (p.${s.page})`}
                      </strong>
                      <span className="score">relevance {(s.score * 100).toFixed(1)}%</span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ))}
        </div>
        {error && (
          <p className="error">
            {error}{' '}
            {lastQuery && <button onClick={retry} disabled={loading}>Retry</button>}
          </p>
        )}
        <div className="controls">
          <input
            placeholder="Ask a question"
            value={query}
            onChange={e => setQuery(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') submit();
            }}
          />
          <button onClick={submit} disabled={loading}>{loading ? 'Sending...' : 'Send'}</button>
        </div>
        <Link href="/">
          <button className="secondary back">Back</button>
        </Link>
      </div>
      <style jsx>{`
        .chat-card {
          display: flex;
          flex-direction: column;
          height: 100%;
        }
        .messages {
          flex: 1;
          overflow-y: auto;
          margin-bottom: 1rem;
        }
        .msg {
          display: flex;
          margin: 0.5rem 0;
        }
        .msg.user {
          justify-content: flex-end;
        }
        .msg.assistant {
          justify-content: flex-start;
        }
        .bubble {
          max-width: 70%;
          padding: 0.75rem 1rem;
          border-radius: 8px;
          white-space: pre-wrap;
        }
        .msg.user .bubble {
          background: #0070f3;
          color: #fff;
        }
        .msg.assistant .bubble {
          background: #f1f1f1;
        }
        .sources {
          font-size: 0.8rem;
          color: #555;
          margin-left: 0.5rem;
        }
        .warn {
          color: #b36b00;
          font-size: 0.8rem;
          margin-left: 0.5rem;
        }
        .controls {
          display: flex;
          gap: 0.5rem;
        }
        .controls input {
          flex: 1;
        }
        .score {
          margin-left: 0.25rem;
          color: #555;
        }
        .error {
          color: #e00;
          margin-bottom: 0.5rem;
        }
        .error button {
          margin-left: 0.5rem;
        }
        .back {
          margin-top: 0.5rem;
          align-self: flex-start;
        }
      `}</style>
    </div>
  );
}
