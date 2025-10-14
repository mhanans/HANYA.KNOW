import { ChangeEvent, useCallback, useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/router';
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

interface GitHubStatus {
  isConfigured: boolean;
  isConnected: boolean;
  login?: string;
  avatarUrl?: string;
}

interface GitHubRepository {
  id: number;
  name: string;
  fullName: string;
  description?: string;
  private: boolean;
  defaultBranch: string;
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
  const router = useRouter();
  const [githubStatus, setGithubStatus] = useState<GitHubStatus | null>(null);
  const [githubError, setGitHubError] = useState('');
  const [repos, setRepos] = useState<GitHubRepository[]>([]);
  const [reposLoading, setReposLoading] = useState(false);
  const [selectedRepo, setSelectedRepo] = useState('');
  const [selectedBranch, setSelectedBranch] = useState('');
  const [exchangeInFlight, setExchangeInFlight] = useState(false);
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

  const loadReposForStatus = useCallback(async (status?: GitHubStatus | null) => {
    const effectiveStatus = status ?? githubStatus;
    if (!effectiveStatus?.isConnected) {
      setRepos([]);
      setSelectedRepo('');
      setSelectedBranch('');
      return;
    }

    setReposLoading(true);
    setGitHubError('');
    try {
      const res = await apiFetch('/api/github/repos');
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to load repositories.');
      }
      const list: GitHubRepository[] = await res.json();
      setRepos(list);
      if (list.length > 0) {
        if (selectedRepo) {
          const existing = list.find(repo => repo.fullName === selectedRepo);
          if (existing) {
            setSelectedBranch(existing.defaultBranch ?? '');
          } else {
            const fallback = list[0];
            setSelectedRepo(fallback.fullName);
            setSelectedBranch(fallback.defaultBranch ?? '');
          }
        } else {
          setSelectedBranch('');
        }
      } else {
        setSelectedRepo('');
        setSelectedBranch('');
      }
    } catch (err: any) {
      setGitHubError(err?.message || 'Failed to load GitHub repositories.');
    } finally {
      setReposLoading(false);
    }
  }, [githubStatus, selectedRepo]);

  const loadGitHubStatus = useCallback(async () => {
    try {
      const res = await apiFetch('/api/github/status');
      if (!res.ok) return;
      const body: GitHubStatus = await res.json();
      setGithubStatus(body);
      await loadReposForStatus(body);
    } catch {
      /* ignore */
    }
  }, [loadReposForStatus]);

  const connectGitHub = async () => {
    setGitHubError('');
    try {
      const res = await apiFetch('/api/github/login');
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Unable to start GitHub login.');
      }
      const body = await res.json();
      if (!body?.url) throw new Error('Missing GitHub authorization URL.');
      window.location.href = body.url;
    } catch (err: any) {
      setGitHubError(err?.message || 'Failed to start GitHub login.');
    }
  };

  const disconnectGitHub = async () => {
    setGitHubError('');
    try {
      const res = await apiFetch('/api/github/logout', { method: 'POST' });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to disconnect GitHub.');
      }
      setRepos([]);
      setSelectedRepo('');
      setSelectedBranch('');
      await loadGitHubStatus();
    } catch (err: any) {
      setGitHubError(err?.message || 'Failed to disconnect GitHub.');
    }
  };

  const refreshRepos = useCallback(async () => {
    await loadReposForStatus();
  }, [loadReposForStatus]);

  const handleRepoChange = (event: ChangeEvent<HTMLSelectElement>) => {
    const value = event.target.value;
    setSelectedRepo(value);
    if (!value) {
      setSelectedBranch('');
      return;
    }
    const match = repos.find(repo => repo.fullName === value);
    setSelectedBranch(match?.defaultBranch ?? '');
  };

  const handleBranchChange = (event: ChangeEvent<HTMLInputElement>) => {
    setSelectedBranch(event.target.value);
  };

  const exchangeGitHub = useCallback(async (code: string, state: string) => {
    setExchangeInFlight(true);
    setGitHubError('');
    try {
      const res = await apiFetch('/api/github/exchange', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, state }),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || 'Failed to connect GitHub.');
      }
      const body: GitHubStatus = await res.json();
      setGithubStatus(body);
      await loadReposForStatus(body);
    } catch (err: any) {
      setGitHubError(err?.message || 'Failed to complete GitHub login.');
    } finally {
      setExchangeInFlight(false);
      router.replace('/source-code', undefined, { shallow: true });
    }
  }, [loadReposForStatus, router]);

  useEffect(() => {
    refreshStatus();
    const interval = setInterval(refreshStatus, 30000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    loadGitHubStatus();
  }, [loadGitHubStatus]);

  useEffect(() => {
    if (!router.isReady) return;
    const errorParam = router.query.error;
    const errorDescription = router.query.error_description;
    const codeParam = router.query.code;
    const stateParam = router.query.state;
    if (typeof errorParam === 'string') {
      setGitHubError(typeof errorDescription === 'string' ? String(errorDescription) : 'GitHub authorization was cancelled.');
      router.replace('/source-code', undefined, { shallow: true });
      return;
    }
    if (typeof codeParam === 'string' && typeof stateParam === 'string') {
      void exchangeGitHub(codeParam, stateParam);
    }
  }, [exchangeGitHub, router.isReady, router.query.code, router.query.error, router.query.error_description, router.query.state]);

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

  const syncDisabled = syncInFlight || !!syncStatus?.isRunning || exchangeInFlight;

  const triggerSync = async () => {
    if (syncDisabled) return;
    setSyncError('');
    setSyncInFlight(true);
    try {
      const payload: Record<string, string> = {};
      if (selectedRepo) payload.githubRepository = selectedRepo;
      if (selectedBranch) payload.branch = selectedBranch;
      const options: RequestInit = { method: 'POST' };
      if (Object.keys(payload).length > 0) {
        options.headers = { 'Content-Type': 'application/json' };
        options.body = JSON.stringify(payload);
      }
      const res = await apiFetch('/api/source-code/sync', options);
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
      <section className="card github-card">
        <div>
          <h2>GitHub Repository</h2>
          <p className="hint">Connect your GitHub account to import a repository before syncing embeddings.</p>
        </div>
        {githubError && <p className="error">{githubError}</p>}
        {githubStatus === null ? (
          <p className="hint">Checking GitHub connection…</p>
        ) : !githubStatus.isConfigured ? (
          <p className="hint warning">GitHub OAuth belum dikonfigurasi di backend.</p>
        ) : githubStatus.isConnected ? (
          <div className="github-connected">
            <div className="github-user">
              {githubStatus.avatarUrl && (
                <img src={githubStatus.avatarUrl} alt="GitHub avatar" className="github-avatar" />
              )}
              <span className="github-login">@{githubStatus.login}</span>
              <button type="button" className="btn secondary" onClick={disconnectGitHub}>
                Disconnect
              </button>
            </div>
            <div className="github-repo-controls">
              <label htmlFor="github-repo-select">Repository</label>
              <div className="github-repo-row">
                <select
                  id="github-repo-select"
                  value={selectedRepo}
                  onChange={handleRepoChange}
                  disabled={reposLoading}
                >
                  <option value="">Keep current files</option>
                  {repos.map(repo => (
                    <option key={repo.id} value={repo.fullName}>
                      {repo.fullName}
                      {repo.private ? ' (private)' : ''}
                    </option>
                  ))}
                  {repos.length === 0 && <option value="" disabled>No repositories available</option>}
                </select>
                <button type="button" className="btn secondary" onClick={refreshRepos} disabled={reposLoading}>
                  {reposLoading ? 'Loading…' : 'Refresh'}
                </button>
              </div>
              <label htmlFor="github-branch-input">Branch</label>
              <input
                id="github-branch-input"
                type="text"
                value={selectedBranch}
                onChange={handleBranchChange}
                placeholder="Branch name"
                disabled={!selectedRepo}
              />
            </div>
            {selectedRepo && (
              <p className="hint selection-hint">
                Selected repository: <strong>{selectedRepo}</strong>
                {selectedBranch ? ` · branch ${selectedBranch}` : ''}
              </p>
            )}
          </div>
        ) : (
          <button type="button" className="btn" onClick={connectGitHub} disabled={exchangeInFlight}>
            {exchangeInFlight ? 'Opening GitHub…' : 'Login with GitHub'}
          </button>
        )}
      </section>

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
