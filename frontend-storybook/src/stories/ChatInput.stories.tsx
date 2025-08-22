import React from 'react';
import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { action } from '@storybook/addon-actions';
import { ChatInput } from './ChatInput';

const meta = {
  title: 'Components/ChatInput',
  component: ChatInput,
  parameters: { layout: 'centered' },
  tags: ['autodocs'],
} satisfies Meta<typeof ChatInput>;

export default meta;
type Story = StoryObj<typeof meta>;

const categories = [
  { id: 1, name: 'General' },
  { id: 2, name: 'Support' },
  { id: 3, name: 'Fun' },
];

export const Default: Story = {
  render: args => {
    const [query, setQuery] = React.useState('');
    const [selected, setSelected] = React.useState<number[]>([]);
    return (
      <ChatInput
        {...args}
        query={query}
        setQuery={setQuery}
        selected={selected}
        setSelected={setSelected}
      />
    );
  },
  args: {
    send: action('send'),
    loading: false,
    categories,
  },
};

export const Loading: Story = {
  ...Default,
  args: {
    ...Default.args,
    loading: true,
  },
};
