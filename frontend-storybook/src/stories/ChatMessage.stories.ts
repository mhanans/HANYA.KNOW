import type { Meta, StoryObj } from '@storybook/react';
import { ChatMessage } from './ChatMessage';

const meta = {
  title: 'Components/ChatMessage',
  component: ChatMessage,
  parameters: {
    layout: 'padded',
  },
  tags: ['autodocs'],
} satisfies Meta<typeof ChatMessage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const UserMessage: Story = {
  args: {
    message: {
      text: 'Hello, this is a message from the user.',
      sender: 'user',
    },
  },
};

export const BotMessage: Story = {
  args: {
    message: {
      text: 'Hello, this is a response from the bot.',
      sender: 'bot',
    },
  },
};

export const BotMessageWithAvatar: Story = {
  args: {
    message: {
      text: 'I have a custom avatar!',
      sender: 'bot',
    },
    botAvatar: 'https://storybook.js.org/images/brand/storybook-icon.svg',
  },
};
