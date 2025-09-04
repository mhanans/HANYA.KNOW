import { useState, useEffect } from 'react';
import { apiFetch } from '../lib/api';

interface Ticket {
  id: number;
  ticketNumber: string;
  complaint: string;
  detail: string;
  categoryId?: number;
  picId?: number;
  reason?: string;
}

interface Category {
  id: number;
  ticketType: string;
}

interface Pic {
  id: number;
  name: string;
}

export default function Tickets() {
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [pics, setPics] = useState<Pic[]>([]);
  const [ticketNumber, setTicketNumber] = useState('');
  const [complaint, setComplaint] = useState('');
  const [detail, setDetail] = useState('');
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const t = await apiFetch('/api/tickets');
      if (t.ok) setTickets(await t.json());
      const c = await apiFetch('/api/ticketcategories');
      if (c.ok) setCategories(await c.json());
      const p = await apiFetch('/api/pics');
      if (p.ok) setPics(await p.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const create = async () => {
    setError('');
    try {
      const res = await apiFetch('/api/tickets', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ticketNumber, complaint, detail })
      });
      if (!res.ok) throw new Error(await res.text());
      const t = await res.json();
      setTickets(prev => [t, ...prev]);
      setTicketNumber('');
      setComplaint('');
      setDetail('');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const getCategory = (id?: number) => categories.find(c => c.id === id)?.ticketType ?? 'Unassigned';
  const getPic = (id?: number) => pics.find(p => p.id === id)?.name ?? 'Unassigned';

  return (
    <div className="page-container">
      <h1>Tickets</h1>
      <div className="controls" style={{ flexDirection: 'column', alignItems: 'flex-start', gap: '8px' }}>
        <input value={ticketNumber} onChange={e => setTicketNumber(e.target.value)} placeholder="Ticket number" className="form-input" />
        <input value={complaint} onChange={e => setComplaint(e.target.value)} placeholder="Complaint" className="form-input" />
        <textarea value={detail} onChange={e => setDetail(e.target.value)} placeholder="Detail" className="form-input" />
        <button onClick={create} className="btn btn-primary">Submit</button>
      </div>
      <div className="card table-wrapper">
        <table className="table">
          <thead>
            <tr>
              <th>Number</th>
              <th>Complaint</th>
              <th>Detail</th>
              <th>Category</th>
              <th>PIC</th>
              <th>Reason</th>
            </tr>
          </thead>
          <tbody>
            {tickets.map(t => (
              <tr key={t.id}>
                <td>{t.ticketNumber}</td>
                <td>{t.complaint}</td>
                <td>{t.detail}</td>
                <td>{getCategory(t.categoryId)}</td>
                <td>{getPic(t.picId)}</td>
                <td>{t.reason ?? '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {error && <p className="error">{error}</p>}
      </div>
    </div>
  );
}
