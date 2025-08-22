import type { Meta, StoryObj } from '@storybook/react';
import { ChatInterface } from './ChatInterface';

const meta = {
  title: 'Components/ChatInterface',
  component: ChatInterface,
  parameters: {
    layout: 'fullscreen',
  },
  tags: ['autodocs'],
} satisfies Meta<typeof ChatInterface>;

export default meta;
type Story = StoryObj<typeof meta>;

export const EmptyChat: Story = {
  args: {
    initialMessages: [],
  },
};

const conversation = [
  { text: 'Tell me a joke.', sender: 'user' as const },
  { text: 'Why did the scarecrow win an award?', sender: 'bot' as const },
  { text: 'I don\'t know, why?', sender: 'user' as const },
  { text: 'Because he was outstanding in his field!', sender: 'bot' as const },
];

export const Conversation: Story = {
  args: {
    initialMessages: conversation,
    botAvatar: 'https://storybook.js.org/images/brand/storybook-icon.svg',
  },
};

const longConversation = [
  ...conversation,
  { text: 'That\'s a good one! How about another?', sender: 'user' as const },
  { text: 'Sure. What do you call fake spaghetti?', sender: 'bot' as const },
  { text: 'What?', sender: 'user' as const },
  { text: 'An Impasta!', sender: 'bot' as const },
  { text: 'Haha, very funny.', sender: 'user' as const },
  { text: 'I thought so too.', sender: 'bot' as const },
  { text: 'What else can you do?', sender: 'user' as const },
  { text: 'I can tell you stories, answer questions, and much more. What would you like to explore?', sender: 'bot' as const },
];

export const LongConversation: Story = {
  args: {
    initialMessages: longConversation,
    botAvatar: 'https://storybook.js.org/images/brand/storybook-icon.svg',
  },
};
