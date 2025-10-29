import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

type LlmProvider = 'openai' | 'gemini' | 'ollama' | 'minimax';

interface Settings {
  applicationName?: string;
  logoUrl?: string;
  llmProvider?: LlmProvider;
  llmModel?: string;
  llmApiKey?: string;
  ollamaHost?: string;
}

export default function Settings() {
  const [settings, setSettings] = useState<Settings>({
    applicationName: '',
    logoUrl: '',
    llmProvider: 'openai',
    llmModel: '',
    llmApiKey: '',
    ollamaHost: ''
  });
  const [msg, setMsg] = useState('');

  const load = async () => {
    try {
      const res = await apiFetch('/api/settings');
      if (res.ok) {
        const data = await res.json();
        setSettings(prev => ({ ...prev, ...data }));
      }
    } catch {}
  };

  useEffect(() => { load(); }, []);

  const save = async () => {
    setMsg('');
    try {
      const res = await apiFetch('/api/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings)
      });
      if (!res.ok) throw new Error(await res.text());
      setMsg('Saved');
    } catch (err) {
      setMsg(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="page-container">
      <div className="card" style={{ maxWidth: '600px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <h1>Settings</h1>
        <h2 style={{ marginBottom: 0 }}>General</h2>
        <input
          value={settings.applicationName || ''}
          onChange={e => setSettings({ ...settings, applicationName: e.target.value })}
          placeholder="Application Name"
          className="form-input"
        />
        <input
          value={settings.logoUrl || ''}
          onChange={e => setSettings({ ...settings, logoUrl: e.target.value })}
          placeholder="Logo URL"
          className="form-input"
        />
        <hr style={{ width: '100%' }} />
        <h2 style={{ marginBottom: 0 }}>AI Model</h2>
        <label className="form-label" style={{ fontWeight: 500 }}>Provider</label>
        <select
          value={settings.llmProvider || 'openai'}
          onChange={e => setSettings({ ...settings, llmProvider: e.target.value as LlmProvider })}
          className="form-input"
        >
          <option value="openai">OpenAI (Closed Source)</option>
          <option value="gemini">Gemini (Closed Source)</option>
          <option value="minimax">MiniMax (Closed Source)</option>
          <option value="ollama">Ollama (Open Source)</option>
        </select>
        <input
          value={settings.llmModel || ''}
          onChange={e => setSettings({ ...settings, llmModel: e.target.value })}
          placeholder="Model name"
          className="form-input"
        />
        {settings.llmProvider === 'ollama' ? (
          <input
            type="url"
            value={settings.ollamaHost || ''}
            onChange={e => setSettings({ ...settings, ollamaHost: e.target.value })}
            placeholder="Ollama host (e.g. http://localhost:11434)"
            className="form-input"
            autoComplete="off"
          />
        ) : (
          <input
            type="password"
            value={settings.llmApiKey || ''}
            onChange={e => setSettings({ ...settings, llmApiKey: e.target.value })}
            placeholder="API key"
            className="form-input"
            autoComplete="new-password"
          />
        )}
        {settings.llmProvider === 'ollama' ? (
          <small style={{ color: '#666' }}>The Ollama host must be an HTTP or HTTPS URL reachable from the backend.</small>
        ) : (
          <small style={{ color: '#666' }}>API keys are never logged by the server.</small>
        )}
        <button onClick={save} className="btn btn-primary" style={{ alignSelf: 'flex-start' }}>Save</button>
        {msg && <p>{msg}</p>}
      </div>
    </div>
  );
}
