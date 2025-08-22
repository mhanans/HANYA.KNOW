// ChatInput.stories.tsx
import type { Meta, StoryObj } from '@storybook/react';
import { ChatInput } from './ChatInput';
import { useState } from 'react';

const meta = {
  title: 'Components/ChatInput',
  component: ChatInput,
  parameters: {
    layout: 'centered',
    // Add a dark background to the stories to match the component's theme
    backgrounds: {
      default: 'dark',
      values: [
        { name: 'dark', value: '#1a1a1a' },
      ],
    },
  },
  tags: ['autodocs'],
  argTypes: {
    // We map actions to the props that are functions
    onSendMessage: { action: 'sendMessage' },
    setQuery: { action: 'setQuery' },
    setSelectedCategories: { action: 'setSelectedCategories' },
  },
  // A wrapper to provide state management for interactive stories
  decorators: [
    (Story, context) => {
      const [query, setQuery] = useState(context.args.query || '');
      const [selected, setSelected] = useState(context.args.selectedCategories || []);

      return (
        <div style={{ width: '600px', padding: '20px' }}>
          <Story
            args={{
              ...context.args,
              query,
              setQuery,
              selectedCategories: selected,
              setSelectedCategories: setSelected,
            }}
          />
        </div>
      );
    },
  ],
} satisfies Meta<typeof ChatInput>;

export default meta;
type Story = StoryObj<typeof meta>;

const mockCategories = [
  { id: 1, name: 'General' },
  { id: 2, name: 'Code Generation' },
  { id: 3, name: 'Creative Writing' },
];

// --- STORIES ---

export const Default: Story = {
  args: {
    isLoading: false,
    error: '',
    categories: mockCategories,
    selectedCategories: [],
    query: '',
  },
};

export const WithText: Story = {
  args: {
    ...Default.args,
    query: 'Hello, can you help me write a function in TypeScript?',
  },
};

export const WithCategoriesSelected: Story = {
  args: {
    ...Default.args,
    selectedCategories: [2],
  },
};

export const Loading: Story = {
  args: {
    ...WithText.args,
    isLoading: true,
  },
};

export const WithError: Story = {
  args: {
    ...Default.args,
    error: 'Failed to send message. Please try again.',
  },
};

export const NoCategories: Story = {
  args: {
    ...Default.args,
    categories: [],
  },
};