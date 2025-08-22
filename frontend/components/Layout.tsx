import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode, useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Settings { applicationName?: string; logoUrl?: string; }

interface NavItem { href: string; label: string; icon: string; }
const navSections: { title: string; links: NavItem[] }[] = [
  { title: 'General', links: [{ href: '/', label: 'Dashboard', icon: '🏠' }] },
  {
    title: 'Content Management',
    links: [
      { href: '/documents', label: 'All Documents', icon: '📄' },
      { href: '/categories', label: 'Categories', icon: '🗂' },
      { href: '/upload', label: 'Upload Document', icon: '⬆️' },
      { href: '/document-analytics', label: 'Document Analytics', icon: '📈' },
    ],
  },
  {
    title: 'Chat',
    links: [
      { href: '/chat', label: 'New Chat', icon: '💬' },
      { href: '/chat-history', label: 'Chat History', icon: '🕓' },
    ],
  },
  { title: 'AI Tools', links: [{ href: '/cv', label: 'Job Vacancy Analysis', icon: '🧠' }] },
  {
    title: 'Admin',
    links: [
      { href: '/users', label: 'User Management', icon: '👤' },
      { href: '/roles', label: 'Manage Role to Category', icon: '🔧' },
      { href: '/role-ui', label: 'Access Control', icon: '🔐' },
      { href: '/settings', label: 'System Settings', icon: '⚙️' },
    ],
  },
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
          {navSections.map(section => (
            <div className="nav-group" key={section.title}>
              <h3 className="nav-group-title">{section.title}</h3>
              <ul className="nav-links">
                {section.links.map(link => (
                  <li key={link.href}>
                    <Link href={link.href} legacyBehavior>
                      <a className={router.pathname === link.href ? 'active' : ''}>
                        <span className="nav-icon">{link.icon}</span>
                        {link.label}
                      </a>
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          ))}
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
