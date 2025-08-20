import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode } from 'react';

const navItems = [
  { href: '/', label: 'Home' },
  { href: '/documents', label: 'Manage Documents' },
  { href: '/chat', label: 'Chat' },
  { href: '/categories', label: 'Manage Categories' },
  { href: '/roles', label: 'Manage Roles' },
  { href: '/cv', label: 'CV Recommendations' }
];

export default function Layout({ children }: { children: ReactNode }) {
  const router = useRouter();
  if (router.pathname === '/chat') {
    return <>{children}</>;
  }
  return (
    <div className="layout">
      <aside className="sidebar">
        <nav>
          {navItems.map(item => (
            <Link key={item.href} href={item.href} legacyBehavior>
              <a className={router.pathname === item.href ? 'active' : ''}>{item.label}</a>
            </Link>
          ))}
        </nav>
      </aside>
      <main className="content">{children}</main>
    </div>
  );
}
