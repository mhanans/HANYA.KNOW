import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode, useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface Settings { applicationName?: string; logoUrl?: string; }

interface NavItem { href: string; label: string; icon: string; key: string; }
const navSections: { title: string; links: NavItem[] }[] = [
  { title: 'General', links: [{ href: '/', label: 'Dashboard', icon: '🏠', key: 'dashboard' }] },
  {
    title: 'Content Management',
    links: [
      { href: '/documents', label: 'All Documents', icon: '📄', key: 'documents' },
      { href: '/categories', label: 'Categories', icon: '🗂', key: 'categories' },
      { href: '/upload', label: 'Upload Document', icon: '⬆️', key: 'upload' },
      { href: '/document-analytics', label: 'Document Analytics', icon: '📈', key: 'document-analytics' },
    ],
  },
  {
    title: 'Chat',
    links: [
      { href: '/chat', label: 'New Chat', icon: '💬', key: 'chat' },
      { href: '/chat-history', label: 'Chat History', icon: '🕓', key: 'chat-history' },
      { href: '/source-code', label: 'Source Code Q&A', icon: '🧩', key: 'source-code' },
    ],
  },
  {
    title: 'AI Tools',
    links: [
      { href: '/cv', label: 'Job Vacancy Analysis', icon: '🧠', key: 'cv' },
      { href: '/data-sources', label: 'Chat with Table', icon: '📊', key: 'data-sources' },
      { href: '/invoice-verification', label: 'Invoice Verification', icon: '🧾', key: 'invoice-verification' },
    ],
  },
  {
    title: 'Pre-Sales',
    links: [
      {
        href: '/pre-sales/project-templates',
        label: 'Project Templates',
        icon: '🗂',
        key: 'pre-sales-project-templates',
      },
      {
        href: '/pre-sales/workspace',
        label: 'Assessment Workspace',
        icon: '🛠️',
        key: 'pre-sales-assessment-workspace',
      },
      {
        href: '/pre-sales/project-timelines',
        label: 'Project Timelines',
        icon: '📅',
        key: 'pre-sales-project-timelines',
      },
      {
        href: '/pre-sales/presales-ai-history',
        label: 'Presales AI History',
        icon: '🗃️',
        key: 'admin-presales-history',
      },
      {
        href: '/pre-sales/configuration',
        label: 'Presales Configuration',
        icon: '⚙️',
        key: 'pre-sales-configuration',
      },
    ],
  },
  {
    title: 'Support',
    links: [
      { href: '/tickets', label: 'Tickets', icon: '🎫', key: 'tickets' },
      { href: '/pic-summary', label: 'PIC Summary', icon: '👥', key: 'pic-summary' },
    ],
  },
  {
    title: 'Admin',
    links: [
      { href: '/users', label: 'User Management', icon: '👤', key: 'users' },
      { href: '/roles', label: 'Manage Role', icon: '🔧', key: 'roles' },
      { href: '/role-ui', label: 'Access Control', icon: '🔐', key: 'role-ui' },
      { href: '/settings', label: 'System Settings', icon: '⚙️', key: 'settings' },
    ],
  },
];

export default function Layout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [settings, setSettings] = useState<Settings>({});
  const [username, setUsername] = useState('');
  const [openSection, setOpenSection] = useState<string>('');
  const [allowed, setAllowed] = useState<string[]>([]);
  const [uiLoaded, setUiLoaded] = useState(false);

  useEffect(() => {
    apiFetch('/api/settings').then(res => res.json()).then(setSettings).catch(() => {});
    if (router.pathname === '/login') return;
    apiFetch('/api/me')
      .then(res => {
        if (res.ok) return res.json();
        throw new Error('unauthenticated');
      })
      .then(u => {
        setUsername(u.username);
        return apiFetch('/api/ui').then(r => r.json()).then((pages: { key: string }[]) => {
          const keys = pages.map(p => p.key);
          setAllowed(keys);
          const current = navSections.find(s => s.links.some(l => l.href === router.pathname && keys.includes(l.key)));
          if (current) {
            setOpenSection(current.title);
          } else {
            const first = navSections.find(s => s.links.some(l => keys.includes(l.key)));
            if (first) setOpenSection(first.title);
          }
          setUiLoaded(true);
        });
      })
      .catch(() => router.push('/login'));
  }, [router.pathname]);

  const accessibleSections = navSections
    .map(section => ({ ...section, links: section.links.filter(link => allowed.includes(link.key)) }))
    .filter(section => section.links.length > 0);

  useEffect(() => {
    if (!uiLoaded || router.pathname === '/login' || router.pathname === '/401') return;
    const allLinks = navSections.flatMap(s => s.links);
    const current = allLinks.find(l => l.href === router.pathname);
    if (current && !allowed.includes(current.key)) {
      router.push('/401');
    }
  }, [uiLoaded, allowed, router.pathname]);

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

  if (router.pathname === '/vendor-invoice-edit') {
    return <>{children}</>;
  }

  return (
    <div className="app-layout">
      <nav className="sidebar">
        <div>
          <div className="sidebar-header"><h2>{settings.applicationName ?? 'HANYA.KNOW'}</h2></div>
          {accessibleSections.map(section => (
            <div className="nav-group" key={section.title}>
              <h3
                className="nav-group-title"
                onClick={() => setOpenSection(section.title)}
              >
                {section.title}
              </h3>
              {openSection === section.title && (
                <ul className="nav-links">
                  {section.links.filter(link => allowed.includes(link.key)).map(link => (
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
              )}
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
            <button className="btn-logout" onClick={logout} title="Logout">
              ⎋ Logout
            </button>
          </div>
        )}
      </nav>
      <main className={`main-content${['/chat', '/source-code'].includes(router.pathname) ? ' chat-page' : ''}`}>{children}</main>
    </div>
  );
}