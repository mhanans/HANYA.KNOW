import { useState } from 'react';
import { apiFetch } from '../lib/api';

interface DbForm {
  host: string;
  port: string;
  database: string;
  username: string;
  password: string;
  table: string;
}

export default function DataSources() {
  const [excelFile, setExcelFile] = useState<File | null>(null);
  const [uploadStatus, setUploadStatus] = useState('');
  const [dbForm, setDbForm] = useState<DbForm>({ host: '', port: '5432', database: '', username: '', password: '', table: '' });
  const [dbStatus, setDbStatus] = useState('');
  const [testing, setTesting] = useState(false);
  const [uploading, setUploading] = useState(false);

  const uploadExcel = async () => {
    if (!excelFile) {
      setUploadStatus('Please choose an Excel or CSV file.');
      return;
    }
    const form = new FormData();
    form.append('file', excelFile);
    setUploading(true);
    setUploadStatus('Uploading...');
    try {
      const res = await apiFetch('/api/data/upload-excel', { method: 'POST', body: form });
      if (res.ok) {
        setUploadStatus('File uploaded successfully.');
        setExcelFile(null);
      } else {
        setUploadStatus('Upload failed.');
      }
    } catch (err) {
      setUploadStatus(`Upload error: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setUploading(false);
    }
  };

  const testConnection = async () => {
    setDbStatus('');
    setTesting(true);
    try {
      const res = await apiFetch('/api/data/test-connection', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dbForm)
      });
      const data = await res.json();
      if (res.ok && data.success) {
        setDbStatus('Connection successful');
      } else {
        setDbStatus(data.message || 'Connection failed');
      }
    } catch (err) {
      setDbStatus(`Connection error: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setTesting(false);
    }
  };

  return (
    <div className="page-container">
      <h1>Data Sources</h1>
      <div className="card">
        <h2>Upload Excel</h2>
        <div className="form-grid">
          <label>File</label>
          <input type="file" accept=".xlsx,.csv" className="form-input" onChange={e => setExcelFile(e.target.files?.[0] || null)} />
        </div>
        <div className="actions">
          <button className="btn btn-primary" onClick={uploadExcel} disabled={uploading}>{uploading ? 'Uploading...' : 'Upload'}</button>
        </div>
        {uploadStatus && <p className={uploadStatus.includes('successfully') ? 'success' : 'error'}>{uploadStatus}</p>}
      </div>

      <div className="card">
        <h2>Connect to Database</h2>
        <div className="form-grid">
          <label>Host</label>
          <input className="form-input" value={dbForm.host} onChange={e => setDbForm({ ...dbForm, host: e.target.value })} />
          <label>Port</label>
          <input className="form-input" value={dbForm.port} onChange={e => setDbForm({ ...dbForm, port: e.target.value })} />
          <label>Database</label>
          <input className="form-input" value={dbForm.database} onChange={e => setDbForm({ ...dbForm, database: e.target.value })} />
          <label>Username</label>
          <input className="form-input" value={dbForm.username} onChange={e => setDbForm({ ...dbForm, username: e.target.value })} />
          <label>Password</label>
          <input type="password" className="form-input" value={dbForm.password} onChange={e => setDbForm({ ...dbForm, password: e.target.value })} />
          <label>Table</label>
          <input className="form-input" value={dbForm.table} onChange={e => setDbForm({ ...dbForm, table: e.target.value })} />
        </div>
        <div className="actions">
          <button className="btn btn-secondary" onClick={testConnection} disabled={testing}>{testing ? 'Testing...' : 'Test Connection'}</button>
        </div>
        {dbStatus && <p className={dbStatus.includes('successful') ? 'success' : 'error'}>{dbStatus}</p>}
      </div>
    </div>
  );
}

