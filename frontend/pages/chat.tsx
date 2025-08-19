import Link from 'next/link';
import { useState } from 'react';

interface Message {
  role: 'user' | 'assistant';
  content: string;
  sources?: string[];
}

export default function Chat() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [query, setQuery] = useState('');
  const [topK, setTopK] = useState(5);
  const [error, setError] = useState('');

  const submit = async () => {
    if (!query.trim()) return;
    const userMessage: Message = { role: 'user', content: query };
    setMessages(prev => [...prev, userMessage]);
    const currentQuery = query;
    setQuery('');
    setError('');
    try {
      const res = await fetch(`${process.env.NEXT_PUBLIC_API_BASE_URL}/api/chat/query`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: currentQuery, topK })
      });
      if (!res.ok) {
        const msg = await res.text();
        throw new Error(msg);
      }
      const data = await res.json();
      setMessages(prev => [...prev, { role: 'assistant', content: data.answer, sources: data.sources }]);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="chat-container">
      <div className="messages">
        {messages.map((m, i) => (
          <div key={i} className={`msg ${m.role}`}>
            <div className="bubble">{m.content}</div>
            {m.role === 'assistant' && m.sources && m.sources.length > 0 && (
              <ul className="sources">
                {m.sources.map((s, idx) => (
                  <li key={idx}>{s}</li>
                ))}
              </ul>
            )}
          </div>
        ))}
      </div>
      {error && <p className="error">{error}</p>}
      <div className="controls">
        <input
          placeholder="Ask a question"
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={e => {
            if (e.key === 'Enter') submit();
          }}
        />
        <input
          type="number"
          min={1}
          value={topK}
          onChange={e => setTopK(Number(e.target.value))}
          className="topk"
        />
        <button onClick={submit}>Send</button>
      </div>
      <Link href="/">
        <button className="back">Back</button>
      </Link>
      <style jsx>{`
        .chat-container {
          display: flex;
          flex-direction: column;
          height: 100vh;
          padding: 1rem;
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
          padding: 0.5rem 1rem;
          border-radius: 12px;
          white-space: pre-wrap;
        }
        .msg.user .bubble {
          background: #0070f3;
          color: white;
        }
        .msg.assistant .bubble {
          background: #eaeaea;
        }
        .sources {
          font-size: 0.8rem;
          color: #555;
          margin-left: 0.5rem;
        }
        .controls {
          display: flex;
          gap: 0.5rem;
        }
        .controls input[type="text"], .controls input:not(.topk) {
          flex: 1;
          padding: 0.5rem;
        }
        .topk {
          width: 4rem;
          padding: 0.5rem;
        }
        .error {
          color: red;
          margin-bottom: 0.5rem;
        }
        .back {
          margin-top: 0.5rem;
          align-self: flex-start;
        }
      `}</style>
    </div>
  );
}
