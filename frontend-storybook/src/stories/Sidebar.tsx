import React from 'react';
import './sidebar.css';

interface Conversation {
  id: string;
  title: string;
  lastMessage: string;
}

export interface SidebarProps {
  conversations: Conversation[];
  onNewChat: () => void;
  onSelectConversation: (id: string) => void;
  activeConversationId?: string;
}

export const Sidebar = ({
  conversations,
  onNewChat,
  onSelectConversation,
  activeConversationId,
}: SidebarProps) => {
  return (
    <div className="sidebar">
      <div className="sidebar-header">
        <button className="new-chat-button" onClick={onNewChat}>
          + New Chat
        </button>
      </div>
      <div className="conversation-list">
        {conversations.map((convo) => (
          <div
            key={convo.id}
            className={`conversation-item ${
              convo.id === activeConversationId ? 'active' : ''
            }`}
            onClick={() => onSelectConversation(convo.id)}
          >
            <p className="conversation-title">{convo.title}</p>
            <p className="conversation-preview">{convo.lastMessage}</p>
          </div>
        ))}
      </div>
    </div>
  );
};
