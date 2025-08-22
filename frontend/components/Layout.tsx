import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode, useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Settings { applicationName?: string; logoUrl?: string; }

const navLinks = [
  { href: '/', label: 'Dashboard' },
  { href: '/documents', label: 'All Documents' },
  { href: '/categories', label: 'Categories' },
  { href: '/upload', label: 'Upload Document' },
  { href: '/document-analytics', label: 'Document Analytics' },
  { href: '/chat', label: 'New Chat' },
  { href: '/chat-history', label: 'Chat History' },
  { href: '/cv', label: 'Job Vacancy Analysis' },
  { href: '/users', label: 'User Management' },
  { href: '/roles', label: 'Manage Role to Category' },
  { href: '/role-ui', label: 'Access Control' },
  { href: '/settings', label: 'System Settings' },
];

export default function Layout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [settings, setSettings] = useState<Settings>({});
  const [username, setUsername] = useState('');

  useEffect(() => {
    apiFetch('/api/settings').then(res => res.json()).then(setSettings).catch(() => {});
    if (router.pathname === '/login') return;
    apiFetch('/api/me').then(res => {
      if (res.ok) return res.json();
      throw new Error('unauthenticated');
    }).then(u => setUsername(u.username)).catch(() => router.push('/login'));
  }, [router.pathname]);

  const logout = async () => {
    await apiFetch('/api/logout', { method: 'POST' });
    if (typeof window !== 'undefined') {
      localStorage.removeItem('token');
    }
    setUsername('');
    router.push('/login');
  };

  if (router.pathname === '/login') {
    return <main className="main-content">{children}</main>;
  }

  return (
    <div className="app-layout">
      <nav className="sidebar">
        <div>
          <div className="sidebar-header"><h2>{settings.applicationName ?? 'HANYA.KNOW'}</h2></div>
          <ul className="nav-links">
            {navLinks.map(link => (
              <li key={link.href}>
                <Link href={link.href} legacyBehavior>
                  <a className={router.pathname === link.href ? 'active' : ''}>{link.label}</a>
                </Link>
              </li>
            ))}
          </ul>
        </div>
        {username && (
          <div className="user-profile">
            <div className="avatar"></div>
            <div className="user-info">
              <span className="user-name">{username}</span>
              <span className="user-role">administrator</span>
            </div>
            <button className="btn-logout" onClick={logout} title="Logout">⎋</button>
          </div>
        )}
      </nav>
      <main className="main-content">{children}</main>
    </div>
  );
}
