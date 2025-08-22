import type { Meta, StoryObj } from '@storybook/nextjs-vite';

import { Menu } from './Menu';

const meta = {
  title: 'Components/Menu',
  component: Menu,
  parameters: {
    layout: 'fullscreen',
  },
} satisfies Meta<typeof Menu>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    items: ['Home', 'Docs', 'About'],
  },
};

