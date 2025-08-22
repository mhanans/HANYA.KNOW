import React from 'react';
import { Sidebar, SidebarProps } from './Sidebar';
import { ChatInterface, ChatInterfaceProps } from './ChatInterface';

export interface AppLayoutProps {
  sidebar: SidebarProps;
  chatInterface: ChatInterfaceProps;
}

export const AppLayout = ({ sidebar, chatInterface }: AppLayoutProps) => {
  return (
    <div className="app-layout">
      <Sidebar {...sidebar} />
      <main className="main-content">
        <ChatInterface {...chatInterface} />
      </main>
    </div>
  );
};
