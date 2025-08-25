// src/stories/ChatInterface.tsx
import React, { useState, useEffect, useRef } from 'react';
// Make sure to import your version of ChatMessage and its props (if needed)
import { ChatMessage } from './ChatMessage';
import { ChatInput } from './ChatInput';
import './chatInterface.css';

// Define the internal Message type for the interface's state
interface Message {
  text: string;
  sender: 'user' | 'bot';
  // You could even add sources here if your API provides them
  sources?: { index: number; file: string; page?: number }[];
}

// Sub-components (TypingIndicator, WelcomeScreen) remain the same as before
const TypingIndicator = () => ( <div className="typing-indicator"><span></span><span></span><span></span></div> );
const WelcomeScreen = ({ logoUrl, appName }: { logoUrl?: string; appName?: string }) => ( <div className="welcome-screen">{logoUrl ? <img src={logoUrl} alt="Logo" className="welcome-logo" /> : <h1>{appName || 'Chat'}</h1>}<p>How can I help you today?</p></div> );

export interface ChatInterfaceProps {
  initialMessages?: Message[];
  botAvatar?: string;
  userAvatar?: string;
  appName?: string;
  appLogoUrl?: string;
  categories?: { id: number; name: string }[];
}

export const ChatInterface: React.FC<ChatInterfaceProps> = ({
  initialMessages = [],
  botAvatar,
  userAvatar,
  appName,
  appLogoUrl,
  categories = [],
}) => {
  const [messages, setMessages] = useState<Message[]>(initialMessages);
  const [query, setQuery] = useState('');
  const [selectedCategories, setSelectedCategories] = useState<number[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, isLoading]);

  const handleSendMessage = () => {
    if (!query.trim()) return;
    setError('');

    const newUserMessage: Message = { text: query, sender: 'user' };
    setMessages(prev => [...prev, newUserMessage]);
    setQuery('');
    setIsLoading(true);

    setTimeout(() => {
      const botResponse: Message = {
        text: `This is a simulated response to "${newUserMessage.text}"`,
        sender: 'bot',
        // Example of adding sources
        sources: [{ index: 1, file: 'simulation_data.txt' }]
      };
      setMessages(prev => [...prev, botResponse]);
      setIsLoading(false);
    }, 1500);
  };

  return (
    <div className="chat-interface">
      <div className="messages-container">
        {messages.length === 0 && !isLoading ? (
          <WelcomeScreen logoUrl={appLogoUrl} appName={appName} />
        ) : (
          messages.map((msg, index) => (
            // *** THIS IS THE CRITICAL CHANGE ***
            // We are mapping the Message object to ChatMessageProps here
            <ChatMessage
              key={index}
              role={msg.sender}
              content={msg.text}
              sources={msg.sources}
              avatarUrl={msg.sender === 'bot' ? botAvatar : userAvatar}
            />
          ))
        )}
        
        {/* Now this will work because ChatMessage accepts children */}
        {isLoading && (
          <ChatMessage
            role="bot"
            content="" // Content is empty because we are passing children
            avatarUrl={botAvatar}
          >
            <TypingIndicator />
          </ChatMessage>
        )}
        
        <div ref={messagesEndRef} />
      </div>

      <div className="input-area">
        <ChatInput
          query={query}
          setQuery={setQuery}
          onSendMessage={handleSendMessage}
          isLoading={isLoading}
          categories={categories}
          selectedCategories={selectedCategories}
          setSelectedCategories={setSelectedCategories}
          error={error}
        />
      </div>
    </div>
  );
};