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

  const send = () => {
    const text = query.trim();
    if (!text || loading) return;
    setQuery('');
    setError('');
    const user: Message = { role: 'user', content: text };
    const assistant: Message = { role: 'assistant', content: '' };
    const assistantIndex = messages.length + 1;
    setMessages(prev => [...prev, user, assistant]);
    setLoading(true);

    const params = new URLSearchParams({ query: text });
    if (conversationId) params.append('conversationId', conversationId);
    selected.forEach(id => params.append('categoryIds', id.toString()));

    const es = new EventSource(`/api/chat/stream?${params.toString()}`, { withCredentials: true });

    es.addEventListener('id', e => {
      setConversationId((e as MessageEvent).data);
    });

    es.addEventListener('sources', e => {
      const raw = JSON.parse((e as MessageEvent).data) as any[];
      const src: Source[] = raw.map(s => ({
        index: s.Index ?? s.index,
        file: s.File ?? s.file,
        page: s.Page ?? s.page,
        content: s.Content ?? s.content,
        score: s.Score ?? s.score,
      }));
      setMessages(prev => {
        const ms = [...prev];
        ms[assistantIndex].sources = src;
        return ms;
      });
    });

    es.addEventListener('token', e => {
      const token = JSON.parse((e as MessageEvent).data);
      setMessages(prev => {
        const ms = [...prev];
        const msg = ms[assistantIndex];
        const existing = msg.content;
        const delta = token.startsWith(existing)
          ? token.slice(existing.length)
          : token;
        msg.content += delta;
        return ms;
      });
    });

    es.addEventListener('error', e => {
      const msg = (e as MessageEvent).data || 'Stream error';
      setError(msg);
      es.close();
      setLoading(false);
    });

    es.addEventListener('done', () => {
      es.close();
      setLoading(false);
    });
  };

  return (
    <>
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
    </>
  );
}

