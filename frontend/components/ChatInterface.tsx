import React, { useState } from 'react';

interface Message {
  sender: 'user' | 'bot';
  text: string;
}

const ChatInterface = () => {
  const [messages, setMessages] = useState<Message[]>([]);
  const [query, setQuery] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSendMessage = async () => {
    if (!query.trim()) return;

    const userMessage = { sender: 'user', text: query };
    // Add user message and an empty bot placeholder
    const newMessages = [...messages, userMessage, { sender: 'bot', text: '' }];
    setMessages(newMessages);
    setQuery('');
    setIsLoading(true);

    // Create the EventSource to connect to your streaming endpoint
    const eventSource = new EventSource('/api/chat/stream', {
      // Cast to any because EventSource init types do not include custom options
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: query, history: messages })
    } as any);

    // --- This is the core streaming logic ---
    eventSource.onmessage = (event) => {
      const parsedData = JSON.parse(event.data);

      if (parsedData.text) {
        // Update the last message (the bot's placeholder) by appending the new text
        setMessages(prevMessages => {
          const updatedMessages = [...prevMessages];
          const lastMessage = updatedMessages[updatedMessages.length - 1];
          lastMessage.text += parsedData.text;
          return updatedMessages;
        });
      }
    };

    // Handle errors and the end of the stream
    eventSource.onerror = (err) => {
      console.error('EventSource failed:', err);
      eventSource.close();
      setIsLoading(false); // Re-enable the input
    };

    // The stream will be closed by the server with res.end()
    // which triggers the onerror handler.
  };

  return (
    <div>
      <div>
        {messages.map((m, i) => (
          <div key={i}>
            <strong>{m.sender}:</strong> {m.text}
          </div>
        ))}
      </div>
      <div>
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          disabled={isLoading}
        />
        <button onClick={handleSendMessage} disabled={isLoading || !query.trim()}>
          Send
        </button>
      </div>
    </div>
  );
};

export default ChatInterface;
