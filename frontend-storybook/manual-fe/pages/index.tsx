import { useEffect, useState } from 'react';
import Link from 'next/link';
import { apiFetch } from '../lib/api';

interface Stats {
  chats: number;
  documents: number;
  categories: number;
  users: number;
}

export default function Home() {
  const [stats, setStats] = useState<Stats | null>(null);
  useEffect(() => {
    apiFetch('/api/stats')
      .then(res => res.json())
      .then(setStats)
      .catch(() => setStats({ chats: 0, documents: 0, categories: 0, users: 0 }));
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
        <div className="stat">
          <span className="stat-number">{stats?.users ?? 0}</span>
          <span className="stat-label">Users</span>
        </div>
      </div>
      <div className="quick-links">
        <h2>Quick Links</h2>
        <div className="links">
          <Link href="/upload">Upload Document</Link>
          <Link href="/chat">New Chat</Link>
          <Link href="/cv">Job Vacancy Analysis</Link>
        </div>
      </div>
      <style jsx>{`
        .home-card {
          display: flex;
          flex-direction: column;
          gap: 2rem;
          text-align: center;
          max-width: none;
        }
        .stats {
          display: flex;
          gap: 1rem;
          flex-wrap: wrap;
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
        .quick-links .links {
          display: flex;
          gap: 1rem;
        }
        @media (max-width: 600px) {
          .stats {
            flex-direction: column;
          }
          .quick-links .links {
            flex-direction: column;
          }
        }
      `}</style>
    </div>
  );
}
