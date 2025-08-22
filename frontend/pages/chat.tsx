import { useState, useEffect, useRef } from 'react';
import { apiFetch } from '../lib/api';

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

const SendIcon = ({ disabled }: { disabled: boolean }) => (
  <svg
    width="24"
    height="24"
    viewBox="0 0 24 24"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    className={`send-icon ${disabled ? 'disabled' : ''}`}
  >
    <path d="M12 2L2 22L12 18L22 22L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" />
  </svg>
);

export default function Chat() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selected, setSelected] = useState<number[]>([]);
  const [error, setError] = useState('');
  const [conversationId, setConversationId] = useState<string | null>(null);
  const endRef = useRef<HTMLDivElement>(null);
  const userInitials = 'AD';

  useEffect(() => {
    const load = async () => {
      try {
        const res = await apiFetch('/api/categories');
        if (res.ok) setCategories(await res.json());
      } catch {
        /* ignore */
      }
    };
    load();
    const savedId = localStorage.getItem('conversationId');
    const savedMessages = localStorage.getItem('conversationMessages');
    if (savedId) setConversationId(savedId);
    if (savedMessages) setMessages(JSON.parse(savedMessages));
  }, []);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
    localStorage.setItem('conversationMessages', JSON.stringify(messages));
  }, [messages]);

  useEffect(() => {
    if (conversationId) localStorage.setItem('conversationId', conversationId);
  }, [conversationId]);

  const toggleCategory = (id: number) => {
    setSelected(prev => (prev.includes(id) ? prev.filter(i => i !== id) : [...prev, id]));
  };

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
      const res = await apiFetch('/api/chat/stream', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: text, categoryIds: selected, conversationId })
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
          } else if (event === 'id') {
            setConversationId(data);
          }
        }
      }
    } catch (err) {
      setMessages(prev => {
        const ms = [...prev];
        ms[assistantIndex].content = err instanceof Error ? err.message : String(err);
        return ms;
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="chat-interface">
      <div className="messages-container">
        {messages.map((m, i) => (
          <div key={i} className={`chat-message ${m.role === 'assistant' ? 'assistant' : 'user'}`}>
            <div className="avatar">
              {m.role === 'assistant' ? '🤖' : userInitials}
            </div>
            <div className="bubble">
              {m.content || (loading && m.role === 'assistant' && i === messages.length - 1 ? (
                <div className="typing-indicator"><span /><span /><span /></div>
              ) : null)}
              {m.role === 'assistant' && m.sources && m.sources.length > 0 && (
                <div className="sources-container">
                  <h4>Sources:</h4>
                  <ul className="sources">
                    {m.sources.map(s => (
                      <li key={s.index} className="source-item">
                        <span className="source-icon">📄</span>
                        <span className="source-filename">{s.file}</span>
                        {s.page !== undefined && (
                          <span className="source-page">(Page {s.page})</span>
                        )}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          </div>
        ))}
        <div ref={endRef} />
      </div>
      <div className="input-area" onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }}}>
        {error && <p className="error">{error}</p>}
        <div className="chat-input-wrapper">
          <div className="category-tags-container">
            {categories.map(c => (
              <button
                key={c.id}
                className={`category-tag ${selected.includes(c.id) ? 'selected' : ''}`}
                onClick={() => toggleCategory(c.id)}
              >
                {c.name}
              </button>
            ))}
          </div>
          <div className="chat-input-container">
            <textarea
              placeholder="Send a message..."
              value={query}
              onChange={e => setQuery(e.target.value)}
              disabled={loading}
              className="chat-input"
            />
            <button className="send-button" onClick={send} disabled={loading || !query.trim()} aria-label="Send message">
              <SendIcon disabled={loading || !query.trim()} />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

