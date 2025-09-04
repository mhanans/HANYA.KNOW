import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import Modal from '../components/Modal';

interface Pic {
  id: number;
  name: string;
  availability: boolean;
  categoryIds: number[];
  ticketCount: number;
}

interface Category {
  id: number;
  ticketType: string;
}

interface Ticket {
  id: number;
  ticketNumber: string;
  complaint: string;
}

export default function PicSummary() {
  const [pics, setPics] = useState<Pic[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedPic, setSelectedPic] = useState<Pic | null>(null);
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [modalOpen, setModalOpen] = useState(false);

  const load = async () => {
    try {
      const [p, c] = await Promise.all([
        apiFetch('/api/pics'),
        apiFetch('/api/ticketcategories')
      ]);
      if (p.ok) setPics(await p.json());
      if (c.ok) setCategories(await c.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const showTickets = async (pic: Pic) => {
    try {
      const res = await apiFetch(`/api/pics/${pic.id}/tickets`);
      if (res.ok) {
        setTickets(await res.json());
        setSelectedPic(pic);
        setModalOpen(true);
      }
    } catch {
      /* ignore */
    }
  };

  const categoryNames = (ids: number[]) => ids.map(id => categories.find(c => c.id === id)?.ticketType ?? String(id));

  return (
    <div className="page-container">
      <h1>PIC Summary</h1>
      <div className="card table-wrapper">
        <table className="table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Categories</th>
              <th>Available</th>
              <th>Tickets</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {pics.map(p => (
              <tr key={p.id}>
                <td>{p.name}</td>
                <td>
                  <div className="tags">
                    {categoryNames(p.categoryIds).map(n => (
                      <span className="tag" key={n}>{n}</span>
                    ))}
                  </div>
                </td>
                <td>{p.availability ? 'Yes' : 'No'}</td>
                <td>{p.ticketCount}</td>
                <td><button className="btn btn-secondary" onClick={() => showTickets(p)}>View Tickets</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Modal isOpen={modalOpen} onClose={() => setModalOpen(false)} title={`Tickets for ${selectedPic?.name ?? ''}`}>
        <table className="table">
          <thead>
            <tr>
              <th>Number</th>
              <th>Complaint</th>
            </tr>
          </thead>
          <tbody>
            {tickets.map(t => (
              <tr key={t.id}>
                <td>{t.ticketNumber}</td>
                <td>{t.complaint}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Modal>
    </div>
  );
}
