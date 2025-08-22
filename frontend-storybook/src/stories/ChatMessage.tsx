import React from 'react';

export interface ChatMessageProps {
  role: 'user' | 'assistant';
  content: string;
  sources?: { index: number; file: string; page?: number }[];
}

export const ChatMessage: React.FC<ChatMessageProps> = ({ role, content, sources }) => {
  return (
    <div className={`chat-message ${role}`}>
      <div className="avatar" />
      <div className="bubble">
        {content}
        {role === 'assistant' && sources && (
          <ul className="sources">
            {sources.map(s => (
              <li key={s.index}>
                [{s.index}] {s.file}
                {s.page !== undefined && ` (p.${s.page})`}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
};