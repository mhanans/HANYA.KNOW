import React from 'react';

export interface Source {
  index: number;
  file: string;
  page?: number;
  content: string;
  score: number;
}

export interface Message {
  role: 'user' | 'assistant';
  content: string;
  sources?: Source[];
}

export default function MessageView({ message }: { message: Message }) {
  return (
    <div className={`message ${message.role}`}>
      <div className="bubble">
        {message.content}
        {message.role === 'assistant' && message.sources && message.sources.length > 0 && (
          <ol>
            {message.sources.map((src) => (
              <li key={src.index}>
                [{src.index}] {src.file}
                {src.page ? ` (p.${src.page})` : ''} – {src.score.toFixed(2)}
              </li>
            ))}
          </ol>
        )}
      </div>
    </div>
  );
}
