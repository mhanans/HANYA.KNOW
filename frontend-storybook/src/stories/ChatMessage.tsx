import React from 'react';
import './chatMessage.css';

export interface ChatMessageProps {
  message: {
    text: string;
    sender: 'user' | 'bot';
  };
  botAvatar?: string;
}

export const ChatMessage = ({ message, botAvatar }: ChatMessageProps) => {
  const { text, sender } = message;
  const isUser = sender === 'user';

  return (
    <div className={`chat-message ${isUser ? 'user-message' : 'bot-message'}`}>
      {!isUser && (
        <div className="avatar">
          {botAvatar ? <img src={botAvatar} alt="Bot Avatar" /> : '🤖'}
        </div>
      )}
      <div className="message-bubble">{text}</div>
    </div>
  );
};
