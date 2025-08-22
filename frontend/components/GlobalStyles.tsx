import React from 'react';

const globalCss = `
    /*
     * =================================================================
     * A. GLOBAL THEME & RESET (theme.css)
     * =================================================================
     */
    :root {
      /* Color Palette */
      --bg-primary: #1e1e1e;
      --bg-secondary: #2d2d2d;
      --surface: #3a3a3a;
      --primary-accent: #007bff;
      --primary-accent-hover: #0056b3;
      --text-primary: #e0e0e0;
      --text-secondary: #a0a0a0;
      --border-color: #4d4d4d;
      --border-color-focus: #a0a0a0;
      --error-color: #e53e3e;

      /* Typography */
      --font-sans: -apple-system, BlinkMacSystem-Font, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;

      /* Sizing & Spacing */
      --spacing-unit: 8px;
      --border-radius-sm: 4px;
      --border-radius-md: 8px;
      --border-radius-lg: 16px;
      --border-radius-full: 9999px;

      /* Shadows */
      --shadow-sm: 0 1px 2px 0 rgb(0 0 0 / 0.15);
      --shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.2), 0 2px 4px -2px rgb(0 0 0 / 0.2);
    }
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    body { background-color: var(--bg-primary); color: var(--text-primary); font-family: var(--font-sans); -webkit-font-smoothing: antialiased; -moz-osx-font-smoothing: grayscale; }
    h1, h2, h3 { color: var(--text-primary); margin-bottom: calc(var(--spacing-unit) * 2); }
    h1 { font-size: 2rem; } h2 { font-size: 1.5rem; } p { line-height: 1.6; color: var(--text-secondary); }
    a { color: var(--primary-accent); text-decoration: none; } a:hover { text-decoration: underline; }

    /* --- Base Component Styles --- */
    .btn { display: inline-flex; align-items: center; justify-content: center; padding: calc(var(--spacing-unit) * 1.5) calc(var(--spacing-unit) * 3); border: 1px solid transparent; border-radius: var(--border-radius-md); font-size: 1rem; font-weight: 500; cursor: pointer; transition: all 0.2s ease-in-out; }
    .btn-primary { background-color: var(--primary-accent); color: white; } .btn-primary:hover { background-color: var(--primary-accent-hover); }
    .btn-secondary { background-color: var(--surface); color: var(--text-primary); border-color: var(--border-color); } .btn-secondary:hover { background-color: var(--border-color); }
    .btn-danger { background: transparent; color: var(--error-color); border-color: var(--error-color); } .btn-danger:hover { background-color: rgba(229,62,62,0.1); }
    .form-input, .form-textarea, .form-select { width: 100%; background-color: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: var(--border-radius-md); padding: calc(var(--spacing-unit) * 1.5); color: var(--text-primary); font-size: 1rem; transition: all 0.2s ease; }
    .form-input:focus, .form-textarea:focus, .form-select:focus { outline: none; border-color: var(--border-color-focus); box-shadow: 0 0 0 3px rgba(0, 123, 255, 0.25); }
    .form-input::placeholder, .form-textarea::placeholder { color: var(--text-secondary); }
    .card { background-color: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: var(--border-radius-lg); padding: calc(var(--spacing-unit) * 3); box-shadow: var(--shadow-sm); }
    .table { width: 100%; border-collapse: collapse; margin-top: calc(var(--spacing-unit) * 2); }
    .table th, .table td { padding: calc(var(--spacing-unit) * 1.5); text-align: left; border-bottom: 1px solid var(--border-color); }
    .table th { color: var(--text-primary); font-weight: 600; } .table tbody tr:hover { background-color: var(--surface); }
    .tag { display: inline-flex; align-items: center; gap: 4px; background-color: var(--surface); color: var(--text-secondary); border: 1px solid var(--border-color); border-radius: var(--border-radius-full); padding: 4px 12px; font-size: 0.8rem; font-weight: 500; }
    .tag button { background: none; border: none; color: inherit; cursor: pointer; padding: 0; }
    .tag-input .tags { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
    .tag-input select { min-width: 10rem; }

    .modal-backdrop { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background-color: rgba(0,0,0,0.6); display: flex; align-items: center; justify-content: center; z-index: 1000; }
    .modal-content { background-color: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: var(--border-radius-lg); max-width: 600px; width: 100%; }
    .modal-header, .modal-body, .modal-footer { padding: 16px; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border-color); }
    .modal-footer { display: flex; justify-content: flex-end; border-top: 1px solid var(--border-color); }
    .close-button { background: none; border: none; color: var(--text-secondary); font-size: 1.5rem; cursor: pointer; }
    .close-button:hover { color: var(--text-primary); }
    
    /*
     * =================================================================
     * B. LAYOUT STYLES (appLayout.css)
     * =================================================================
     */
    .app-layout { display: flex; height: 100vh; width: 100%; background-color: var(--bg-primary); }
    .sidebar { width: 240px; flex-shrink: 0; background-color: var(--bg-secondary); border-right: 1px solid var(--border-color); padding: calc(var(--spacing-unit) * 2); display: flex; flex-direction: column; }
    .sidebar-header h2 { padding: var(--spacing-unit); }
    .nav-group { margin-top: calc(var(--spacing-unit) * 2); }
    .nav-group-title { font-size: 0.75rem; color: var(--text-secondary); margin-bottom: var(--spacing-unit); text-transform: uppercase; }
    .nav-links { list-style: none; }
    .nav-links a { display: flex; align-items: center; gap: 8px; padding: calc(var(--spacing-unit) * 1.5) var(--spacing-unit); color: var(--text-secondary); text-decoration: none; border-radius: var(--border-radius-md); transition: all 0.2s ease; white-space: nowrap; }
    .nav-links a:hover { background-color: var(--surface); color: var(--text-primary); }
    .nav-links a.active { background-color: var(--primary-accent); color: white; }
    .nav-icon { font-size: 1.2rem; }
    .main-content { flex-grow: 1; overflow-y: auto; height: 100vh; padding: 32px; }
    .user-profile { margin-top: auto; padding-top: calc(var(--spacing-unit) * 2); border-top: 1px solid var(--border-color); display: flex; align-items: center; gap: var(--spacing-unit); }
    .user-profile .avatar { width: 40px; height: 40px; border-radius: 50%; background-color: var(--primary-accent); flex-shrink: 0; }
    .user-profile .user-info { display: flex; flex-direction: column; flex-grow: 1; } .user-name { color: var(--text-primary); font-weight: 500; } .user-role { color: var(--text-secondary); font-size: 0.8rem; text-transform: capitalize; }
    .btn-logout { background: transparent; border: none; color: var(--text-secondary); cursor: pointer; padding: var(--spacing-unit); border-radius: var(--border-radius-md); transition: all 0.2s ease; }
    .btn-logout:hover { background-color: var(--surface); color: var(--error-color); }

    /*
     * =================================================================
     * C. PAGE-SPECIFIC LAYOUTS (page-specific.css)
     * =================================================================
     */
    .page-container { max-width: 1200px; margin: 0 auto; display: flex; flex-direction: column; gap: 32px; }
    .filters, .controls { display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 16px; }
    .table-wrapper { overflow-x: auto; }
    .empty-state { text-align: center; padding: 40px 0; display: flex; flex-direction: column; gap: 16px; align-items: center; }
    .form-grid { display: grid; grid-template-columns: 150px 1fr; gap: 12px 16px; align-items: center; }
    .form-grid label { text-align: right; }
    @media (max-width: 600px) { .form-grid { grid-template-columns: 1fr; } .form-grid label { text-align: left; } }
    .stat-card { display: flex; align-items: center; gap: 16px; }
    .stat-icon { font-size: 2rem; }
    .summary { background-color: var(--surface); padding: 16px; white-space: pre-wrap; }
    .error { color: var(--error-color); }
    .page-header { display: flex; justify-content: space-between; align-items: center; }
    .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 24px; }
    .stat-title { font-size: 1rem; color: var(--text-secondary); margin-bottom: 8px; } .stat-value { font-size: 2.5rem; color: var(--text-primary); font-weight: 600; line-height: 1; }
    .quick-links { display: flex; gap: 16px; flex-wrap: wrap; }

    /*
     * =================================================================
     * D. CHAT COMPONENT STYLES (chat*.css)
     * =================================================================
     */
    .chat-interface { display: flex; flex-direction: column; width: 100%; height: 100%; background-color: var(--bg-primary); overflow: hidden; }
    .messages-container { flex-grow: 1; padding: 24px 24px 0 24px; overflow-y: auto; display: flex; flex-direction: column; gap: 20px; justify-content: flex-end; min-height: 0; }
    .messages-container::-webkit-scrollbar { width: 8px; } .messages-container::-webkit-scrollbar-track { background: transparent; } .messages-container::-webkit-scrollbar-thumb { background-color: #4d4d4d; border-radius: 4px; }
    .input-area { padding: 16px 24px 24px 24px; background: linear-gradient(to top, var(--bg-primary) 80%, rgba(30, 30, 30, 0)); }
    .welcome-screen { flex-grow: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; text-align: center; color: var(--text-secondary); }
    .typing-indicator { display: flex; align-items: center; gap: 5px; padding: 12px 0; }
    .typing-indicator span { height: 8px; width: 8px; background-color: #8d8d8d; border-radius: 50%; display: inline-block; animation: bounce 1.4s infinite ease-in-out both; }
    .typing-indicator span:nth-child(1) { animation-delay: -0.32s; } .typing-indicator span:nth-child(2) { animation-delay: -0.16s; }
    @keyframes bounce { 0%, 80%, 100% { transform: scale(0); } 40% { transform: scale(1.0); } }

    .chat-message { display: flex; align-items: flex-start; gap: 12px; max-width: 85%; }
    .chat-message.user { align-self: flex-end; flex-direction: row-reverse; } .chat-message.assistant { align-self: flex-start; }
    .chat-message .avatar { width: 36px; height: 36px; border-radius: 50%; object-fit: cover; flex-shrink: 0; margin-top: 4px; background-color: var(--surface); }
    .chat-message .bubble { white-space: pre-wrap; padding: 12px 16px; border-radius: 18px; line-height: 1.5; word-wrap: break-word; }
    .chat-message.user .bubble { background: var(--primary-accent); color: #fff; border-bottom-right-radius: 4px; }
    .chat-message.assistant .bubble { background: var(--surface); color: var(--text-primary); border-bottom-left-radius: 4px; }
    .sources-container { margin-top: 1rem; padding-top: 0.75rem; border-top: 1px solid rgba(255, 255, 255, 0.1); }
    .sources-container h4 { font-size: 0.9rem; color: var(--text-secondary); margin-bottom: 0.5rem; font-weight: 500; }
    .sources { list-style: none; padding-left: 0; font-size: 0.85rem; display: flex; flex-direction: column; gap: 0.5rem; }
    .sources li { display: flex; align-items: center; gap: 0.5rem; color: var(--text-secondary); } .source-index { font-weight: bold; color: var(--text-primary); }
    .source-file { flex-grow: 1; } .source-relevance { background-color: var(--bg-secondary); padding: 2px 6px; border-radius: var(--border-radius-sm); font-size: 0.75rem; }

    .chat-input-wrapper { width: 100%; max-width: 768px; margin: 0 auto; display: flex; flex-direction: column; gap: 8px; }
    .category-tags-container { display: flex; flex-wrap: wrap; gap: 8px; }
    .category-tag { background-color: var(--surface); color: var(--text-primary); border: 1px solid var(--border-color); border-radius: 16px; padding: 6px 12px; font-size: 14px; cursor: pointer; transition: all 0.2s ease-in-out; }
    .category-tag:hover { background-color: #4a4a4a; border-color: #6d6d6d; }
    .category-tag.selected { background-color: var(--primary-accent); color: white; border-color: var(--primary-accent-hover); }
    .chat-input-container { display: flex; align-items: flex-end; padding: 8px 8px 8px 16px; background-color: var(--bg-secondary); border-radius: 24px; border: 1px solid var(--border-color); transition: border-color 0.2s ease-in-out, box-shadow 0.2s ease-in-out; }
    .chat-input-container:focus-within { border-color: var(--border-color-focus); box-shadow: 0 0 0 2px rgba(160, 160, 160, 0.2); }
    .chat-input { flex-grow: 1; border: none; background: transparent; color: var(--text-primary); font-size: 16px; padding: 8px 0; resize: none; outline: none; max-height: 200px; overflow-y: auto; line-height: 1.5; }
    .chat-input::placeholder { color: var(--text-secondary); }
    .send-button { background: #3a3a3a; border: none; border-radius: 50%; width: 40px; height: 40px; display: flex; align-items: center; justify-content: center; cursor: pointer; transition: background-color 0.2s ease-in-out; color: #a0a0a0; margin-left: 8px; flex-shrink: 0; }
    .send-button:hover:not(:disabled) { background: #4a4a4a; color: #fff; } .send-button:disabled { cursor: not-allowed; background-color: transparent; }
    .send-icon { color: var(--primary-accent); transition: color 0.2s ease-in-out; } .send-icon.disabled { color: #6d6d6d; }

    /* Login Page */
    .login-container { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 32px; background: radial-gradient(circle at top, rgba(255,255,255,0.05), transparent); }
    .login-card { width: 100%; max-width: 400px; display: flex; flex-direction: column; gap: 16px; }
    .logo { width: 80px; height: auto; margin: 0 auto 8px; }
    .login-header { text-align: center; font-weight: 600; }
    .login-subtitle { text-align: center; color: var(--text-secondary); margin-top: -16px; margin-bottom: 16px; }
    .form-group { display: flex; flex-direction: column; gap: 8px; }
    .input-wrapper { position: relative; }
    .input-wrapper .input-icon { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); color: var(--text-secondary); pointer-events: none; }
    .input-wrapper .toggle-password { position: absolute; right: 12px; top: 50%; transform: translateY(-50%); background: none; border: none; color: var(--text-secondary); cursor: pointer; padding: 0; display: flex; align-items: center; }
    .input-wrapper input { padding-left: 40px; padding-right: 40px; }
    .remember-row { display: flex; justify-content: space-between; align-items: center; font-size: 0.9rem; }
    .login-button { width: 100%; }
    .forgot-link { color: var(--primary-accent); font-size: 0.9rem; }
    .error-banner { display: flex; align-items: center; gap: 8px; background-color: rgba(229,62,62,0.1); border: 1px solid var(--error-color); color: var(--error-color); padding: 8px 12px; border-radius: var(--border-radius-md); }
    .spinner { width: 24px; height: 24px; animation: spin 1s linear infinite; }
    @keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
`;

const GlobalStyles = () => (
  <style suppressHydrationWarning dangerouslySetInnerHTML={{ __html: globalCss }} />
);

export default GlobalStyles;

