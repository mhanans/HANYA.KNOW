import Link from 'next/link';
import { useRouter } from 'next/router';
import { ReactNode, useEffect, useState } from 'react';
import {
  Avatar,
  Box,
  Button,
  Collapse,
  Divider,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  Stack,
  Typography,
} from '@mui/material';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
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
        href: '/pre-sales/presales-ai-history',
        label: 'Presales AI History',
        icon: '🗃️',
        key: 'admin-presales-history',
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
      { href: '/pre-sales/project-templates', label: 'Template Management', icon: '🗂️', key: 'pre-sales-project-templates' },
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
    return (
      <Box component="main" sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
        {children}
      </Box>
    );
  }

  if (router.pathname === '/vendor-invoice-edit') {
    return <>{children}</>;
  }

  return (
    <Box sx={{ display: 'flex', height: '100vh', bgcolor: 'background.default', color: 'text.primary' }}>
      <Box
        component="nav"
        sx={{
          width: 260,
          flexShrink: 0,
          bgcolor: 'background.default',
          borderRight: theme => `1px solid ${theme.palette.divider}`,
          display: 'flex',
          flexDirection: 'column',
          p: 3,
          gap: 3,
        }}
      >
        <Box>
          <Typography variant="h6" fontWeight={700} gutterBottom>
            {settings.applicationName ?? 'HANYA.KNOW'}
          </Typography>
          <Divider sx={{ borderColor: 'divider', mb: 2 }} />
          <Stack spacing={2}>
            {accessibleSections.map(section => {
              const isOpen = openSection === section.title;
              return (
                <Box key={section.title}>
                  <ListItemButton
                    onClick={() => setOpenSection(section.title)}
                    sx={{
                      borderRadius: 2,
                      '&:hover': { bgcolor: 'rgba(255,255,255,0.04)' },
                    }}
                  >
                    <ListItemText
                      primary={
                        <Typography variant="overline" color="text.secondary">
                          {section.title}
                        </Typography>
                      }
                    />
                    {isOpen ? <ExpandLessIcon fontSize="small" /> : <ExpandMoreIcon fontSize="small" />}
                  </ListItemButton>
                  <Collapse in={isOpen} timeout="auto" unmountOnExit>
                    <List disablePadding>
                      {section.links.filter(link => allowed.includes(link.key)).map(link => (
                        <ListItem key={link.href} disablePadding>
                          <Link href={link.href} legacyBehavior passHref>
                            <ListItemButton
                              component="a"
                              selected={router.pathname === link.href}
                              sx={{
                                borderRadius: 2,
                                color: 'text.secondary',
                                '&.Mui-selected': {
                                  bgcolor: 'primary.main',
                                  color: 'common.white',
                                  '&:hover': { bgcolor: 'primary.dark' },
                                },
                              }}
                            >
                              <Stack direction="row" spacing={1.5} alignItems="center">
                                <Typography component="span" fontSize={18}>
                                  {link.icon}
                                </Typography>
                                <Typography variant="body2">{link.label}</Typography>
                              </Stack>
                            </ListItemButton>
                          </Link>
                        </ListItem>
                      ))}
                    </List>
                  </Collapse>
                </Box>
              );
            })}
          </Stack>
        </Box>
        {username && (
          <Box sx={{ mt: 'auto', pt: 3 }}>
            <Stack direction="row" spacing={2} alignItems="center">
              <Avatar sx={{ bgcolor: 'primary.main' }}>{username.slice(0, 2).toUpperCase()}</Avatar>
              <Box sx={{ flexGrow: 1 }}>
                <Typography variant="subtitle2">{username}</Typography>
                <Typography variant="caption" color="text.secondary">
                  administrator
                </Typography>
              </Box>
              <Button variant="outlined" color="error" size="small" onClick={logout} title="Logout">
                Logout
              </Button>
            </Stack>
          </Box>
        )}
      </Box>
      <Box
        component="main"
        sx={{
          flexGrow: 1,
          height: '100vh',
          overflowY: ['/chat', '/source-code'].includes(router.pathname) ? 'hidden' : 'auto',
          p: 4,
        }}
      >
        {children}
      </Box>
    </Box>
  );
}
