import { FormEvent, useEffect, useRef, useState } from 'react';
import Script from 'next/script';
import { useRouter } from 'next/router';
import { apiFetch } from '../lib/api';

const UserIcon = () => (
  <svg width="24" height="24" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 20a8 8 0 1116 0" />
  </svg>
);

const LockIcon = () => (
  <svg width="24" height="24" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M16 11V7a4 4 0 00-8 0v4M5 11h14a2 2 0 012 2v7a2 2 0 01-2 2H5a2 2 0 01-2-2v-7a2 2 0 012-2z" />
  </svg>
);

const EyeIcon = () => (
  <svg width="24" height="24" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.477 0 8.268 2.943 9.542 7-1.274 4.057-5.065 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
  </svg>
);

const EyeOffIcon = () => (
  <svg width="24" height="24" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a10.05 10.05 0 012.319-3.766m3.12-2.507A9.956 9.956 0 0112 5c4.478 0 8.268 2.943 9.543 7-.26.828-.6 1.624-1.015 2.373M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M3 3l18 18" />
  </svg>
);

const WarningIcon = () => (
  <svg width="24" height="24" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10.29 3.86L1.82 18a1 1 0 00.86 1.5h18.64a1 1 0 00.86-1.5L13.71 3.86a1 1 0 00-1.72 0z" />
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v4m0 4h.01" />
  </svg>
);

const Spinner = () => (
  <svg className="spinner" viewBox="0 0 50 50">
    <circle cx="25" cy="25" r="20" fill="none" stroke="currentColor" strokeWidth="4" strokeLinecap="round" />
  </svg>
);

export default function Login() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [remember, setRemember] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [ssoError, setSsoError] = useState('');
  const [ssoLoading, setSsoLoading] = useState(false);
  const router = useRouter();
  const ssoFormRef = useRef<HTMLFormElement>(null);
  const observerRef = useRef<MutationObserver | null>(null);

  useEffect(() => {
    const form = ssoFormRef.current;
    if (!form) {
      return;
    }

    const handleTokenReception = async (tokenInput: HTMLInputElement) => {
      if (!tokenInput.value) {
        return;
      }

      observerRef.current?.disconnect();
      setSsoLoading(true);
      setSsoError('');

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
          return;
        }

        const errorData = await response.json().catch(() => null);
        const message = errorData?.message ?? 'SSO login failed. Please try again.';
        setSsoError(message);
        observerRef.current = new MutationObserver(mutations => {
          for (const mutation of mutations) {
            mutation.addedNodes.forEach(node => {
              if (node.nodeName === 'INPUT' && (node as HTMLInputElement).id === 'TAMSignOnToken') {
                handleTokenReception(node as HTMLInputElement);
              }
            });
          }
        });
        observerRef.current.observe(form, { childList: true, subtree: true });
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to process the SSO token.';
        setSsoError(message);
      } finally {
        setSsoLoading(false);
      }
    };

    observerRef.current = new MutationObserver(mutations => {
      for (const mutation of mutations) {
        mutation.addedNodes.forEach(node => {
          if (node.nodeName === 'INPUT' && (node as HTMLInputElement).id === 'TAMSignOnToken') {
            handleTokenReception(node as HTMLInputElement);
          }
        });
      }
    });

    observerRef.current.observe(form, { childList: true, subtree: true });

    return () => {
      observerRef.current?.disconnect();
    };
  }, [router]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const res = await apiFetch('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });
      if (!res.ok) {
        if (res.status === 401 || res.status === 403) {
          throw new Error('The username or password you entered is incorrect. Please try again.');
        }
        throw new Error('Could not connect to the server. Please check your internet connection and try again.');
      }
      const data = await res.json();
      if (typeof window !== 'undefined') {
        localStorage.setItem('token', data.token);
        document.cookie = `token=${data.token}; path=/`;
      }
      router.push('/');
    } catch (err) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('An unexpected error occurred.');
      }
    } finally {
      setLoading(false);
    }
  };

  const ssoHost = process.env.NEXT_PUBLIC_ACCELIST_SSO_HOST;

  return (
    <div className="login-container">
      <div className="card login-card">
        <img src="/logo.svg" alt="HANYA.KNOW logo" className="logo" />
        <h1 className="login-header">Login</h1>
        <p className="login-subtitle">Welcome back! Please sign in to your account.</p>

        <form className="login-form" onSubmit={submit}>
          <div className="form-group">
            <label htmlFor="username">Username</label>
            <div className="input-wrapper">
              <span className="input-icon"><UserIcon /></span>
              <input
                id="username"
                value={username}
                onChange={e => setUsername(e.target.value)}
                placeholder="e.g., admin"
                className="form-input"
              />
            </div>
          </div>

          <div className="form-group">
            <label htmlFor="password">Password</label>
            <div className="input-wrapper">
              <span className="input-icon"><LockIcon /></span>
              <input
                id="password"
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="••••••••"
                className="form-input"
              />
              <button
                type="button"
                className="toggle-password"
                onClick={() => setShowPassword(s => !s)}
                aria-label={showPassword ? 'Hide password' : 'Show password'}
              >
                {showPassword ? <EyeOffIcon /> : <EyeIcon />}
              </button>
            </div>
          </div>

          <div className="remember-row">
            <label>
              <input
                type="checkbox"
                checked={remember}
                onChange={e => setRemember(e.target.checked)}
              />{' '}
              Remember me
            </label>
            <a href="#" className="forgot-link">Forgot password?</a>
          </div>

          {error && (
            <div className="error-banner">
              <WarningIcon />
              <span>{error}</span>
            </div>
          )}

          <button type="submit" className="btn btn-primary login-button" disabled={loading}>
            {loading ? <Spinner /> : 'Login'}
          </button>
        </form>

        {ssoHost ? (
          <>
            <p></p>
            <p className="login-subtitle">or</p>
            <form id="ssoLoginForm" ref={ssoFormRef} className="sso-form" onSubmit={event => event.preventDefault()}>
              <tam-sso
                app={process.env.NEXT_PUBLIC_ACCELIST_SSO_APP_ID}
                server={ssoHost}
                scope={process.env.NEXT_PUBLIC_ACCELIST_SSO_SCOPE}
                redirect-uri={process.env.NEXT_PUBLIC_ACCELIST_SSO_REDIRECT_URI}
                auto-submit="ssoLoginForm"
                data-label="Login with SSO"
              >
                <button
                  type="button"
                  className="btn btn-secondary login-button sso-button"
                  disabled={ssoLoading}
                >
                  {ssoLoading ? <Spinner /> : 'Login with SSO'}
                </button>
              </tam-sso>
            </form>
            {ssoError && (
              <div className="error-banner">
                <WarningIcon />
                <span>{ssoError}</span>
              </div>
            )}
          </>
        ) : null}
      </div>

      {ssoHost ? <Script src={`${ssoHost}/js/tam-sso.js`} strategy="lazyOnload" /> : null}
    </div>
  );
}
