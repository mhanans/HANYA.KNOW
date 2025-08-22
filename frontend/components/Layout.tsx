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
  const [openSection, setOpenSection] = useState<string>(navSections[0].title);
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);

  const toggleSidebar = () => setIsSidebarCollapsed(!isSidebarCollapsed);

  useEffect(() => {
    const handleResize = () => {
      if (window.innerWidth <= 768) {
        setIsSidebarCollapsed(true);
      } else {
        setIsSidebarCollapsed(false);
      }
    };
    handleResize();
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  useEffect(() => {
    apiFetch('/api/settings').then(res => res.json()).then(setSettings).catch(() => {});
    if (router.pathname === '/login') return;
    const current = navSections.find(s => s.links.some(l => l.href === router.pathname));
    if (current) setOpenSection(current.title);
    apiFetch('/api/me')
      .then(res => {
        if (res.ok) return res.json();
        throw new Error('unauthenticated');
      })
      .then(u => setUsername(u.username))
      .catch(() => router.push('/login'));
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
    <div className={`app-layout ${isSidebarCollapsed ? 'sidebar-collapsed' : ''}`}>
      <nav className="sidebar">
        <div>
          <div className="sidebar-header"><h2>{settings.applicationName ?? 'HANYA.KNOW'}</h2></div>
          {navSections.map(section => (
            <div className="nav-group" key={section.title}>
              <h3
                className="nav-group-title"
                onClick={() => setOpenSection(section.title)}
              >
                {section.title}
              </h3>
              {openSection === section.title && (
                <ul className="nav-links">
                  {section.links.map(link => (
                    <li key={link.href}>
                      <Link href={link.href} legacyBehavior>
                        <a className={router.pathname === link.href ? 'active' : ''}>
                          <span className="nav-icon">{link.icon}</span>
                          <span className="nav-label">{link.label}</span>
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
        <button onClick={toggleSidebar} className="sidebar-toggle-btn desktop-only">
          {isSidebarCollapsed ? '»' : '«'}
        </button>
      </nav>
      <main className={`main-content${router.pathname === '/chat' ? ' chat-page' : ''}`}>
        <button onClick={toggleSidebar} className="sidebar-toggle-btn mobile-only">☰</button>
        {children}
      </main>
      {!isSidebarCollapsed && (
        <div className="sidebar-backdrop mobile-only" onClick={toggleSidebar}></div>
      )}
    </div>
  );
}
