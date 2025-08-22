import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode, useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Settings { applicationName?: string; logoUrl?: string; }

const navGroups = [
  {
    label: 'General',
    items: [
      { href: '/', label: 'Home' },
      { href: '/chat', label: 'Chat' },
      { href: '/documents', label: 'Manage Documents' }
    ]
  },
  {
    label: 'Management',
    items: [
      { href: '/categories', label: 'Manage Categories' },
      { href: '/roles', label: 'Manage Role to Category' },
      { href: '/role-ui', label: 'Manage Role to UI' },
      { href: '/users', label: 'Manage Users' },
      { href: '/settings', label: 'General Settings' }
    ]
  },
  {
    label: 'Other',
    items: [
      { href: '/cv', label: 'CV Recommendations' }
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
    const u = localStorage.getItem('username');
    if (u) setUsername(u);
  }, []);

  const toggle = (label: string) => setOpen(o => ({ ...o, [label]: !o[label] }));

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
      `}</style>
    </div>
  );
}
