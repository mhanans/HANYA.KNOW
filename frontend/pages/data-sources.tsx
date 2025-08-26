import { ReactNode, useEffect, useRef, useState } from 'react';
import { apiFetch } from '../lib/api';

interface DbForm {
  host: string;
  port: string;
  database: string;
  username: string;
  password: string;
  table: string;
}

const SendIcon = ({ disabled }: { disabled: boolean }) => (
  <svg
    width="24"
    height="24"
    viewBox="0 0 24 24"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    className={`send-icon ${disabled ? 'disabled' : ''}`}
  >
    <path d="M12 2L2 22L12 18L22 22L12 2Z" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" />
  </svg>
);

export default function DataSources() {
  const [activeTab, setActiveTab] = useState<'upload' | 'database'>('upload');

  const [excelFile, setExcelFile] = useState<File | null>(null);
  const [fileUploaded, setFileUploaded] = useState(false);
  const [uploadStatus, setUploadStatus] = useState('');
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [analyzing, setAnalyzing] = useState(false);

  const fileInput = useRef<HTMLInputElement>(null);

  const [dbForm, setDbForm] = useState<DbForm>({ host: '', port: '5432', database: '', username: '', password: '', table: '' });
  const [dbStatus, setDbStatus] = useState('');
  const [testing, setTesting] = useState(false);
  const [dbReady, setDbReady] = useState(false);

  const [showTabs, setShowTabs] = useState(true);
  const [isDataReady, setIsDataReady] = useState(false);
  const [summary, setSummary] = useState<ReactNode>('');

  const uploadExcel = async () => {
    if (!excelFile) {
      setUploadStatus('Please choose an Excel or CSV file.');
      return;
    }
    const form = new FormData();
    form.append('file', excelFile);
    setUploading(true);
    setUploadStatus('');
    setFileUploaded(false);
    const xhr = new XMLHttpRequest();
    xhr.upload.onprogress = e => {
      if (e.lengthComputable) setUploadProgress((e.loaded / e.total) * 100);
    };
    xhr.onload = () => {
      setUploading(false);
      if (xhr.status >= 200 && xhr.status < 300) {
        setUploadStatus('File uploaded successfully.');
        setFileUploaded(true);
      } else {
        setUploadStatus('Upload failed.');
      }
    };
    xhr.onerror = () => {
      setUploading(false);
      setUploadStatus('Upload error.');
    };
    xhr.open('POST', '/api/proxy/data/upload-excel');
    const token = typeof window !== 'undefined' ? localStorage.getItem('token') : null;
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    xhr.send(form);
  };

  const testConnection = async () => {
    setDbStatus('');
    setTesting(true);
    setDbReady(false);
    try {
      const res = await apiFetch('/api/data/test-connection', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dbForm)
      });
      const data = await res.json();
      if (res.ok && data.success) {
        setDbStatus('Connection Successful!');
        setDbReady(true);
      } else {
        setDbStatus(`Connection failed: ${data.message || ''}`.trim());
      }
    } catch (err) {
      setDbStatus(`Connection error: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setTesting(false);
    }
  };

  const finalizeUpload = () => {
    if (excelFile) {
      setAnalyzing(true);
      setTimeout(() => {
        setSummary(
          <span>
            <strong>{excelFile.name}</strong> uploaded successfully.
          </span>
        );
        setIsDataReady(true);
        setShowTabs(false);
        setMessages([
          {
            role: 'assistant',
            content: `Your data from ${excelFile.name} is ready. What would you like to know?`
          }
        ]);
        setAnalyzing(false);
      }, 1000);
    }
  };

  const finalizeDb = () => {
    setSummary(
      <span>
        Connected to <strong>{dbForm.table || 'table'}</strong> on {dbForm.host}
      </span>
    );
    setIsDataReady(true);
    setShowTabs(false);
    setMessages([
      {
        role: 'assistant',
        content: `Connected to ${dbForm.table || 'table'} on ${dbForm.host}. What would you like to know?`
      }
    ]);
  };

  const [messages, setMessages] = useState<{ role: 'user' | 'assistant'; content: string; error?: boolean }[]>([]);
  const [query, setQuery] = useState('');
  const [asking, setAsking] = useState(false);
  const endRef = useRef<HTMLDivElement>(null);
  const chatRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  useEffect(() => {
    if (isDataReady) {
      chatRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [isDataReady]);

  const ask = async () => {
    if (!query.trim()) return;
    setMessages(m => [...m, { role: 'user', content: query }]);
    setAsking(true);
    try {
      let res: Response;
      if (excelFile) {
        const form = new FormData();
        form.append('file', excelFile);
        form.append('query', query);
        res = await apiFetch('/api/data/chat-file', { method: 'POST', body: form });
      } else {
        const body = { ...dbForm, query };
        res = await apiFetch('/api/data/chat-db', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body)
        });
      }
      const data = await res.json();
      if (data.answer) {
        setMessages(m => [...m, { role: 'assistant', content: data.answer }]);
      } else if (data.error) {
        setMessages(m => [...m, { role: 'assistant', content: data.userMessage || 'Request failed.', error: true }]);
      } else {
        setMessages(m => [...m, { role: 'assistant', content: data.message || 'Request failed.', error: true }]);
      }
    } catch (err) {
      setMessages(m => [
        ...m,
        {
          role: 'assistant',
          content: `Error: ${err instanceof Error ? err.message : String(err)}`,
          error: true
        }
      ]);
    } finally {
      setQuery('');
      setAsking(false);
    }
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (e.dataTransfer.files?.[0]) {
      setExcelFile(e.dataTransfer.files[0]);
      setFileUploaded(false);
      setUploadStatus('');
    }
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
  };

  const removeFile = (e: React.MouseEvent) => {
    e.stopPropagation();
    setExcelFile(null);
    setFileUploaded(false);
    setUploadStatus('');
  };

  return (
    <div className="page-container">
      <div className="page-header">
        <div>
          <h1>Connect a Data Source</h1>
          <p>Upload a file or connect to a database to begin your data analysis chat.</p>
        </div>
      </div>

      {showTabs && (
        <>
          <div className="tab-container">
            <button className={`tab-button ${activeTab === 'upload' ? 'active' : ''}`} onClick={() => setActiveTab('upload')}>
              Upload File
            </button>
            <button className={`tab-button ${activeTab === 'database' ? 'active' : ''}`} onClick={() => setActiveTab('database')}>
              Connect to Database
            </button>
          </div>

          <div className="tab-content">
            {activeTab === 'upload' ? (
              <div className="card">
                <h2>Upload File</h2>
                {!fileUploaded ? (
                  <div
                    className="dropzone"
                    onDragOver={handleDragOver}
                    onDrop={handleDrop}
                    onClick={() => fileInput.current?.click()}
                  >
                    {excelFile ? (
                      <div className="file-info">
                        <span>{excelFile.name}</span>
                        <button className="remove-file" onClick={removeFile} aria-label="Remove file">
                          ✖
                        </button>
                      </div>
                    ) : (
                      <p>Drag &amp; drop your Excel file here, or click to browse</p>
                    )}
                    <input
                      type="file"
                      accept=".xlsx,.csv"
                      ref={fileInput}
                      style={{ display: 'none' }}
                      onChange={e => {
                        setExcelFile(e.target.files?.[0] || null);
                        setFileUploaded(false);
                        setUploadStatus('');
                      }}
                    />
                  </div>
                ) : (
                  <div className="upload-success">
                    <span className="success-icon">✔</span>
                    <span className="file-name">{excelFile?.name}</span>
                    <button className="remove-file" onClick={removeFile} aria-label="Remove file">
                      ✖
                    </button>
                  </div>
                )}
                {uploading && (
                  <progress className="progress-bar" max="100" value={uploadProgress}></progress>
                )}
                {uploadStatus && (
                  <p className={uploadStatus.includes('success') ? 'success' : 'error'}>{uploadStatus}</p>
                )}
                <div className="actions">
                  {fileUploaded ? (
                    <button className="btn btn-primary" onClick={finalizeUpload} disabled={analyzing}>
                      {analyzing ? (
                        <>
                          <span className="spinner"></span> Analyzing...
                        </>
                      ) : (
                        'Analyze'
                      )}
                    </button>
                  ) : (
                    <button
                      className="btn btn-primary"
                      onClick={uploadExcel}
                      disabled={uploading || !excelFile}
                    >
                      {uploading ? 'Uploading...' : 'Upload'}
                    </button>
                  )}
                </div>
              </div>
            ) : (
              <div className="card">
            <h2>Connect to Database</h2>
            <div className="form-grid">
              <label htmlFor="host">Host</label>
              <input id="host" className="form-input" value={dbForm.host} onChange={e => setDbForm({ ...dbForm, host: e.target.value })} />
              <label htmlFor="port">Port</label>
              <input id="port" className="form-input" value={dbForm.port} onChange={e => setDbForm({ ...dbForm, port: e.target.value })} />
              <label htmlFor="database">Database</label>
              <input id="database" className="form-input" value={dbForm.database} onChange={e => setDbForm({ ...dbForm, database: e.target.value })} />
              <label htmlFor="username">Username</label>
              <input id="username" className="form-input" value={dbForm.username} onChange={e => setDbForm({ ...dbForm, username: e.target.value })} />
              <label htmlFor="password">Password</label>
              <input id="password" type="password" className="form-input" value={dbForm.password} onChange={e => setDbForm({ ...dbForm, password: e.target.value })} />
              <label htmlFor="table">Table</label>
              <input id="table" className="form-input" value={dbForm.table} onChange={e => setDbForm({ ...dbForm, table: e.target.value })} />
            </div>
            <div className="actions">
              <button className="btn btn-secondary" onClick={testConnection} disabled={testing}>
                {testing ? 'Testing...' : 'Test Connection'}
              </button>
              {dbStatus && (
                <span className={dbReady ? 'success' : 'error'}>{dbStatus}</span>
              )}
            </div>
            <div className="actions">
              <button className="btn btn-primary" onClick={finalizeDb} disabled={!dbReady}>
                Chat with Data
              </button>
            </div>
          </div>
        )}
        </div>
        </>
      )}

      {isDataReady && (
        <>
          <div className="card">
            <h2>Connection Summary</h2>
            <div className="summary">
              <span className="success-icon">✔</span>
              <p>{summary}</p>
            </div>
          </div>
          <div className="card chat-with-data-section" ref={chatRef}>
            <h2>Chat with Data</h2>
            <div className="messages-container">
              {messages.map((m, i) => (
                <div key={i} className={`chat-message ${m.role === 'assistant' ? 'assistant' : 'user'} ${m.error ? 'error' : ''}`}>
                  <div className="avatar">{m.role === 'assistant' ? '🤖' : 'You'}</div>
                  <div className="bubble">{m.content}</div>
                </div>
              ))}
              <div ref={endRef}></div>
            </div>
            <div
              className="input-area"
              onKeyDown={e => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  ask();
                }
              }}
            >
              <div className="chat-input-wrapper">
                <div className="chat-input-container">
                  <textarea
                    className="chat-input"
                    placeholder="Ask a question..."
                    value={query}
                    onChange={e => setQuery(e.target.value)}
                    disabled={asking}
                  />
                  <button
                    className="send-button"
                    onClick={ask}
                    disabled={asking || !query.trim()}
                    aria-label="Send message"
                  >
                    <SendIcon disabled={asking || !query.trim()} />
                  </button>
                </div>
              </div>
            </div>
          </div>
        </>
      )}

      <style jsx>{`
        .tab-container { display: flex; border-bottom: 1px solid var(--border-color); }
        .tab-button { padding: 0.75rem 1.5rem; cursor: pointer; background: none; border: none; color: var(--text-secondary); border-bottom: 2px solid transparent; }
        .tab-button.active { color: var(--primary-accent); border-bottom-color: var(--primary-accent); }
        .tab-content { padding-top: 2rem; }
        .dropzone { border: 2px dashed var(--border-color); padding: 2rem; text-align: center; cursor: pointer; }
        .file-info { display: flex; align-items: center; justify-content: center; gap: 0.5rem; }
        .upload-success { display: flex; align-items: center; gap: 0.5rem; }
        .success-icon { color: var(--success-color); }
        .summary { display: flex; align-items: center; gap: 0.5rem; }
        .remove-file { background: none; border: none; cursor: pointer; color: var(--error-color); }
        .progress-bar { width: 100%; margin-top: 1rem; }
        .spinner { border: 2px solid var(--primary-accent); border-top-color: transparent; border-radius: 50%; width: 1rem; height: 1rem; display: inline-block; animation: spin 1s linear infinite; margin-right: 0.5rem; }
        @keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
        .chat-message.assistant.error .bubble { border: 1px solid var(--error-color); color: var(--error-color); }
      `}</style>
    </div>
  );
}

