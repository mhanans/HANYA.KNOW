import type { Meta, StoryObj } from '@storybook/react';
import Layout from '../../frontend/components/Layout';

const meta: Meta<typeof Layout> = {
  title: 'Layout',
  component: Layout,
};

export default meta;

type Story = StoryObj<typeof Layout>;

export const Default: Story = {
  render: () => (
    <Layout>
      <div>Content</div>
    </Layout>
  ),
};
