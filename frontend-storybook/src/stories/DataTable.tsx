import React from 'react';

interface DataTableColumn<T extends Record<string, unknown>> {
  header: string;
  accessor: keyof T;
  render?: (row: T) => React.ReactNode;
}

interface DataTableProps<T extends Record<string, unknown>> {
  columns: DataTableColumn<T>[];
  data: T[];
}

export function DataTable<T extends Record<string, unknown> = Record<string, unknown>>({ columns, data }: DataTableProps<T>) {
  return (
    <div className="datatable-container">
      <div className="table-wrapper">
        <table className="datatable">
          <thead>
            <tr>
              {columns.map((col) => (
                <th key={String(col.accessor)}>{col.header}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data.map((row, rowIndex) => (
              <tr key={rowIndex}>
                {columns.map((col) => (
                  <td key={String(col.accessor)}>
                    {col.render ? col.render(row) : String(row[col.accessor])}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
