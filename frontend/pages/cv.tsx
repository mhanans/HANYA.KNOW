import { useEffect, useState } from 'react';

interface Recommendation {
  id: number;
  position: string;
  details: string;
  summary: string;
  createdAt: string;
}

export default function Cv() {
  const [position, setPosition] = useState('');
  const [details, setDetails] = useState('');
  const [recs, setRecs] = useState<Recommendation[]>([]);
  const [status, setStatus] = useState('');
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
    } catch (err) {
      setStatus(`Error: ${err instanceof Error ? err.message : String(err)}`);
    }
  };

  const retry = async (id: number) => {
    await fetch(`${base}/api/recommendations/${id}/retry`, { method: 'POST' });
    await load();
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
        <button onClick={generate}>Generate</button>
      </div>
      {status && <p className="error">{status}</p>}

      <h2>Assessments</h2>
      <div className="rec-grid head">
        <div>Position</div>
        <div>Details</div>
        <div>Top Candidates</div>
        <div>Generated</div>
        <div>Actions</div>
      </div>
      {recs.map(r => (
        <div className="rec-grid row" key={r.id}>
          <div className="name">{r.position}</div>
          <div className="detail">{r.details}</div>
          <div className="summary"><pre>{r.summary}</pre></div>
          <div>{new Date(r.createdAt).toLocaleString()}</div>
          <div className="actions">
            <button onClick={() => retry(r.id)}>Retry</button>
          </div>
        </div>
      ))}

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
          grid-template-columns: 1fr 1fr 1fr 150px auto;
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
        .rec-grid .summary pre {
          white-space: pre-wrap;
          margin: 0;
        }
        @media (max-width: 600px) {
          .cv-grid {
            grid-template-columns: 1fr;
          }
          .rec-grid {
            grid-template-columns: 1fr;
          }
          .rec-grid.head { display: none; }
        }
      `}</style>
    </div>
  );
}
