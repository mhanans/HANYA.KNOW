import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Doc { source: string; }

export default function DocumentAnalytics() {
  const [docs, setDocs] = useState<Doc[]>([]);
  const [summary, setSummary] = useState('');

  const load = async () => {
    try {
      const res = await apiFetch('/api/documents');
      if (res.ok) setDocs(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const analyze = async (source: string) => {
    setSummary('Loading...');
    try {
      const res = await apiFetch(`/api/documents/summary?source=${encodeURIComponent(source)}`);
      if (res.ok) {
        const data = await res.json();
        setSummary(data.summary || 'No summary available');
      } else {
        setSummary('Failed to load summary');
      }
    } catch {
      setSummary('Failed to load summary');
    }
  };

  return (
    <div className="page-container">
      <h1>Document Analytics</h1>
      <div className="card table-wrapper">
        <table className="table">
          <thead>
            <tr>
              <th>Document</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {docs.map(d => (
              <tr key={d.source}>
                <td>{d.source}</td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button className="btn btn-secondary" onClick={() => analyze(d.source)}>View Summary</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {summary && <pre className="summary">{summary}</pre>}
    </div>
  );
}
