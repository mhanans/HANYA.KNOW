import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { LoginPage } from './LoginPage';

const meta = {
  title: 'Pages/LoginPage',
  component: LoginPage,
  parameters: {
    layout: 'fullscreen',
  },
} satisfies Meta<typeof LoginPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    onSubmit: (data) => console.log(data),
  },
};
