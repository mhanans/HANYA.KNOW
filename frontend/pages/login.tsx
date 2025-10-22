import { useState, FormEvent, useEffect, useRef } from 'react';
import { useRouter } from 'next/router';
import { apiFetch } from '../lib/api';

const SSO_HOST = (process.env.NEXT_PUBLIC_ACCELIST_SSO_HOST ?? '').replace(/\/$/, '');
const SSO_APP_ID = process.env.NEXT_PUBLIC_ACCELIST_SSO_APP_ID ?? '';
const SSO_REDIRECT_URI = process.env.NEXT_PUBLIC_ACCELIST_SSO_REDIRECT_URI ?? '';
const DEFAULT_SCOPE = 'email profile openid';
const SSO_SCOPE = (process.env.NEXT_PUBLIC_ACCELIST_SSO_SCOPE ?? DEFAULT_SCOPE).trim() || DEFAULT_SCOPE;
const SSO_LOAD_ERROR = 'Unable to load Accelist SSO. Please refresh and try again.';

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
  const [ssoLoading, setSsoLoading] = useState(false);
  const [resolvedRedirectUri, setResolvedRedirectUri] = useState(SSO_REDIRECT_URI);
  const [ssoReady, setSsoReady] = useState(false);
  const ssoElementContainerRef = useRef<HTMLDivElement | null>(null);
  const tamSsoElementRef = useRef<HTMLElement | null>(null);
  const router = useRouter();
  const ssoEnabled = Boolean(SSO_HOST && SSO_APP_ID && resolvedRedirectUri);

  useEffect(() => {
    if (resolvedRedirectUri || typeof window === 'undefined') {
      return;
    }
    setResolvedRedirectUri(`${window.location.origin}/auth/sso/callback`);
  }, [resolvedRedirectUri]);

  useEffect(() => {
    if (!ssoEnabled) {
      setSsoReady(false);
      return;
    }
    if (typeof window === 'undefined') {
      return;
    }

    let active = true;
    const handleError = () => {
      if (!active) {
        return;
      }
      setSsoReady(false);
      setError(prev => prev || SSO_LOAD_ERROR);
    };

    const markReady = () => {
      if (!active) {
        return;
      }
      setSsoReady(true);
      setError(prev => (prev === SSO_LOAD_ERROR ? '' : prev));
    };

    const awaitDefinition = () => {
      if (!window.customElements) {
        markReady();
        return;
      }
      if (window.customElements.get('tam-sso')) {
        markReady();
        return;
      }
      try {
        window.customElements
          .whenDefined('tam-sso')
          .then(() => {
            markReady();
          })
          .catch(handleError);
      } catch (err) {
        markReady();
      }
    };

    const existing = document.querySelector<HTMLScriptElement>('script[data-accelist-sso]');
    if (existing) {
      awaitDefinition();
      existing.addEventListener('error', handleError);
      return () => {
        active = false;
        existing.removeEventListener('error', handleError);
      };
    }

    const script = document.createElement('script');
    script.src = `${SSO_HOST}/js/tam-sso.js`;
    script.async = true;
    script.dataset.accelistSso = 'true';
    script.addEventListener('load', awaitDefinition);
    script.addEventListener('error', handleError);
    document.body.appendChild(script);

    return () => {
      active = false;
      script.removeEventListener('load', awaitDefinition);
      script.removeEventListener('error', handleError);
    };
  }, [ssoEnabled, setError]);

  useEffect(() => {
    if (typeof window === 'undefined') {
      return;
    }

    const container = ssoElementContainerRef.current;
    if (!container) {
      return;
    }

    if (!ssoEnabled) {
      if (tamSsoElementRef.current) {
        tamSsoElementRef.current.remove();
        tamSsoElementRef.current = null;
      }
      return;
    }

    let element = tamSsoElementRef.current;
    if (!element) {
      element = document.createElement('tam-sso');
      tamSsoElementRef.current = element;
    }

    if (element.parentElement !== container) {
      container.innerHTML = '';
      container.appendChild(element);
    }

    element.setAttribute('app', SSO_APP_ID);
    element.setAttribute('server', SSO_HOST);
    element.setAttribute('scope', SSO_SCOPE);
    if (resolvedRedirectUri) {
      element.setAttribute('redirect-uri', resolvedRedirectUri);
    } else {
      element.removeAttribute('redirect-uri');
    }
    element.setAttribute('auto-submit', 'accelist-sso-form');

    return () => {
      // no-op cleanup; element persists for reuse
    };
  }, [ssoEnabled, resolvedRedirectUri]);

  useEffect(() => {
    return () => {
      if (tamSsoElementRef.current) {
        tamSsoElementRef.current.remove();
        tamSsoElementRef.current = null;
      }
    };
  }, []);

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

  const submitSso = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!ssoEnabled) return;
    setError('');
    const form = e.currentTarget;
    const data = new FormData(form);
    const token = data.get('TAMSignOnToken');
    if (!token || typeof token !== 'string') {
      setError('SSO authentication failed. Please try again.');
      form.reset();
      return;
    }

    setSsoLoading(true);
    try {
      const res = await apiFetch('/api/login/sso', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tamSignOnToken: token })
      });
      if (!res.ok) {
        let message = 'SSO authentication failed.';
        try {
          const payload = await res.json();
          if (payload?.message) message = payload.message;
        } catch {
          // ignore
        }
        throw new Error(message);
      }
      const body = await res.json();
      if (typeof window !== 'undefined') {
        localStorage.setItem('token', body.token);
        document.cookie = `token=${body.token}; path=/`;
      }
      router.push('/');
    } catch (err) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('An unexpected error occurred.');
      }
    } finally {
      form.reset();
      setSsoLoading(false);
    }
  };

  return (
    <div className="login-container">
      <div className="card login-card">
        <form className="login-form" onSubmit={submit}>
          <img src="/logo.svg" alt="HANYA.KNOW logo" className="logo" />
          <h1 className="login-header">Login</h1>
          <p className="login-subtitle">Welcome back! Please sign in to your account.</p>

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
        <div className="accelist-sso-section">
          <div className="login-divider">
            <span>{ssoEnabled ? 'or' : 'Single sign-on'}</span>
          </div>
          {ssoEnabled ? (
            <form
              id="accelist-sso-form"
              className={`accelist-sso-form${ssoLoading ? ' loading' : ''}`}
              onSubmit={submitSso}
            >
              <input type="hidden" name="TAMSignOnToken" />
              <div className="accelist-sso-widget">
                {!ssoReady && (
                  <button type="button" className="btn secondary accelist-sso-placeholder" disabled>
                    Loading Accelist SSO…
                  </button>
                )}
                <div
                  ref={ssoElementContainerRef}
                  className={`accelist-sso-element ${ssoReady ? 'ready' : 'pending'}`}
                />
                {ssoLoading && (
                  <div className="accelist-sso-overlay">
                    <Spinner />
                  </div>
                )}
              </div>
            </form>
          ) : (
            <div className="accelist-sso-widget">
              <button type="button" className="btn secondary accelist-sso-placeholder" disabled>
                Login with SSO not available
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
