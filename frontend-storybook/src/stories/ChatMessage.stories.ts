import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { ChatMessage } from './ChatMessage';

const meta = {
  title: 'Components/ChatMessage',
  component: ChatMessage,
} satisfies Meta<typeof ChatMessage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const User: Story = {
  args: {
    role: 'user',
    content: 'Hello, this is a user message.',
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