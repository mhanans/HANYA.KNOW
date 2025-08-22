import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Settings { applicationName?: string; logoUrl?: string; }

export default function Settings() {
  const [settings, setSettings] = useState<Settings>({ applicationName: '', logoUrl: '' });
  const [msg, setMsg] = useState('');

  const load = async () => {
    try {
      const res = await apiFetch('/api/settings');
      if (res.ok) setSettings(await res.json());
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
    <div className="card">
      <h1>General Settings</h1>
      <input value={settings.applicationName || ''} onChange={e => setSettings({ ...settings, applicationName: e.target.value })} placeholder="Application Name" />
      <input value={settings.logoUrl || ''} onChange={e => setSettings({ ...settings, logoUrl: e.target.value })} placeholder="Logo URL" />
      <button onClick={save}>Save</button>
      {msg && <p>{msg}</p>}
    </div>
  );
}
