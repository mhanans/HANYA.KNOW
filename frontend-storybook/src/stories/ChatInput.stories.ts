import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { ChatInput } from './ChatInput';

const meta = {
  title: 'Components/ChatInput',
  component: ChatInput,
  parameters: {
    layout: 'centered',
  },
  tags: ['autodocs'],
  argTypes: {
    onSendMessage: { action: 'sendMessage' },
  },
} satisfies Meta<typeof ChatInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    disabled: false,
  },
};

export const Disabled: Story = {
  args: {
    disabled: true,
  },
};
