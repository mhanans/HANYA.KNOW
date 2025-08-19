import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode } from 'react';

const navItems = [
  { href: '/', label: 'Home' },
  { href: '/ingest', label: 'Upload' },
  { href: '/chat', label: 'Chat' },
  { href: '/categories', label: 'Categories' }
];

export default function Layout({ children }: { children: ReactNode }) {
  const router = useRouter();
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
