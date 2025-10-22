import Link from 'next/link';
import { useEffect, useState } from 'react';
import {
  Box,
  Button,
  Card,
  CardContent,
  Grid,
  Stack,
  Typography,
} from '@mui/material';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
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
    <Box sx={{ maxWidth: 1200, mx: 'auto', display: 'flex', flexDirection: 'column', gap: 4 }}>
      <Typography variant="h1">Dashboard</Typography>
      <Grid container spacing={3}>
        {[
          { icon: '💬', label: 'Chats', value: stats?.chats ?? 0 },
          { icon: '📄', label: 'Documents', value: stats?.documents ?? 0 },
          { icon: '🗂', label: 'Categories', value: stats?.categories ?? 0 },
          { icon: '👤', label: 'Users', value: stats?.users ?? 0 },
        ].map(item => (
          <Grid item xs={12} sm={6} md={3} key={item.label}>
            <Card sx={{ height: '100%' }}>
              <CardContent>
                <Stack direction="row" spacing={2} alignItems="center">
                  <Typography component="span" fontSize={36}>
                    {item.icon}
                  </Typography>
                  <Box>
                    <Typography variant="subtitle2" color="text.secondary">
                      {item.label}
                    </Typography>
                    <Typography variant="h3">{item.value}</Typography>
                  </Box>
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>
      <Card>
        <CardContent>
          <Stack spacing={3}>
            <Typography variant="h2">Quick Links</Typography>
            <Stack direction="row" spacing={2} flexWrap="wrap">
              {[ 
                { href: '/upload', label: 'Upload Document', color: 'primary' as const },
                { href: '/chat', label: 'New Chat', color: 'secondary' as const },
                { href: '/source-code', label: 'Source Code Q&A', color: 'secondary' as const },
                { href: '/cv', label: 'Job Vacancy Analysis', color: 'secondary' as const },
                { href: '/data-sources', label: 'Data Sources', color: 'secondary' as const },
              ].map(link => (
                <Button
                  key={link.href}
                  component={Link}
                  href={link.href}
                  variant="contained"
                  color={link.color}
                  endIcon={<ArrowForwardIcon />}
                >
                  {link.label}
                </Button>
              ))}
            </Stack>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}
