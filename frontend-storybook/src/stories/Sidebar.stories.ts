import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { Sidebar } from './Sidebar';

const meta = {
  title: 'Components/Sidebar',
  component: Sidebar,
  parameters: {
    layout: 'fullscreen',
  },
  tags: ['autodocs'],
  argTypes: {
    onNewChat: { action: 'newChat' },
    onSelectConversation: { action: 'selectConversation' },
  },
} satisfies Meta<typeof Sidebar>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Empty: Story = {
  args: {
    conversations: [],
  },
};

const conversations = [
  { id: '1', title: 'Joke Session', lastMessage: 'Because he was outstanding...' },
  { id: '2', title: 'React Questions', lastMessage: 'What is a hook?' },
  { id: '3', title: 'Storybook Help', lastMessage: 'How do I write stories?' },
];

export const WithConversations: Story = {
  args: {
    conversations,
    activeConversationId: '2',
  },
};

const longConversationList = Array.from({ length: 20 }, (_, i) => ({
  id: `${i + 1}`,
  title: `Conversation ${i + 1}`,
  lastMessage: `This is the last message of conversation ${i + 1}.`,
}));

export const LongList: Story = {
  args: {
    conversations: longConversationList,
    activeConversationId: '5',
  },
};
