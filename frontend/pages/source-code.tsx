import { useEffect, useRef, useState } from 'react';
import { apiFetch } from '../lib/api';

interface CodeSource {
  id: string;
  order: number;
  filePath: string;
  symbolName?: string;
  startLine?: number;
  endLine?: number;
  score: number;
  content: string;
}

interface Message {
  role: 'user' | 'assistant';
  content: string;
  sources?: CodeSource[];
}

interface SyncStatus {
  isRunning: boolean;
  activeJobStartedAt?: string;
  lastStartedAt?: string;
  lastCompletedAt?: string;
  lastStatus?: string;
  lastError?: string;
  lastFileCount?: number;
  lastChunkCount?: number;
  lastDurationSeconds?: number;
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

export default function SourceCodeChat() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [question, setQuestion] = useState('');
  const [loading, setLoading] = useState(false);
  const [chatError, setChatError] = useState('');
  const [syncError, setSyncError] = useState('');
  const [syncStatus, setSyncStatus] = useState<SyncStatus | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [syncInFlight, setSyncInFlight] = useState(false);
  const endRef = useRef<HTMLDivElement>(null);
  const initials = 'AD';

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const storedMessages = localStorage.getItem('source-code-messages');
    const storedSession = localStorage.getItem('source-code-session');
    if (storedMessages) setMessages(JSON.parse(storedMessages));
    if (storedSession) setSessionId(storedSession);
  }, []);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    localStorage.setItem('source-code-messages', JSON.stringify(messages));
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    if (sessionId) {
      localStorage.setItem('source-code-session', sessionId);
    } else {
      localStorage.removeItem('source-code-session');
    }
  }, [sessionId]);

  const refreshStatus = async () => {
    try {
      const res = await apiFetch('/api/source-code/status');
      if (!res.ok) return;
      const body = await res.json();
      setSyncStatus(body);
    } catch {
      /* ignore */
    }
  };

  useEffect(() => {
    refreshStatus();
    const interval = setInterval(refreshStatus, 30000);
    return () => clearInterval(interval);
  }, []);

  const formatDate = (value?: string) => {
    if (!value) return 'Never';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return 'Never';
    return date.toLocaleString();
  };

  const formatDuration = (seconds?: number) => {
    if (!seconds || seconds <= 0) return '—';
    if (seconds < 60) return `${seconds.toFixed(1)}s`;
    const mins = Math.floor(seconds / 60);
    const rem = seconds % 60;
    return `${mins}m ${rem.toFixed(1)}s`;
  };

  const syncDisabled = syncInFlight || !!syncStatus?.isRunning;

  const triggerSync = async () => {
    if (syncDisabled) return;
    setSyncError('');
    setSyncInFlight(true);
    try {
      const res = await apiFetch('/api/source-code/sync', { method: 'POST' });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to sync source code.');
      }
      const body = await res.json();
      setSyncStatus(body);
    } catch (err: any) {
      setSyncError(err?.message || 'Failed to sync source code.');
    } finally {
      await refreshStatus();
      setSyncInFlight(false);
    }
  };

  const ask = async () => {
    const text = question.trim();
    if (!text || loading) return;
    setQuestion('');
    setChatError('');
    const user: Message = { role: 'user', content: text };
    const assistant: Message = { role: 'assistant', content: '' };
    let assistantIndex = 0;
    setMessages(prev => {
      const next = [...prev, user, assistant];
      assistantIndex = next.length - 1;
      return next;
    });
    setLoading(true);

    try {
      const res = await apiFetch('/api/chat/source-code', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: text, sessionId }),
      });

      if (!res.ok) {
        const msg = await res.text();
        throw new Error(msg || 'Failed to query source code.');
      }

      const body = await res.json();
      setSessionId(body.sessionId ?? null);
      setMessages(prev => {
        const next = [...prev];
        const target = next[assistantIndex];
        target.content = body.answer ?? '';
        if (Array.isArray(body.sources)) {
          target.sources = body.sources.map((s: any) => ({
            id: String(s.id ?? s.Id ?? ''),
            order: s.order ?? s.Order ?? 0,
            filePath: s.filePath ?? s.FilePath ?? '',
            symbolName: s.symbolName ?? s.SymbolName ?? undefined,
            startLine: s.startLine ?? s.StartLine ?? undefined,
            endLine: s.endLine ?? s.EndLine ?? undefined,
            score: s.score ?? s.Score ?? 0,
            content: s.content ?? s.Content ?? '',
          }));
        }
        return next;
      });
    } catch (err: any) {
      setChatError(err?.message || 'Failed to query source code.');
      setMessages(prev => {
        const next = [...prev];
        next[assistantIndex].content = 'An error occurred while generating the answer.';
        return next;
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="source-code-page">
      <section className="card sync-card">
        <div>
          <h1>Source Code Q&amp;A</h1>
          <p className="hint">Keep the embeddings in sync before asking detailed repository questions.</p>
          <div className="sync-meta">
            <div><span className="meta-label">Last sync:</span> {formatDate(syncStatus?.lastCompletedAt)}</div>
            <div><span className="meta-label">Status:</span> {syncStatus?.lastStatus ?? (syncStatus?.isRunning ? 'running' : 'unknown')}</div>
            <div><span className="meta-label">Processed:</span> {syncStatus?.lastFileCount ?? 0} files · {syncStatus?.lastChunkCount ?? 0} chunks</div>
            <div><span className="meta-label">Duration:</span> {formatDuration(syncStatus?.lastDurationSeconds)}</div>
          </div>
          {syncStatus?.lastStatus === 'failed' && syncStatus.lastError && (
            <p className="error">Last sync failed: {syncStatus.lastError}</p>
          )}
          {syncError && <p className="error">{syncError}</p>}
        </div>
        <button type="button" className="btn" disabled={syncDisabled} onClick={triggerSync}>
          {syncInFlight || syncStatus?.isRunning ? 'Syncing…' : 'Sync Source Code'}
        </button>
      </section>

      <section className="card chat-card">
        {chatError && <p className="error">{chatError}</p>}
        <div className="messages-container">
          {messages.map((message, index) => (
            <div key={index} className={`chat-message ${message.role === 'assistant' ? 'assistant' : 'user'}`}>
              <div className="avatar">{message.role === 'assistant' ? '🤖' : initials}</div>
              <div className="bubble">
                {message.content || (loading && message.role === 'assistant' && index === messages.length - 1 ? (
                  <div className="typing-indicator"><span /><span /><span /></div>
                ) : null)}
                {message.role === 'assistant' && message.sources && message.sources.length > 0 && (
                  <div className="sources-container code-sources">
                    <h4>Source snippets:</h4>
                    <ul className="sources">
                      {message.sources.map(source => (
                        <li key={source.id || source.order} className="source-item code-source-item">
                          <div className="code-source-header">
                            <span className="source-icon">📄</span>
                            <span className="source-filename">{source.filePath}</span>
                            {source.symbolName && <span className="source-symbol"> {source.symbolName}</span>}
                            {(source.startLine !== undefined || source.endLine !== undefined) && (
                              <span className="source-page">(lines {source.startLine ?? '?'}–{source.endLine ?? '?'})</span>
                            )}
                          </div>
                          <pre className="code-snippet"><code>{source.content}</code></pre>
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
        <div className="input-area" onKeyDown={event => {
          if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            ask();
          }
        }}>
          <div className="chat-input-wrapper">
            <div className="chat-input-container">
              <textarea
                placeholder="Ask a question about the source code..."
                value={question}
                onChange={event => setQuestion(event.target.value)}
                disabled={loading}
                className="chat-input"
              />
              <button
                className="send-button"
                onClick={ask}
                disabled={loading || !question.trim()}
                aria-label="Send question"
              >
                <SendIcon disabled={loading || !question.trim()} />
              </button>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}
