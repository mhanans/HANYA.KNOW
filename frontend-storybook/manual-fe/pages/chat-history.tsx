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
        const msgs: { role: string; content: string }[] = await res.json();
        setMessages(msgs.map((m) => `${m.role}: ${m.content}`));
      }
    } catch {
      /* ignore */
    }
  };

  return (
    <div className="card chat-card">
      <h1>Chat History</h1>
      <div className="table-wrapper">
        <table className="chat-table">
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
                <td className="name">{h.firstMessage}</td>
                <td className="actions"><button onClick={() => view(h.id)}>View</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {messages.length > 0 && (
        <div className="messages">
          {messages.map((m, idx) => <p key={idx}>{m}</p>)}
        </div>
      )}
      <style jsx>{`
        .chat-card { max-width: none; }
        .table-wrapper { overflow-x: auto; margin-bottom: 1rem; }
        .chat-table { width: 100%; border-collapse: collapse; }
        .chat-table th, .chat-table td { padding: 0.5rem; text-align: left; border-top: 1px solid #ddd; }
        .chat-table thead { background: #e0e7ff; font-weight: 600; }
        .chat-table .actions { display: flex; gap: 0.5rem; }
        .chat-table .name { word-break: break-word; }
      `}</style>
    </div>
  );
}
