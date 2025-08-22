import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import Modal from '../components/Modal';

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
  const [recommendationData, setRecommendationData] = useState<Recommendation | null>(null);
  const load = async () => {
    try {
      const res = await apiFetch('/api/recommendations');
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
      const res = await apiFetch('/api/recommendations', {
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
      const res = await apiFetch(`/api/recommendations/${id}/retry`, { method: 'POST' });
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
      const res = await apiFetch(`/api/recommendations/${id}/retry-summary`, { method: 'POST' });
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
  if (recommendationData?.summaryJson) {
    try { candidates = JSON.parse(recommendationData.summaryJson); } catch { /* ignore */ }
  }

  return (
    <div className="page-container">
      <h1>Job Vacancy Analysis</h1>
      <div className="card">
        <p className="hint">Enter position details to get top candidates from uploaded CVs.</p>
        <div className="form-grid">
          <label>Position Name</label>
          <input className="form-input" value={position} onChange={e => setPosition(e.target.value)} />
          <label>Position Details</label>
          <textarea className="form-textarea" value={details} onChange={e => setDetails(e.target.value)} />
        </div>
        <div className="actions">
          <button className="btn btn-primary" onClick={generate} disabled={loading}>{loading ? 'Generating...' : 'Generate'}</button>
        </div>
        {status && <p className="error">{status}</p>}
      </div>

      <h2>Assessments</h2>
      <div className="card table-wrapper">
        <table className="table">
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
                <td>{r.position}</td>
                <td>{r.details}</td>
                <td>{new Date(r.createdAt).toLocaleString()}</td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button className="btn btn-secondary" onClick={() => setRecommendationData(r)}>View Result</button>
                  <button className="btn btn-secondary" onClick={() => retry(r.id)}>Retry</button>
                  <button className="btn btn-secondary" onClick={() => retrySummary(r.id)}>Retry Summary</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Modal
        isOpen={!!recommendationData}
        onClose={() => setRecommendationData(null)}
        title="CV Recommendation"
      >
        {candidates.length ? (
          <div className="table-wrapper">
            <table className="table">
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
                    <td>{c.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p>No structured summary available.</p>
        )}
      </Modal>
    </div>
  );
}
