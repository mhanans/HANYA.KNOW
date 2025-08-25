// src/stories/ChatMessage.stories.tsx
import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { ChatMessage } from './ChatMessage';

// Import the CSS for the typing indicator so the story looks correct
import './chatInterface.css'; 

const meta = {
  title: 'Components/ChatMessage',
  component: ChatMessage,
  parameters: {
    backgrounds: {
      default: 'dark',
      values: [{ name: 'dark', value: '#1e1e1e' }],
    },
    layout: 'padded', // Use padded layout to see the message bubble clearly
  },
  // Add some padding around the component in Storybook for better viewing
  decorators: [
    (Story) => <div style={{ padding: '20px' }}><Story /></div>
  ],
  tags: ['autodocs'],
} satisfies Meta<typeof ChatMessage>;

export default meta;
type Story = StoryObj<typeof meta>;

// --- STORIES ---

export const User: Story = {
  args: {
    role: 'user',
    content: 'Hello, this is a user message.',
  },
};

export const UserWithAvatar: Story = {
  args: {
    ...User.args,
    avatarUrl: 'https://i.pravatar.cc/40?u=user',
  },
};

export const Assistant: Story = {
  args: {
    role: 'assistant',
    content: 'Hello, this is an assistant message with sources.',
    sources: [
      { index: 1, file: 'document1.pdf', page: 2 },
      { index: 2, file: 'document2.pdf' },
    ],
  },
};

export const AssistantWithAvatar: Story = {
  args: {
    ...Assistant.args,
    avatarUrl: 'https://storybook.js.org/images/brand/storybook-icon.svg',
  },
};

// This is the improved Typing Indicator for the story.
// It uses the actual CSS class for an accurate representation.
const TypingIndicator = () => (
  <div className="typing-indicator">
    <span></span>
    <span></span>
    <span></span>
  </div>
);

export const AssistantTyping: Story = {
  args: {
    role: 'assistant',
    content: '', // Content is ignored because we are passing children
    avatarUrl: 'https://storybook.js.org/images/brand/storybook-icon.svg',
    children: <TypingIndicator />,
  },
};