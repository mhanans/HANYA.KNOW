import React, { useState, useEffect, useRef } from 'react';
import { ChatMessage } from './ChatMessage';
import { ChatInput } from './ChatInput';
import './chatInterface.css';

interface Message {
  text: string;
  sender: 'user' | 'bot';
}

export interface ChatInterfaceProps {
  initialMessages?: Message[];
  botAvatar?: string;
}

export const ChatInterface = ({ initialMessages = [], botAvatar }: ChatInterfaceProps) => {
  const [messages, setMessages] = useState<Message[]>(initialMessages);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const handleSendMessage = (text: string) => {
    const newUserMessage: Message = { text, sender: 'user' };
    setMessages((prevMessages) => [...prevMessages, newUserMessage]);

    // Simulate a bot response
    setTimeout(() => {
      const botResponse: Message = {
        text: `This is a simulated response to "${text}"`,
        sender: 'bot',
      };
      setMessages((prevMessages) => [...prevMessages, botResponse]);
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
        <ChatInput onSendMessage={handleSendMessage} />
      </div>
    </div>
  );
};