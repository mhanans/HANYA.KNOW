import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Settings {
  applicationName?: string;
  logoUrl?: string;
}

export default function Settings() {
  const [settings, setSettings] = useState<Settings>({
    applicationName: '',
    logoUrl: ''
  });
  const [msg, setMsg] = useState('');

  const load = async () => {
    try {
      const res = await apiFetch('/api/settings');
      if (res.ok) {
        const data = await res.json();
        setSettings(prev => ({ ...prev, ...data }));
      }
    } catch { }
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
        <button onClick={save} className="btn btn-primary" style={{ alignSelf: 'flex-start' }}>Save</button>
        {msg && <p>{msg}</p>}
      </div>
    </div>
  );
}
