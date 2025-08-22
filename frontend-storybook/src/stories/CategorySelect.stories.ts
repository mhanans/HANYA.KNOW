import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { CategorySelect, Category } from './CategorySelect';

const meta = {
  title: 'Components/CategorySelect',
  component: CategorySelect,
} satisfies Meta<typeof CategorySelect>;

export default meta;
type Story = StoryObj<typeof meta>;

const sampleOptions: Category[] = [
  { id: 1, name: 'Science' },
  { id: 2, name: 'Technology' },
  { id: 3, name: 'Arts' },
];

export const Default: Story = {
  args: {
    options: sampleOptions,
    selected: [1],
    onChange: () => {},
  },
};

export const MultipleSelected: Story = {
  args: {
    options: sampleOptions,
    selected: [1, 3],
    onChange: () => {},
  },
};

