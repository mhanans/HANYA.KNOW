import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import Layout from '../components/Layout';
import React from 'react';

const meta = {
  title: 'Components/Layout',
  component: Layout,
  parameters: {
    nextjs: {
      router: {
        pathname: '/',
      },
    },
    layout: 'fullscreen',
  },
} satisfies Meta<typeof Layout>;

export default meta;
type Story = StoryObj<typeof Layout>;

export const Default: Story = {
  args: {
    children: <div style={{ padding: '1rem' }}>Content</div>,
  },
};
