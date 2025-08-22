import React from 'react';
import { Button } from './Button';
import './chatInput.css';

export interface ChatInputProps {
  query: string;
  setQuery: (query: string) => void;
  send: () => void;
  loading: boolean;
  categories: { id: number; name: string }[];
  selected: number[];
  setSelected: (selected: number[]) => void;
  error?: string;
}

export const ChatInput: React.FC<ChatInputProps> = ({
  query,
  setQuery,
  send,
  loading,
  categories,
  selected,
  setSelected,
  error,
}) => {
  return (
    <div className="chat-input" onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }}}>
      {error && <p className="error">{error}</p>}
      <textarea
        placeholder="Send a message..."
        value={query}
        onChange={e => setQuery(e.target.value)}
        disabled={loading}
      />
      <div className="actions">
        <select multiple value={selected.map(String)} onChange={e => {
          const opts = Array.from(e.target.selectedOptions).map(o => parseInt(o.value));
          setSelected(opts);
        }}>
          {categories.map(c => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
        <Button onClick={send} disabled={loading || !query.trim()} label="Send" />
      </div>
    </div>
  );
};
