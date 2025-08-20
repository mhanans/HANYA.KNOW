import { useEffect, useState, useRef } from 'react';

interface Recommendation {
  id: number;
  position: string;
  details: string;
  summary: string;
  summaryJson: string;
  createdAt: string;
}

interface Candidate {
  name: string;
  reason: string;
}

export default function Cv() {
  const [position, setPosition] = useState('');
  const [details, setDetails] = useState('');
  const [recs, setRecs] = useState<Recommendation[]>([]);
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(false);
  const [viewId, setViewId] = useState<number | null>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);
  const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');

  const load = async () => {
    try {
      const res = await fetch(`${base}/api/recommendations`);
      if (res.ok) setRecs(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const generate = async () => {
    setStatus('');
    if (!position.trim() || !details.trim()) {
      setStatus('Position and details are required.');
      return;
    }
    setLoading(true);
    setStatus('Generating...');
    try {
      const res = await fetch(`${base}/api/recommendations`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ position, details })
      });
      if (!res.ok) {
        let msg = res.statusText;
        try {
          const data = await res.json();
          if (data?.detail) msg = data.detail;
        } catch {
          /* ignore */
        }
        setStatus(`Request failed: ${msg}`);
        return;
      }
      setPosition('');
      setDetails('');
      await load();
      setStatus('');
    } catch (err) {
      setStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setLoading(false);
    }
  };

  const retry = async (id: number) => {
    setStatus('Retrying...');
    try {
      const res = await fetch(`${base}/api/recommendations/${id}/retry`, { method: 'POST' });
      if (!res.ok) {
        let msg = res.statusText;
        try {
          const data = await res.json();
          if (data?.detail) msg = data.detail;
        } catch {
          /* ignore */
        }
        setStatus(`Request failed: ${msg}`);
        return;
      }
      await load();
      setStatus('');
    } catch (err) {
      setStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  };

  const retrySummary = async (id: number) => {
    setStatus('Retrying summary...');
    try {
      const res = await fetch(`${base}/api/recommendations/${id}/retry-summary`, { method: 'POST' });
      if (!res.ok) {
        let msg = res.statusText;
        try {
          const data = await res.json();
          if (data?.detail) msg = data.detail;
        } catch {
          /* ignore */
        }
        setStatus(`Request failed: ${msg}`);
        return;
      }
      await load();
      setStatus('');
    } catch (err) {
      setStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  };

  let candidates: Candidate[] = [];
  const rec = viewId !== null ? recs.find(r => r.id === viewId) : null;
  if (rec?.summaryJson) {
    try { candidates = JSON.parse(rec.summaryJson); } catch { /* ignore */ }
  }

  useEffect(() => {
    if (viewId !== null) {
      dialogRef.current?.showModal();
    } else {
      dialogRef.current?.close();
    }
  }, [viewId]);

  return (
    <div className="card cv-card">
      <h1>CV Recommendations</h1>
      <p className="hint">Enter position details to get top candidates from uploaded CVs.</p>

      <div className="cv-grid">
        <label>Position Name</label>
        <input value={position} onChange={e => setPosition(e.target.value)} />
        <label>Position Details</label>
        <textarea value={details} onChange={e => setDetails(e.target.value)} />
      </div>
      <div className="actions">
        <button onClick={generate} disabled={loading}>{loading ? 'Generating...' : 'Generate'}</button>
      </div>
      {status && <p className="error">{status}</p>}

      <h2>Assessments</h2>
      <div className="table-wrapper">
        <table className="rec-table">
          <thead>
            <tr>
              <th>Position</th>
              <th>Details</th>
              <th>Generated</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {recs.map(r => (
              <tr key={r.id}>
                <td className="name">{r.position}</td>
                <td className="detail">{r.details}</td>
                <td>{new Date(r.createdAt).toLocaleString()}</td>
                <td className="actions">
                  <button onClick={() => setViewId(r.id)}>View Result</button>
                  <button onClick={() => retry(r.id)}>Retry</button>
                  <button onClick={() => retrySummary(r.id)}>Retry Summary</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <dialog ref={dialogRef} className="result-dialog" onClose={() => setViewId(null)}>
        <h3>Top Candidates</h3>
        {candidates.length ? (
          <div className="cand-wrapper">
            <table className="cand-table">
              <thead>
                <tr>
                  <th>No</th>
                  <th>Candidate Name</th>
                  <th>Reason</th>
                </tr>
              </thead>
              <tbody>
                {candidates.map((c, i) => (
                  <tr key={i}>
                    <td>{i + 1}</td>
                    <td>{c.name}</td>
                    <td className="reason">{c.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p>No structured summary available.</p>
        )}
        <div className="actions">
          <button onClick={() => setViewId(null)}>OK</button>
        </div>
      </dialog>

      <style jsx>{`
        .cv-grid {
          display: grid;
          grid-template-columns: 150px 1fr;
          gap: 0.5rem 1rem;
        }
        .cv-grid textarea {
          min-height: 80px;
        }
        .table-wrapper { overflow-x: auto; }
        .rec-table {
          width: 100%;
          border-collapse: collapse;
        }
        .rec-table th, .rec-table td {
          padding: 0.5rem;
          text-align: left;
          border-top: 1px solid #ddd;
        }
        .rec-table thead {
          background: #e0e7ff;
          font-weight: 600;
        }
        .rec-table .actions {
          display: flex;
          gap: 0.5rem;
        }
        .cand-wrapper { overflow-x: auto; }
        .cand-table {
          width: 100%;
          border-collapse: collapse;
          border: 1px solid #ddd;
        }
        .cand-table th, .cand-table td {
          padding: 0.5rem;
          text-align: left;
          border: 1px solid #ddd;
        }
        .cand-table thead {
          background: #f3f4f6;
          font-weight: 600;
        }
        .cand-table .reason {
          word-break: break-word;
        }
        .result-dialog::backdrop {
          background: rgba(0,0,0,0.5);
        }
        .result-dialog {
          border: 1px solid #ccc;
          padding: 1rem;
          max-width: 500px;
          width: 100%;
        }
        @media (max-width: 600px) {
          .cv-grid {
            grid-template-columns: 1fr;
          }
          .table-wrapper { width: 100%; }
        }
      `}</style>
    </div>
  );
}
