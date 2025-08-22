import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface ConversationInfo {
  id: string;
  created: string;
  firstMessage: string;
}

export default function ChatHistory() {
  const [history, setHistory] = useState<ConversationInfo[]>([]);
  const [messages, setMessages] = useState<string[]>([]);

  const load = async () => {
    try {
      const res = await apiFetch('/api/chat/history');
      if (res.ok) setHistory(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => { load(); }, []);

  const view = async (id: string) => {
    try {
      const res = await apiFetch(`/api/chat/history/${id}`);
      if (res.ok) {
        const msgs = await res.json();
        setMessages(msgs.map((m: any) => `${m.role}: ${m.content}`));
      }
    } catch {
      /* ignore */
    }
  };

  return (
    <div className="page-container">
      <div className="card">
        <h1>Chat History</h1>
        <div style={{ overflowX: 'auto', marginBottom: '1rem' }}>
          <table className="table">
            <thead>
              <tr>
                <th>Date</th>
                <th>First Message</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {history.map(h => (
                <tr key={h.id}>
                  <td>{new Date(h.created).toLocaleString()}</td>
                  <td>{h.firstMessage}</td>
                  <td style={{ display: 'flex', gap: '8px' }}>
                    <button className="btn btn-secondary" onClick={() => view(h.id)}>View</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {messages.length > 0 && (
          <div className="card">
            {messages.map((m, idx) => <p key={idx}>{m}</p>)}
          </div>
        )}
      </div>
    </div>
  );
}
