'use client';

import Script from 'next/script';
import { useEffect } from 'react';

export default function SsoCallbackPage() {
  useEffect(() => {
    const timer = setTimeout(() => {
      window.close();
    }, 10000);

    return () => clearTimeout(timer);
  }, []);

  const ssoHost = process.env.NEXT_PUBLIC_ACCELIST_SSO_HOST;

  return (
    <div>
      <h1>Processing login...</h1>
      {ssoHost ? (
        <Script src={`${ssoHost}/js/callback.js`} strategy="afterInteractive" />
      ) : (
        <p>Missing SSO host configuration.</p>
      )}
    </div>
  );
}
