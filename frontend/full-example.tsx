import React, { useState, useRef, useEffect } from 'react';

// ===================================================================================
// 1. ALL STYLES (CSS)
// The entire design system is embedded here for simplicity.
// ===================================================================================

const GlobalStyles = () => (
  <style>{`
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
    .form-input, .form-textarea, .form-select { width: 100%; background-color: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: var(--border-radius-md); padding: calc(var(--spacing-unit) * 1.5); color: var(--text-primary); font-size: 1rem; transition: all 0.2s ease; }
    .form-input:focus, .form-textarea:focus, .form-select:focus { outline: none; border-color: var(--border-color-focus); box-shadow: 0 0 0 3px rgba(0, 123, 255, 0.25); }
    .form-input::placeholder, .form-textarea::placeholder { color: var(--text-secondary); }
    .card { background-color: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: var(--border-radius-lg); padding: calc(var(--spacing-unit) * 3); box-shadow: var(--shadow-sm); }
    .table { width: 100%; border-collapse: collapse; margin-top: calc(var(--spacing-unit) * 2); }
    .table th, .table td { padding: calc(var(--spacing-unit) * 1.5); text-align: left; border-bottom: 1px solid var(--border-color); }
    .table th { color: var(--text-primary); font-weight: 600; } .table tbody tr:hover { background-color: var(--surface); }
    .tag { display: inline-block; background-color: var(--surface); color: var(--text-secondary); border: 1px solid var(--border-color); border-radius: var(--border-radius-full); padding: 4px 12px; font-size: 0.8rem; font-weight: 500; }

    /*
     * =================================================================
     * B. LAYOUT STYLES (appLayout.css)
     * =================================================================
     */
    .app-layout { display: flex; height: 100vh; width: 100%; background-color: var(--bg-primary); }
    .sidebar { width: 240px; flex-shrink: 0; background-color: var(--bg-secondary); border-right: 1px solid var(--border-color); padding: calc(var(--spacing-unit) * 2); display: flex; flex-direction: column; }
    .sidebar-header h2 { padding: var(--spacing-unit); } .nav-links { list-style: none; margin-top: calc(var(--spacing-unit) * 2); }
    .nav-links a { display: block; padding: calc(var(--spacing-unit) * 1.5) var(--spacing-unit); color: var(--text-secondary); text-decoration: none; border-radius: var(--border-radius-md); transition: all 0.2s ease; }
    .nav-links a:hover { background-color: var(--surface); color: var(--text-primary); }
    .nav-links a.active { background-color: var(--primary-accent); color: white; }
    .main-content { flex-grow: 1; overflow-y: auto; height: 100vh; }
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
    .page-container { padding: 32px; display: flex; flex-direction: column; gap: 32px; }
    .page-header { display: flex; justify-content: space-between; align-items: center; }
    .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 24px; }
    .stat-title { font-size: 1rem; color: var(--text-secondary); margin-bottom: 8px; } .stat-value { font-size: 2.5rem; color: var(--text-primary); font-weight: 600; line-height: 1; }
    .quick-links { display: flex; gap: 16px; flex-wrap: wrap; }

    /*
     * =================================================================
     * D. CHAT COMPONENT STYLES (chat*.css)
     * =================================================================
     */
    /* --- ChatInterface --- */
    .chat-interface { display: flex; flex-direction: column; width: 100%; height: 100%; background-color: var(--bg-primary); overflow: hidden; }
    .messages-container { flex-grow: 1; padding: 24px 24px 0 24px; overflow-y: auto; display: flex; flex-direction: column; gap: 20px; justify-content: flex-end; min-height: 0; }
    .messages-container::-webkit-scrollbar { width: 8px; } .messages-container::-webkit-scrollbar-track { background: transparent; } .messages-container::-webkit-scrollbar-thumb { background-color: #4d4d4d; border-radius: 4px; }
    .input-area { padding: 16px 24px 24px 24px; background: linear-gradient(to top, var(--bg-primary) 80%, rgba(30, 30, 30, 0)); }
    .welcome-screen { flex-grow: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; text-align: center; color: var(--text-secondary); }
    .typing-indicator { display: flex; align-items: center; gap: 5px; padding: 12px 0; }
    .typing-indicator span { height: 8px; width: 8px; background-color: #8d8d8d; border-radius: 50%; display: inline-block; animation: bounce 1.4s infinite ease-in-out both; }
    .typing-indicator span:nth-child(1) { animation-delay: -0.32s; } .typing-indicator span:nth-child(2) { animation-delay: -0.16s; }
    @keyframes bounce { 0%, 80%, 100% { transform: scale(0); } 40% { transform: scale(1.0); } }

    /* --- ChatMessage --- */
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

    /* --- ChatInput --- */
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

    .controls { display: flex; gap: 8px; flex-wrap: wrap; }
    .filters { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 16px; }
    .table-wrapper { overflow-x: auto; }
    .btn-danger { background-color: #b30e0e; color: #fff; }
    .btn-danger:hover { background-color: #930b0b; }
    .error { color: var(--error-color); }
    .success { color: #38a169; }
    .hint { color: var(--text-secondary); }
    .form-grid { display: grid; grid-template-columns: 150px 1fr; gap: 12px; }
    .actions { display: flex; gap: 8px; margin-top: 16px; }
    .tag-input { display: flex; flex-direction: column; gap: 8px; }
    .tag-input .tags { display: flex; gap: 8px; flex-wrap: wrap; }
    .tag-input .tag button { background: none; border: none; color: var(--text-secondary); margin-left: 4px; cursor: pointer; }
    .modal-backdrop { position: fixed; inset: 0; background: rgba(0,0,0,0.6); display: flex; align-items: center; justify-content: center; }
    .modal-content { background: var(--bg-secondary); border-radius: var(--border-radius-lg); width: 90%; max-width: 600px; box-shadow: var(--shadow-md); display: flex; flex-direction: column; }
    .modal-header, .modal-footer { padding: 16px; display: flex; align-items: center; justify-content: space-between; }
    .modal-header { border-bottom: 1px solid var(--border-color); }
    .modal-footer { border-top: 1px solid var(--border-color); justify-content: flex-end; }
    .modal-body { padding: 16px; }
    .close-button { background: none; border: none; color: var(--text-secondary); font-size: 1.5rem; cursor: pointer; }
    .close-button:hover { color: var(--text-primary); }
    .empty-state { text-align: center; padding: 32px; }
    .recommendation-tag { padding: 2px 6px; border-radius: 4px; font-size: 0.8rem; }
    .recommendation-top { background-color: #38a169; color: #fff; }
    .recommendation-high { background-color: #3182ce; color: #fff; }
    .recommendation-reservations { background-color: #dd6b20; color: #fff; }
    .candidate-list { display: flex; flex-direction: column; gap: 16px; }
    .candidate-card { border: 1px solid var(--border-color); border-radius: var(--border-radius-md); padding: 12px; }
    .candidate-card-header { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; }
    .candidate-name { font-weight: 600; color: var(--text-primary); }
    .candidate-summary { margin-bottom: 8px; color: var(--text-secondary); }
    .candidate-skills { display: flex; gap: 8px; flex-wrap: wrap; }
    .skill-tag { background-color: var(--surface); border-radius: var(--border-radius-full); padding: 4px 8px; font-size: 0.75rem; }
    .summary { white-space: pre-wrap; }
  `}</style>
);

// ===================================================================================
// 2. TYPE DEFINITIONS
// Centralized types for clarity.
// ===================================================================================

type Page =
  | 'Dashboard'
  | 'Chat'
  | 'Stream Chat'
  | 'Chat History'
  | 'Documents'
  | 'Document Analytics'
  | 'Upload'
  | 'Categories'
  | 'Roles'
  | 'Users'
  | 'CV Generator'
  | 'Settings';
interface Source { index: number; file: string; page?: number; relevance: number; }
interface Message { sender: 'user' | 'bot'; text: string; sources?: Source[]; }
interface NavLink { label: Page; href: string; }
interface Category { id: number; name: string; }
interface Role { id: number; name: string; allCategories: boolean; categoryIds: number[]; }
interface User { id: number; username: string; roleIds: number[]; password?: string; }
interface Recommendation { id: number; position: string; details: string; summary: string; summaryJson: string; createdAt: string; }
interface ConversationInfo { id: string; created: string; firstMessage: string; }
interface Candidate { name: string; reason: string; }

// ===================================================================================
// 3. REUSABLE UI COMPONENTS
// All the building blocks defined in one place.
// ===================================================================================

const SendIcon = ({ disabled }: { disabled: boolean }) => <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" className={`send-icon ${disabled ? 'disabled' : ''}`}><path d="M12 2L2 22L12 18L22 22L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" /></svg>;
const TypingIndicator = () => <div className="typing-indicator"><span></span><span></span><span></span></div>;

const ChatMessage: React.FC<{ message: Message, botAvatar?: string, userAvatar?: string, children?: React.ReactNode }> = ({ message, botAvatar, userAvatar, children }) => {
  const isBot = message.sender === 'bot';
  const role = isBot ? 'assistant' : 'user';
  const avatarUrl = isBot ? botAvatar : userAvatar;
  return (
    <div className={`chat-message ${role}`}>
      {avatarUrl ? <img src={avatarUrl} alt={`${role} avatar`} className="avatar" /> : <div className="avatar" />}
      <div className="bubble">
        {children || <>
          {message.text}
          {isBot && message.sources && message.sources.length > 0 && (
            <div className="sources-container">
              <h4>Sources:</h4>
              <ul className="sources">
                {message.sources.map(s => (
                  <li key={s.index}>
                    <span className="source-index">[{s.index}]</span>
                    <span className="source-file">{s.file}{s.page ? ` (p.${s.page})` : ''}</span>
                    <span className="source-relevance">Relevance: {(s.relevance * 100).toFixed(0)}%</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </>}
      </div>
    </div>
  );
};

const ChatInput: React.FC<{ query: string; setQuery: (q: string) => void; onSendMessage: () => void; }> = ({ query, setQuery, onSendMessage }) => {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  useEffect(() => { if (textareaRef.current) { textareaRef.current.style.height = 'auto'; textareaRef.current.style.height = `${textareaRef.current.scrollHeight}px`; } }, [query]);
  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); onSendMessage(); } };
  return (
    <div className="chat-input-wrapper">
      <div className="category-tags-container">
        <button className="category-tag selected">General</button>
        <button className="category-tag">Finance</button>
        <button className="category-tag">CV</button>
      </div>
      <div className="chat-input-container">
        <textarea ref={textareaRef} className="chat-input" placeholder="Send a message..." value={query} onChange={e => setQuery(e.target.value)} onKeyDown={handleKeyDown} rows={1} />
        <button className="send-button" onClick={onSendMessage} disabled={!query.trim()} aria-label="Send message"><SendIcon disabled={!query.trim()} /></button>
      </div>
    </div>
  );
};

const TagInput: React.FC<{ options: Category[]; selected: number[]; onChange: (ids: number[]) => void; }> = ({ options, selected, onChange }) => {
  const available = options.filter(o => !selected.includes(o.id));
  return (
    <div className="tag-input">
      <div className="tags">
        {selected.map(id => {
          const opt = options.find(o => o.id === id);
          return (
            <span className="tag" key={id}>
              {opt?.name}
              <button type="button" onClick={() => onChange(selected.filter(x => x !== id))}>×</button>
            </span>
          );
        })}
      </div>
      {available.length > 0 && (
        <select className="form-select" value="" onChange={e => { const val = Number(e.target.value); if (val) onChange([...selected, val]); }}>
          <option value="">Select...</option>
          {available.map(o => (<option key={o.id} value={o.id}>{o.name}</option>))}
        </select>
      )}
    </div>
  );
};

const Modal: React.FC<{ isOpen: boolean; onClose: () => void; title: string; children: React.ReactNode }> = ({ isOpen, onClose, title, children }) => {
  if (!isOpen) return null;
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-content" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h2>{title}</h2>
          <button className="close-button" onClick={onClose} aria-label="Close">&times;</button>
        </div>
        <div className="modal-body">{children}</div>
        <div className="modal-footer">
          <button className="btn btn-secondary" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
};

// ===================================================================================
// 4. PAGE COMPONENTS
// Each feature page is a self-contained component.
// ===================================================================================

const DashboardPage = () => (
  <div className="page-container">
    <h1>Dashboard</h1>
    <div className="stats-grid">
      <div className="card"><h3 className="stat-title">Total Chats</h3><p className="stat-value">1,204</p></div>
      <div className="card"><h3 className="stat-title">Documents Indexed</h3><p className="stat-value">89</p></div>
      <div className="card"><h3 className="stat-title">Categories</h3><p className="stat-value">12</p></div>
      <div className="card"><h3 className="stat-title">Active Users</h3><p className="stat-value">4</p></div>
    </div>
    <div className="card">
      <h2>Quick Links</h2>
      <div className="quick-links">
        <button className="btn btn-primary">Start New Chat</button>
        <button className="btn btn-secondary">Upload Document</button>
        <button className="btn btn-secondary">Manage Roles</button>
      </div>
    </div>
  </div>
);

const ChatInterface = () => {
    const [messages, setMessages] = useState<Message[]>([
        { sender: 'user', text: 'What were the key findings in the 2023 annual report?' },
        { sender: 'bot', text: 'The key findings were a 15% increase in revenue, driven by new market expansion [1].', sources: [{ index: 1, file: 'Annual_Report_2023.pdf', page: 4, relevance: 0.92 }] },
    ]);
    const [query, setQuery] = useState('');
    const messagesEndRef = useRef<HTMLDivElement>(null);
    useEffect(() => { messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [messages]);

    const handleSendMessage = () => {
        if (!query.trim()) return;
        setMessages(prev => [...prev, { sender: 'user', text: query }]);
        setQuery('');
        // Here you would add logic to get a real bot response
    };

    return (
        <div className="chat-interface">
            <div className="messages-container">
                {messages.map((msg, index) => <ChatMessage key={index} message={msg} userAvatar=" " botAvatar=" " />)}
                <div ref={messagesEndRef} />
            </div>
            <div className="input-area"><ChatInput query={query} setQuery={setQuery} onSendMessage={handleSendMessage} /></div>
        </div>
    );
};

const DocumentsPage = () => (
  <div className="page-container">
    <div className="page-header"><h1>Documents</h1><button className="btn btn-primary">Upload PDF</button></div>
    <div className="card">
      <table className="table">
        <thead><tr><th>File Name</th><th>Categories</th><th>Status</th><th>Actions</th></tr></thead>
        <tbody>
          <tr><td>Annual_Report_2023.pdf</td><td><span className="tag">Finance</span><span className="tag">Reports</span></td><td>Analyzed</td><td><button className="btn btn-secondary">Delete</button></td></tr>
          <tr><td>CV_JohnDoe.pdf</td><td><span className="tag">CV</span><span className="tag">Recruitment</span></td><td>Analyzed</td><td><button className="btn btn-secondary">Delete</button></td></tr>
        </tbody>
      </table>
    </div>
  </div>
);

const UploadPage = () => {
  const [status, setStatus] = useState('');
  return (
    <div className="page-container">
      <h1>Upload Document</h1>
      <div className="card">
        <p className="hint">Upload new PDF documents.</p>
        <div className="form-grid">
          <label>Files</label>
          <input className="form-input" type="file" multiple />
          <label>Title</label>
          <input className="form-input" placeholder="Document title (optional)" />
          <label>Text</label>
          <textarea className="form-textarea" placeholder="Text content (optional)" />
          <label>Category</label>
          <select className="form-select"><option>No category</option><option>Finance</option></select>
        </div>
        <div className="actions">
          <button className="btn btn-primary" onClick={() => setStatus('Upload successful')}>Upload</button>
        </div>
        {status && <p className="success">{status}</p>}
      </div>
    </div>
  );
};

const CategoriesPage = () => {
  const [categories, setCategories] = useState<Category[]>([
    { id: 1, name: 'Finance' },
    { id: 2, name: 'CV' },
  ]);
  const [name, setName] = useState('');
  const add = () => {
    if (!name.trim()) return;
    setCategories(prev => [...prev, { id: Date.now(), name }]);
    setName('');
  };
  return (
    <div className="page-container">
      <h1>Manage Categories</h1>
      <div className="controls">
        <input className="form-input" value={name} onChange={e => setName(e.target.value)} placeholder="New category" />
        <button className="btn btn-primary" onClick={add}>Add</button>
      </div>
      <div className="card table-wrapper">
        <table className="table">
          <thead><tr><th>Name</th><th>Actions</th></tr></thead>
          <tbody>
            {categories.map(c => (
              <tr key={c.id}>
                <td><input className="form-input" value={c.name} onChange={e => setCategories(prev => prev.map(p => p.id === c.id ? { ...p, name: e.target.value } : p))} /></td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button className="btn btn-primary">Save</button>
                  <button className="btn btn-danger" onClick={() => setCategories(prev => prev.filter(p => p.id !== c.id))}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

const RolesPage = () => {
  const categoryOptions: Category[] = [
    { id: 1, name: 'Finance' },
    { id: 2, name: 'CV' },
  ];
  const [roles, setRoles] = useState<Role[]>([
    { id: 1, name: 'Admin', allCategories: true, categoryIds: [] },
  ]);
  const [newRole, setNewRole] = useState<Role>({ id: 0, name: '', allCategories: true, categoryIds: [] });
  const add = () => {
    if (!newRole.name.trim()) return;
    setRoles(prev => [...prev, { ...newRole, id: Date.now() }]);
    setNewRole({ id: 0, name: '', allCategories: true, categoryIds: [] });
  };
  return (
    <div className="page-container">
      <h1>Manage Role to Category</h1>
      <div className="controls">
        <input className="form-input" value={newRole.name} onChange={e => setNewRole({ ...newRole, name: e.target.value })} placeholder="New role" />
        <label className="form-input" style={{ display: 'flex', alignItems: 'center', gap: '4px', background: 'transparent', border: 'none', padding: 0 }}>
          <input type="checkbox" checked={newRole.allCategories} onChange={e => setNewRole({ ...newRole, allCategories: e.target.checked })} /> All categories
        </label>
        {!newRole.allCategories && (
          <TagInput options={categoryOptions} selected={newRole.categoryIds} onChange={ids => setNewRole({ ...newRole, categoryIds: ids })} />
        )}
        <button className="btn btn-primary" onClick={add}>Add</button>
      </div>
      <div className="card table-wrapper">
        <table className="table">
          <thead><tr><th>Name</th><th>All</th><th>Categories</th><th>Actions</th></tr></thead>
          <tbody>
            {roles.map(r => (
              <tr key={r.id}>
                <td><input className="form-input" value={r.name} onChange={e => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, name: e.target.value } : p))} /></td>
                <td><input type="checkbox" checked={r.allCategories} onChange={e => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, allCategories: e.target.checked } : p))} /></td>
                <td>{!r.allCategories && <TagInput options={categoryOptions} selected={r.categoryIds} onChange={ids => setRoles(prev => prev.map(p => p.id === r.id ? { ...p, categoryIds: ids } : p))} />}</td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button className="btn btn-primary">Save</button>
                  <button className="btn btn-danger" onClick={() => setRoles(prev => prev.filter(p => p.id !== r.id))}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

const UsersPage = () => {
  const roleOptions: Role[] = [
    { id: 1, name: 'Admin', allCategories: true, categoryIds: [] },
    { id: 2, name: 'User', allCategories: true, categoryIds: [] },
  ];
  const [users, setUsers] = useState<User[]>([
    { id: 1, username: 'admin', roleIds: [1], password: '' },
  ]);
  const [newUser, setNewUser] = useState<User>({ id: 0, username: '', roleIds: [], password: '' });
  const add = () => {
    if (!newUser.username.trim()) return;
    setUsers(prev => [...prev, { ...newUser, id: Date.now() }]);
    setNewUser({ id: 0, username: '', roleIds: [], password: '' });
  };
  return (
    <div className="page-container">
      <h1>Manage Users</h1>
      <div className="controls">
        <input className="form-input" value={newUser.username} onChange={e => setNewUser({ ...newUser, username: e.target.value })} placeholder="Username" />
        <input type="password" className="form-input" value={newUser.password} onChange={e => setNewUser({ ...newUser, password: e.target.value })} placeholder="Password" />
        <TagInput options={roleOptions} selected={newUser.roleIds} onChange={ids => setNewUser({ ...newUser, roleIds: ids })} />
        <button className="btn btn-primary" onClick={add}>Add</button>
      </div>
      <div className="card table-wrapper">
        <table className="table">
          <thead><tr><th>Username</th><th>Password</th><th>Roles</th><th>Actions</th></tr></thead>
          <tbody>
            {users.map(u => (
              <tr key={u.id}>
                <td><input className="form-input" value={u.username} onChange={e => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, username: e.target.value } : p))} /></td>
                <td><input type="password" className="form-input" value={u.password ?? ''} onChange={e => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, password: e.target.value } : p))} /></td>
                <td><TagInput options={roleOptions} selected={u.roleIds} onChange={ids => setUsers(prev => prev.map(p => p.id === u.id ? { ...p, roleIds: ids } : p))} /></td>
                <td style={{ display: 'flex', gap: '8px' }}>
                  <button className="btn btn-primary">Save</button>
                  <button className="btn btn-danger" onClick={() => setUsers(prev => prev.filter(p => p.id !== u.id))}>Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

const CVPage = () => {
  const [position, setPosition] = useState('');
  const [details, setDetails] = useState('');
  const [recs, setRecs] = useState<Recommendation[]>([
    { id: 1, position: 'Frontend Developer', details: 'React experience', summary: '', summaryJson: '[{"name":"Alice","reason":"Top Candidate - Strong React skills"}]', createdAt: new Date().toISOString() },
  ]);
  const [modalRec, setModalRec] = useState<Recommendation | null>(null);
  const generate = () => {
    if (!position || !details) return;
    setRecs(prev => [...prev, { id: Date.now(), position, details, summary: '', summaryJson: '[]', createdAt: new Date().toISOString() }]);
    setPosition('');
    setDetails('');
  };
  const parseReason = (reason: string) => {
    const levelMatch = reason.match(/( Top Candidate| Highly Recommended| Recommended with Reservations)/i);
    const level = levelMatch ? levelMatch[1] : '';
    let remaining = levelMatch ? reason.replace(levelMatch[0], '').trim() : reason.trim();
    const skillsMatch = remaining.match(/(?:Skills?|Tech(?: Stack|nologies)?)[:\-]\s*([A-Za-z0-9+,\s]+)/i);
    let skills: string[] = [];
    if (skillsMatch) {
      skills = skillsMatch[1].split(/,\s*/).filter(Boolean);
      remaining = remaining.replace(skillsMatch[0], '').trim();
    }
    return { level, summary: remaining, skills };
  };
  const levelClass = (level: string) => {
    const l = level.toLowerCase();
    if (l.includes('top')) return 'recommendation-top';
    if (l.includes('high')) return 'recommendation-high';
    if (l.includes('reservation')) return 'recommendation-reservations';
    return '';
  };
  const candidates: Candidate[] = modalRec ? (() => {
    try { return JSON.parse(modalRec.summaryJson); } catch { return []; }
  })() : [];
  return (
    <div className="page-container">
      <h1>Job Vacancy Analysis</h1>
      <div className="card">
        <p className="hint">Enter position details to get top candidates from uploaded CVs.</p>
        <div className="form-grid">
          <label>Position Name</label>
          <input className="form-input" value={position} onChange={e => setPosition(e.target.value)} />
          <label>Position Details</label>
          <textarea className="form-textarea" value={details} onChange={e => setDetails(e.target.value)} />
        </div>
        <div className="actions">
          <button className="btn btn-primary" onClick={generate}>Generate</button>
        </div>
      </div>
      <h2>Assessments</h2>
      <div className="card table-wrapper">
        <table className="table">
          <thead><tr><th>Position</th><th>Details</th><th>Generated</th><th>Actions</th></tr></thead>
          <tbody>{recs.map(r => (
            <tr key={r.id}>
              <td>{r.position}</td>
              <td>{r.details}</td>
              <td>{new Date(r.createdAt).toLocaleString()}</td>
              <td style={{ display: 'flex', gap: '8px' }}>
                <button className="btn btn-secondary" onClick={() => setModalRec(r)}>View Result</button>
              </td>
            </tr>
          ))}</tbody>
        </table>
      </div>
      <Modal isOpen={!!modalRec} onClose={() => setModalRec(null)} title="CV Recommendation">
        {candidates.length ? (
          <div className="candidate-list">
            {candidates.map((c, i) => {
              const { level, summary, skills } = parseReason(c.reason);
              return (
                <div key={i} className="candidate-card">
                  <div className="candidate-card-header">
                    <span className="candidate-name">{i + 1}. {c.name}</span>
                    {level && <span className={`recommendation-tag ${levelClass(level)}`}>{level}</span>}
                  </div>
                  {summary && <p className="candidate-summary">{summary}</p>}
                  {skills.length > 0 && (
                    <div className="candidate-skills">
                      {skills.map(skill => (<span key={skill} className="skill-tag">{skill}</span>))}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        ) : (
          <p>No structured summary available.</p>
        )}
      </Modal>
    </div>
  );
};

const ChatHistoryPage = () => {
  const history: ConversationInfo[] = [
    { id: '1', created: new Date().toISOString(), firstMessage: 'Hello' },
  ];
  const [messages, setMessages] = useState<string[]>([]);
  const view = () => setMessages(['user: Hello', 'assistant: Hi there!']);
  return (
    <div className="page-container">
      <h1>Chat History</h1>
      {history.length ? (
        <div className="card table-wrapper">
          <table className="table">
            <thead><tr><th>Date</th><th>First Message</th><th>Actions</th></tr></thead>
            <tbody>{history.map(h => (
              <tr key={h.id}>
                <td>{new Date(h.created).toLocaleString()}</td>
                <td>{h.firstMessage}</td>
                <td style={{ display: 'flex', gap: '8px' }}><button className="btn btn-secondary" onClick={view}>View</button></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      ) : (
        <div className="card empty-state"><p>You have no chat history yet.</p></div>
      )}
      {messages.length > 0 && (
        <div className="card">{messages.map((m, i) => <p key={i}>{m}</p>)}</div>
      )}
    </div>
  );
};

const DocumentAnalyticsPage = () => {
  const docs = [{ source: 'Annual_Report_2023.pdf' }];
  const [summary, setSummary] = useState<string | null>(null);
  return (
    <div className="page-container">
      <h1>Document Analytics</h1>
      <div className="card table-wrapper">
        <table className="table">
          <thead><tr><th>Document</th><th>Actions</th></tr></thead>
          <tbody>{docs.map(d => (
            <tr key={d.source}>
              <td>{d.source}</td>
              <td style={{ display: 'flex', gap: '8px' }}><button className="btn btn-secondary" onClick={() => setSummary('This document discusses revenue growth.')}>View Summary</button></td>
            </tr>
          ))}</tbody>
        </table>
      </div>
      <Modal isOpen={summary !== null} onClose={() => setSummary(null)} title="Document Summary">
        <pre className="summary">{summary}</pre>
      </Modal>
    </div>
  );
};

const SettingsPage = () => (
    <div className="page-container">
        <h1>General Settings</h1>
        <div className="card" style={{ maxWidth: '600px' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                <div><label style={{ display: 'block', marginBottom: '8px' }}>Application Name</label><input type="text" defaultValue="HANYA.KNOW" className="form-input" /></div>
                <div><label style={{ display: 'block', marginBottom: '8px' }}>Application Logo</label><input type="file" className="form-file" /></div>
                <button className="btn btn-primary" style={{ alignSelf: 'flex-start' }}>Save Changes</button>
            </div>
        </div>
    </div>
);


// ===================================================================================
// 5. THE MAIN LAYOUT & APPLICATION SHELL
// This orchestrates the navigation and displays the correct page.
// ===================================================================================

const AppLayout: React.FC<{ navLinks: NavLink[]; activePage: Page; onNavClick: (page: Page) => void; children: React.ReactNode; }> = 
  ({ navLinks, activePage, onNavClick, children }) => {
  return (
    <div className="app-layout">
      <nav className="sidebar">
        <div>
          <div className="sidebar-header"><h2>HANYA.KNOW</h2></div>
          <ul className="nav-links">
            {navLinks.map(link => (
              <li key={link.label}>
                <a href={link.href} className={activePage === link.label ? 'active' : ''} onClick={(e) => { e.preventDefault(); onNavClick(link.label); }}>
                  {link.label}
                </a>
              </li>
            ))}
          </ul>
        </div>
        <div className="user-profile">
          <div className="avatar"></div>
          <div className="user-info"><span className="user-name">Admin User</span><span className="user-role">Administrator</span></div>
          <button className="btn-logout" title="Logout"><svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path><polyline points="16 17 21 12 16 7"></polyline><line x1="21" y1="12" x2="9" y2="12"></line></svg></button>
        </div>
      </nav>
      <main className="main-content">{children}</main>
    </div>
  );
};

// ===================================================================================
// 6. FINAL EXPORTED COMPONENT
// This is the single component you will render in your app or storybook.
// ===================================================================================
export const FullApplicationPage = () => {
  const [activePage, setActivePage] = useState<Page>('Dashboard');

  const adminLinks: NavLink[] = [
    { href: '#', label: 'Dashboard' },
    { href: '#', label: 'Chat' },
    { href: '#', label: 'Stream Chat' },
    { href: '#', label: 'Chat History' },
    { href: '#', label: 'Documents' },
    { href: '#', label: 'Document Analytics' },
    { href: '#', label: 'Upload' },
    { href: '#', label: 'Categories' },
    { href: '#', label: 'Roles' },
    { href: '#', label: 'Users' },
    { href: '#', label: 'CV Generator' },
    { href: '#', label: 'Settings' },
  ];
  
  const renderPage = () => {
    switch (activePage) {
      case 'Dashboard': return <DashboardPage />;
      case 'Chat': return <ChatInterface />;
      case 'Stream Chat': return <ChatInterface />;
      case 'Chat History': return <ChatHistoryPage />;
      case 'Documents': return <DocumentsPage />;
      case 'Document Analytics': return <DocumentAnalyticsPage />;
      case 'Upload': return <UploadPage />;
      case 'Categories': return <CategoriesPage />;
      case 'Roles': return <RolesPage />;
      case 'Users': return <UsersPage />;
      case 'CV Generator': return <CVPage />;
      case 'Settings': return <SettingsPage />;
      default: return <DashboardPage />;
    }
  };

  return (
    <>
      <GlobalStyles />
      <AppLayout navLinks={adminLinks} activePage={activePage} onNavClick={setActivePage}>
        {renderPage()}
      </AppLayout>
    </>
  );
};

export default FullApplicationPage;