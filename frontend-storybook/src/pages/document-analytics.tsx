import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import { DataTable } from '../stories/DataTable';
import { Modal } from '../stories/Modal';

interface Doc { source: string; }

export default function DocumentAnalytics() {
  const [docs, setDocs] = useState<Doc[]>([]);
  const [summary, setSummary] = useState('');
  const [open, setOpen] = useState(false);

  const load = async () => {
    try {
      const res = await apiFetch('/api/documents');
      if (res.ok) setDocs(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => {
    load();
  }, []);

  const analyze = async (source: string) => {
    setSummary('Loading...');
    setOpen(true);
    try {
      const res = await apiFetch(`/api/documents/summary?source=${encodeURIComponent(source)}`);
      if (res.ok) {
        const data = await res.json();
        setSummary(data.summary || 'No summary available');
      } else {
        setSummary('Failed to load summary');
      }
    } catch {
      setSummary('Failed to load summary');
    }
  };

  const columns = [
    { header: 'Document', accessor: 'source' },
    {
      header: 'Actions',
      accessor: 'source',
      render: (row: Doc) => (
        <button onClick={() => analyze(row.source)}>View Summary</button>
      )
    }
  ];

  return (
    <div className="card">
      <h1>Document Analytics</h1>
      <DataTable columns={columns} data={docs} />
      <Modal open={open} onClose={() => setOpen(false)} title="Summary">
        <pre>{summary}</pre>
      </Modal>
    </div>
  );
}

