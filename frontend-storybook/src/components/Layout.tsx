import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode, useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Settings { applicationName?: string; logoUrl?: string; }

const navGroups = [
  {
    label: 'Dashboard',
    items: [
      { href: '/', label: 'Dashboard' }
    ]
  },
  {
    label: 'Documents',
    items: [
      { href: '/documents', label: 'Manage Documents' },
      { href: '/categories', label: 'Categories' },
      { href: '/upload', label: 'Upload Document' },
      { href: '/document-analytics', label: 'Document Analytics' }
    ]
  },
  {
    label: 'AI Assistant',
    items: [
      { href: '/chat', label: 'New Chat' },
      { href: '/chat-history', label: 'Chat History' }
    ]
  },
  {
    label: 'CV Tools',
    items: [
      { href: '/cv', label: 'Job Vacancy Analysis' }
    ]
  },
  {
    label: 'Admin Panel',
    items: [
      { href: '/roles', label: 'Manage Roles' },
      { href: '/role-ui', label: 'UI Access' },
      { href: '/users', label: 'Manage Users' },
      { href: '/settings', label: 'Settings' }
    ]
  }
];

export default function Layout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [open, setOpen] = useState<{ [key: string]: boolean }>({});
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

  const toggle = (label: string) => setOpen(o => ({ ...o, [label]: !o[label] }));

  if (router.pathname === '/login') {
    return <main className="content">{children}</main>;
  }

  return (
    <div className="layout">
      <aside className="sidebar">
        {settings.applicationName && (
          <div className="brand">
            {settings.logoUrl && <img src={settings.logoUrl} alt="logo" />}
            <span>{settings.applicationName}</span>
          </div>
        )}
        {username && <p className="hello">Hello, {username}</p>}
        {username && <button onClick={logout}>Logout</button>}
        <nav>
          {navGroups.map(g => (
            <div key={g.label} className="nav-group">
              <div className="group-label" onClick={() => toggle(g.label)}>{g.label}</div>
              {open[g.label] && g.items.map(item => (
                <Link key={item.href} href={item.href} legacyBehavior>
                  <a className={router.pathname === item.href ? 'active' : ''}>{item.label}</a>
                </Link>
              ))}
            </div>
          ))}
        </nav>
      </aside>
      <main className="content">{children}</main>
      <style jsx>{`
        .group-label { cursor: pointer; font-weight: 600; margin-top: 0.5rem; }
        .nav-group a { display: block; margin-left: 1rem; }
        .brand { display: flex; align-items: center; gap: 0.5rem; padding: 0.5rem 0; }
        .brand img { height: 32px; }
        .hello { padding: 0.5rem 0; }
        .sidebar button { margin-bottom: 0.5rem; }
      `}</style>
    </div>
  );
}
