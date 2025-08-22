// ChatInput.tsx
import React, { useRef, useEffect } from 'react';
import './chatInput.css';

// A simple SVG icon for the send button. You can replace this with an icon library.
const SendIcon = ({ disabled }: { disabled: boolean }) => (
  <svg
    width="24"
    height="24"
    viewBox="0 0 24 24"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    className={`send-icon ${disabled ? 'disabled' : ''}`}
  >
    <path
      d="M12 2L2 22L12 18L22 22L12 2Z"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinejoin="round"
    />
  </svg>
);

// A simple spinner for the loading state
const Spinner = () => <div className="spinner"></div>;


export interface ChatInputProps {
  query: string;
  setQuery: (query: string) => void;
  onSendMessage: () => void;
  isLoading: boolean;
  categories: { id: number; name: string }[];
  selectedCategories: number[];
  setSelectedCategories: (selected: number[]) => void;
  error?: string;
  placeholder?: string;
}

export const ChatInput: React.FC<ChatInputProps> = ({
  query,
  setQuery,
  onSendMessage,
  isLoading,
  categories,
  selectedCategories,
  setSelectedCategories,
  error,
  placeholder = "Send a message...",
}) => {
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  // Auto-resize the textarea based on its content
  useEffect(() => {
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto'; // Reset height
      textareaRef.current.style.height = `${textareaRef.current.scrollHeight}px`;
    }
  }, [query]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      if (!isLoading && query.trim()) {
        onSendMessage();
      }
    }
  };

  const handleCategoryToggle = (id: number) => {
    const isSelected = selectedCategories.includes(id);
    if (isSelected) {
      setSelectedCategories(selectedCategories.filter(catId => catId !== id));
    } else {
      setSelectedCategories([...selectedCategories, id]);
    }
  };

  const isSendDisabled = isLoading || !query.trim();

  return (
    <div className="chat-input-wrapper">
      {/* Modern Category Tags */}
      {categories.length > 0 && (
        <div className="category-tags-container">
          {categories.map(c => (
            <button
              key={c.id}
              className={`category-tag ${selectedCategories.includes(c.id) ? 'selected' : ''}`}
              onClick={() => handleCategoryToggle(c.id)}
              disabled={isLoading}
            >
              {c.name}
            </button>
          ))}
        </div>
      )}

      {/* Main Input Bar */}
      <div className={`chat-input-container ${isLoading ? 'loading' : ''} ${error ? 'has-error' : ''}`}>
        <textarea
          ref={textareaRef}
          className="chat-input"
          placeholder={placeholder}
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          disabled={isLoading}
          rows={1} // Start with a single row
        />
        <button
          className="send-button"
          onClick={onSendMessage}
          disabled={isSendDisabled}
          aria-label="Send message"
        >
          {isLoading ? <Spinner /> : <SendIcon disabled={isSendDisabled} />}
        </button>
      </div>
      
      {/* Error Message */}
      {error && <p className="error-message">{error}</p>}
    </div>
  );
};