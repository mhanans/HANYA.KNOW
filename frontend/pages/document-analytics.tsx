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
    <div className="card docs-card">
      <h1>Document Analytics</h1>
      <div className="table-wrapper">
        <table className="doc-table">
          <thead>
            <tr>
              <th>Document</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {docs.map(d => (
              <tr key={d.source}>
                <td className="name">{d.source}</td>
                <td className="actions"><button onClick={() => analyze(d.source)}>View Summary</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {summary && <pre className="summary">{summary}</pre>}
      <style jsx>{`
        .docs-card { max-width: none; }
        .table-wrapper { overflow-x: auto; margin-bottom: 1rem; }
        .doc-table { width: 100%; border-collapse: collapse; }
        .doc-table th, .doc-table td { padding: 0.5rem; text-align: left; border-top: 1px solid #ddd; }
        .doc-table thead { background: #e0e7ff; font-weight: 600; }
        .doc-table .actions { display: flex; gap: 0.5rem; }
        .doc-table .name { word-break: break-word; }
        .summary { background: #f5f5f5; padding: 1rem; white-space: pre-wrap; }
      `}</style>
    </div>
  );
}
