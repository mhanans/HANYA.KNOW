import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import Modal from '../components/Modal';

interface Doc { source: string; hasSummary: boolean; }

export default function DocumentAnalytics() {
  const [docs, setDocs] = useState<Doc[]>([]);
  const [summary, setSummary] = useState<string | null>(null);

  const load = async () => {
    try {
      const res = await apiFetch('/api/documents');
      if (res.ok) setDocs(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const viewSummary = async (source: string) => {
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

  const generateSummary = async (source: string) => {
    if (!window.confirm('Generate summary for this document?')) return;
    setSummary('Loading...');
    try {
      const res = await apiFetch(`/api/documents/summary?source=${encodeURIComponent(source)}`, {
        method: 'POST'
      });
      if (res.ok) {
        const data = await res.json();
        setSummary(data.summary || 'No summary available');
        alert('Summary generated successfully');
        setDocs(prev => prev.map(p => p.source === source ? { ...p, hasSummary: true } : p));
      } else {
        setSummary(null);
        alert('Failed to generate summary');
      }
    } catch {
      setSummary(null);
      alert('Failed to generate summary');
    }
  };

  const regenerateSummary = async (source: string) => {
    if (!window.confirm('Regenerate summary for this document?')) return;
    setSummary('Loading...');
    try {
      const res = await apiFetch(`/api/documents/summary?source=${encodeURIComponent(source)}`, {
        method: 'POST'
      });
      if (res.ok) {
        const data = await res.json();
        setSummary(data.summary || 'No summary available');
        alert('Summary generated successfully');
      } else {
        setSummary(null);
        alert('Failed to generate summary');
      }
    } catch {
      setSummary(null);
      alert('Failed to generate summary');
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
                  {d.hasSummary ? (
                    <>
                      <button className="btn btn-secondary" onClick={() => viewSummary(d.source)}>View Summary</button>
                      <button className="btn btn-primary" onClick={() => regenerateSummary(d.source)}>Retry Summary</button>
                    </>
                  ) : (
                    <button className="btn btn-primary" onClick={() => generateSummary(d.source)}>Generate Summary</button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Modal
        isOpen={summary !== null}
        onClose={() => setSummary(null)}
        title="Document Summary"
      >
        <pre className="summary">{summary}</pre>
      </Modal>
    </div>
  );
}
