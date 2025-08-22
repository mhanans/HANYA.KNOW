// src/stories/ChatInterface.stories.tsx
import type { Meta, StoryObj } from '@storybook/react';
import { ChatInterface } from './ChatInterface';

// Assuming your Message type is defined in ChatMessage.tsx and exported
// export interface Message { text: string; sender: 'user' | 'bot'; sources?: any[]; }
import { Message } from './ChatMessage'; 

const meta = {
  title: 'Pages/FullChat', // Renamed to reflect it's a full page component
  component: ChatInterface,
  parameters: {
    layout: 'fullscreen',
  },
} satisfies Meta<typeof ChatInterface>;

export default meta;
type Story = StoryObj<typeof meta>;

// This is the data structure that fixes your problem.
// The user's message is the FIRST item in the array.
const conversation: Message[] = [
  { sender: 'user', text: 'Tell me a joke.' },
  { sender: 'bot', text: 'Why did the scarecrow win an award?' },
  { sender: 'bot', text: 'Because he was outstanding in his field!' },
];

export const Default: Story = {
  args: {
    // Pass the full conversation to the component
    initialMessages: conversation,
    
    // Provide avatars
    userAvatar: 'https://i.pravatar.cc/40?u=user123',
    botAvatar: 'https://i.pravatar.cc/40?u=bot456',
  },
};

export const EmptyChat: Story = {
  args: {
    initialMessages: [],
    appName: 'StoryBot',
    appLogoUrl: 'https://storybook.js.org/images/brand/storybook-icon.svg',
    userAvatar: 'https://i.pravatar.cc/40?u=user123',
    botAvatar: 'https://i.pravatar.cc/40?u=bot456',
  },
};