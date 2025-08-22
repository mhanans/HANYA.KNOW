import { useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';
import { DataTable } from '../stories/DataTable';
import { Modal } from '../stories/Modal';

interface ConversationInfo {
  id: string;
  created: string;
  firstMessage: string;
}

interface ChatMessage {
  role: string;
  content: string;
}

export default function ChatHistory() {
  const [history, setHistory] = useState<ConversationInfo[]>([]);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [open, setOpen] = useState(false);

  const load = async () => {
    try {
      const res = await apiFetch('/api/chat/history');
      if (res.ok) setHistory(await res.json());
    } catch {
      /* ignore */
    }
  };

  useEffect(() => {
    load();
  }, []);

  const view = async (id: string) => {
    try {
      const res = await apiFetch(`/api/chat/history/${id}`);
      if (res.ok) {
        setMessages(await res.json());
        setOpen(true);
      }
    } catch {
      /* ignore */
    }
  };

  const columns = [
    {
      header: 'Date',
      accessor: 'created',
      render: (row: ConversationInfo) => new Date(row.created).toLocaleString()
    },
    { header: 'First Message', accessor: 'firstMessage' },
    {
      header: 'Actions',
      accessor: 'id',
      render: (row: ConversationInfo) => (
        <button onClick={() => view(row.id)}>View</button>
      )
    }
  ];

  return (
    <div className="card">
      <h1>Chat History</h1>
      <DataTable columns={columns} data={history} />
      <Modal open={open} onClose={() => setOpen(false)} title="Conversation">
        {messages.map((m, i) => (
          <p key={i}>
            {m.role}: {m.content}
          </p>
        ))}
      </Modal>
    </div>
  );
}

