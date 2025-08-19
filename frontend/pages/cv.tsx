import { useEffect, useState } from 'react';

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
      <div className="rec-grid head">
        <div>Position</div>
        <div>Details</div>
        <div>Generated</div>
        <div>Actions</div>
      </div>
      {recs.map(r => (
        <div key={r.id} className="rec-grid row">
          <div className="name">{r.position}</div>
          <div className="detail">{r.details}</div>
          <div>{new Date(r.createdAt).toLocaleString()}</div>
          <div className="actions">
            <button onClick={() => setViewId(r.id)}>View Result</button>
            <button onClick={() => retry(r.id)}>Retry</button>
            <button onClick={() => retrySummary(r.id)}>Retry Summary</button>
          </div>
        </div>
      ))}

      {viewId !== null && (() => {
        const rec = recs.find(r => r.id === viewId);
        let candidates: Candidate[] = [];
        if (rec?.summaryJson) {
          try { candidates = JSON.parse(rec.summaryJson); } catch { /* ignore */ }
        }
        return (
          <div className="modal">
            <div className="modal-content">
              <h3>Top Candidates</h3>
              {candidates.length ? (
                <div className="cand-wrapper">
                  <div className="cand-grid head">
                    <div>No</div>
                    <div>Candidate Name</div>
                    <div>Reason</div>
                  </div>
                  {candidates.map((c, i) => (
                    <div className="cand-grid row" key={i}>
                      <div>{i + 1}</div>
                      <div>{c.name}</div>
                      <div className="reason">{c.reason}</div>
                    </div>
                  ))}
                </div>
              ) : (
                <p>No structured summary available.</p>
              )}
              <div className="actions">
                <button onClick={() => setViewId(null)}>Close</button>
              </div>
            </div>
          </div>
        );
      })()}

      <style jsx>{`
        .cv-grid {
          display: grid;
          grid-template-columns: 150px 1fr;
          gap: 0.5rem 1rem;
        }
        .cv-grid textarea {
          min-height: 80px;
        }
        .rec-grid {
          display: grid;
          grid-template-columns: 1fr 1fr 150px auto;
          gap: 0.5rem;
          align-items: start;
          padding: 0.25rem 0;
        }
        .rec-grid.head {
          font-weight: 600;
          background: #e0e7ff;
          padding: 0.5rem;
        }
        .rec-grid.row {
          border-top: 1px solid #ddd;
        }
        .cand-grid {
          display: grid;
          grid-template-columns: 40px 1fr 2fr;
          gap: 0.5rem;
          padding: 0.25rem 0;
        }
        .cand-grid.head {
          font-weight: 600;
          background: #f3f4f6;
          padding: 0.5rem;
        }
        .cand-grid.row {
          border-top: 1px solid #ddd;
        }
        .modal {
          position: fixed;
          top: 0;
          left: 0;
          right: 0;
          bottom: 0;
          background: rgba(0,0,0,0.5);
          display: flex;
          align-items: center;
          justify-content: center;
        }
        .modal-content {
          background: #fff;
          padding: 1rem;
          max-width: 500px;
          width: 100%;
        }
        @media (max-width: 600px) {
          .cv-grid {
            grid-template-columns: 1fr;
          }
          .rec-grid {
            grid-template-columns: 1fr;
          }
          .rec-grid.head { display: none; }
          .cand-grid {
            grid-template-columns: 1fr;
          }
          .cand-grid.head { display: none; }
        }
      `}</style>
    </div>
  );
}
