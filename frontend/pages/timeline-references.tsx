import { useRouter } from 'next/router';
import { useEffect } from 'react';

export default function TimelineReferencesRedirect() {
  const router = useRouter();

  useEffect(() => {
    if (!router.isReady) return;
    router.replace({ pathname: '/pre-sales/configuration', query: { tab: 'timeline' } });
  }, [router]);

  return null;
}
