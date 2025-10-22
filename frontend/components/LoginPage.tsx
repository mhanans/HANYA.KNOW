'use client';

import { useEffect, useRef } from 'react';
import Script from 'next/script';
import { useRouter } from 'next/navigation';

export default function LoginPage() {
  const router = useRouter();
  const formRef = useRef<HTMLFormElement>(null);
  const observerRef = useRef<MutationObserver | null>(null);

  useEffect(() => {
    const handleTokenReception = async (tokenInput: HTMLInputElement) => {
      if (tokenInput && tokenInput.value) {
        if (observerRef.current) {
          observerRef.current.disconnect();
        }

        try {
          const tokens = JSON.parse(tokenInput.value);
          if (!tokens.id_token) {
            throw new Error('ID token is missing from SSO response.');
          }

          const response = await fetch('/api/auth/sso-login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ idToken: tokens.id_token }),
            credentials: 'include',
          });

          if (response.ok) {
            router.push('/');
          } else {
            const errorData = await response.json().catch(() => null);
            console.error('SSO login failed on the backend:', errorData?.message ?? response.statusText);
          }
        } catch (error) {
          console.error('Failed to parse or process the SSO token.', error);
        }
      }
    };

    if (formRef.current) {
      observerRef.current = new MutationObserver(mutations => {
        for (const mutation of mutations) {
          mutation.addedNodes.forEach(node => {
            if (node.nodeName === 'INPUT' && (node as HTMLInputElement).id === 'TAMSignOnToken') {
              handleTokenReception(node as HTMLInputElement);
            }
          });
        }
      });

      observerRef.current.observe(formRef.current, { childList: true });
    }

    return () => {
      if (observerRef.current) {
        observerRef.current.disconnect();
      }
    };
  }, [router]);

  const ssoHost = process.env.NEXT_PUBLIC_ACCELIST_SSO_HOST;

  return (
    <>
      <div className="login-container">
        <h2>TAM SSO Login</h2>
        <form id="ssoLoginForm" ref={formRef}>
          <tam-sso
            app={process.env.NEXT_PUBLIC_ACCELIST_SSO_APP_ID}
            server={ssoHost}
            scope={process.env.NEXT_PUBLIC_ACCELIST_SSO_SCOPE}
            redirect-uri={process.env.NEXT_PUBLIC_ACCELIST_SSO_REDIRECT_URI}
            auto-submit="ssoLoginForm"
          ></tam-sso>
        </form>
      </div>

      {ssoHost ? (
        <Script src={`${ssoHost}/js/tam-sso.js`} strategy="lazyOnload" />
      ) : null}
    </>
  );
}
