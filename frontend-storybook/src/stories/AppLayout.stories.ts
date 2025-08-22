import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { AppLayout } from './AppLayout';

const meta = {
  title: 'Layouts/AppLayout',
  component: AppLayout,
  parameters: {
    layout: 'fullscreen',
  },
  tags: ['autodocs'],
} satisfies Meta<typeof AppLayout>;

export default meta;
type Story = StoryObj<typeof meta>;

const conversations = [
  { id: '1', title: 'Joke Session', lastMessage: 'Because he was outstanding...' },
  { id: '2', title: 'React Questions', lastMessage: 'What is a hook?' },
  { id: '3', title: 'Storybook Help', lastMessage: 'How do I write stories?' },
];

const conversation = [
  { text: 'Tell me a joke.', sender: 'user' as const },
  { text: 'Why did the scarecrow win an award?', sender: 'bot' as const },
  { text: 'I don\'t know, why?', sender: 'user' as const },
  { text: 'Because he was outstanding in his field!', sender: 'bot' as const },
];

export const Default: Story = {
  args: {
    sidebar: {
      conversations,
      activeConversationId: '1',
      onNewChat: () => alert('New Chat clicked!'),
      onSelectConversation: (id) => alert(`Selected conversation ${id}`),
    },
    chatInterface: {
      initialMessages: conversation,
      botAvatar: 'https://storybook.js.org/images/brand/storybook-icon.svg',
    },
  },
};
