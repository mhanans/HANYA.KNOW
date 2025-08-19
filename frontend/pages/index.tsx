import { useEffect, useState } from 'react';

interface Stats {
  chats: number;
  documents: number;
  categories: number;
}

export default function Home() {
  const [stats, setStats] = useState<Stats | null>(null);
  const base = (process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');

  useEffect(() => {
    fetch(`${base}/api/stats`)
      .then(res => res.json())
      .then(setStats)
      .catch(() => setStats({ chats: 0, documents: 0, categories: 0 }));
  }, []);

  return (
    <div className="card home-card">
      <h1>Dashboard</h1>
      <div className="stats">
        <div className="stat">
          <span className="stat-number">{stats?.chats ?? 0}</span>
          <span className="stat-label">Chats</span>
        </div>
        <div className="stat">
          <span className="stat-number">{stats?.documents ?? 0}</span>
          <span className="stat-label">Documents</span>
        </div>
        <div className="stat">
          <span className="stat-number">{stats?.categories ?? 0}</span>
          <span className="stat-label">Categories</span>
        </div>
      </div>
      <style jsx>{`
        .home-card {
          display: flex;
          flex-direction: column;
          gap: 2rem;
          text-align: center;
          max-width: 600px;
        }
        .stats {
          display: flex;
          gap: 1rem;
        }
        .stat {
          flex: 1;
          background: #f9f9f9;
          border-radius: 8px;
          padding: 1rem;
          box-shadow: 0 2px 6px rgba(0,0,0,0.05);
        }
        .stat-number {
          display: block;
          font-size: 2rem;
          font-weight: bold;
          color: #0070f3;
        }
        .stat-label {
          color: #555;
        }
        @media (max-width: 600px) {
          .stats {
            flex-direction: column;
          }
        }
      `}</style>
    </div>
  );
}
