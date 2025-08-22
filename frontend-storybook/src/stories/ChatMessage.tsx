// src/stories/ChatMessage.tsx
import React from 'react';

export interface ChatMessageProps {
  // We'll allow 'bot' as an alias for 'assistant' for easier integration
  role: 'user' | 'assistant' | 'bot'; 
  content: string;
  sources?: { index: number; file: string; page?: number }[];
  avatarUrl?: string; // <-- ADDED: For displaying an image avatar
  children?: React.ReactNode; // <-- ADDED: To allow passing custom content like a spinner
}

export const ChatMessage: React.FC<ChatMessageProps> = ({
  role,
  content,
  sources,
  avatarUrl,
  children,
}) => {
  // Handle 'bot' role to apply the correct 'assistant' styling
  const messageRole = role === 'bot' ? 'assistant' : role;

  return (
    <div className={`chat-message ${messageRole}`}>
      {/* Conditionally render an img tag or the placeholder div */}
      {avatarUrl ? (
        <img src={avatarUrl} alt={`${messageRole} avatar`} className="avatar" />
      ) : (
        <div className="avatar" />
      )}
      
      <div className="bubble">
        {/* If children are provided, render them. Otherwise, render content. */}
        {children ? (
          children
        ) : (
          <>
            {content}
            {messageRole === 'assistant' && sources && (
              <ul className="sources">
                {sources.map(s => (
                  <li key={s.index}>
                    [{s.index}] {s.file}
                    {s.page !== undefined && ` (p.${s.page})`}
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </div>
    </div>
  );
};