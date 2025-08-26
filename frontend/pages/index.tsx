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
    <div className="page-container">
      <h1>Dashboard</h1>
      <div className="stats-grid">
        <div className="card stat-card"><span className="stat-icon">💬</span><div><h3 className="stat-title">Chats</h3><p className="stat-value">{stats?.chats ?? 0}</p></div></div>
        <div className="card stat-card"><span className="stat-icon">📄</span><div><h3 className="stat-title">Documents</h3><p className="stat-value">{stats?.documents ?? 0}</p></div></div>
        <div className="card stat-card"><span className="stat-icon">🗂</span><div><h3 className="stat-title">Categories</h3><p className="stat-value">{stats?.categories ?? 0}</p></div></div>
        <div className="card stat-card"><span className="stat-icon">👤</span><div><h3 className="stat-title">Users</h3><p className="stat-value">{stats?.users ?? 0}</p></div></div>
      </div>
      <div className="card">
        <h2>Quick Links</h2>
        <div className="quick-links">
          <Link href="/upload" className="btn btn-primary">Upload Document</Link>
          <Link href="/chat" className="btn btn-secondary">New Chat</Link>
          <Link href="/cv" className="btn btn-secondary">Job Vacancy Analysis</Link>
          <Link href="/data-sources" className="btn btn-secondary">Data Sources</Link>
        </div>
      </div>
    </div>
  );
}
