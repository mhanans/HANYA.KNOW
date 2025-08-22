import type { Meta, StoryObj } from '@storybook/nextjs-vite';
import { DataTable } from './DataTable';

interface RowData {
  name: string;
  role: string;
}

const meta: Meta<typeof DataTable> = {
  title: 'Components/DataTable',
  component: DataTable,
} satisfies Meta<typeof DataTable>;

export default meta;

type Story = StoryObj<typeof meta>;

const columns: { header: string; accessor: keyof RowData }[] = [
  { header: 'Name', accessor: 'name' },
  { header: 'Role', accessor: 'role' },
];

const data: RowData[] = [
  { name: 'Alice', role: 'Admin' },
  { name: 'Bob', role: 'User' },
];

export const Default: Story = {
  render: () => <DataTable<RowData> columns={columns} data={data} />,
};
