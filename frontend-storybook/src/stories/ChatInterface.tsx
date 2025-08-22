import React, { useState, useEffect, useRef } from 'react';
import { ChatMessage } from './ChatMessage';
import { ChatInput } from './ChatInput';

interface Message {
  text: string;
  sender: 'user' | 'bot';
}

export interface ChatInterfaceProps {
  initialMessages?: Message[];
  botAvatar?: string;
  categories?: { id: number; name: string }[];
}

export const ChatInterface = ({
  initialMessages = [],
  botAvatar,
  categories = [
    { id: 1, name: 'General' },
    { id: 2, name: 'Support' },
    { id: 3, name: 'Fun' },
  ],
}: ChatInterfaceProps) => {
  const [messages, setMessages] = useState<Message[]>(initialMessages);
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<number[]>([]);
  const [loading, setLoading] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const send = () => {
    if (!query.trim()) return;
    const newUserMessage: Message = { text: query, sender: 'user' };
    setMessages(prev => [...prev, newUserMessage]);
    setQuery('');
    setLoading(true);

    // Simulate a bot response
    setTimeout(() => {
      const botResponse: Message = {
        text: `This is a simulated response to "${newUserMessage.text}"`,
        sender: 'bot',
      };
      setMessages(prev => [...prev, botResponse]);
      setLoading(false);
    }, 1000);
  };

  return (
    <div className="chat-interface">
      <div className="messages-container">
        {messages.map((msg, index) => (
          <ChatMessage key={index} message={msg} botAvatar={botAvatar} />
        ))}
        <div ref={messagesEndRef} />
      </div>
      <div className="input-container">
        <ChatInput
          query={query}
          setQuery={setQuery}
          send={send}
          loading={loading}
          categories={categories}
          selected={selected}
          setSelected={setSelected}
        />
      </div>
    </div>
  );
};
