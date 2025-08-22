import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { FileUpload } from './FileUpload';

const meta = {
  title: 'Components/FileUpload',
  component: FileUpload,
} satisfies Meta<typeof FileUpload>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {
  args: {
    onFilesChange: (files) => console.log(files),
  },
};
