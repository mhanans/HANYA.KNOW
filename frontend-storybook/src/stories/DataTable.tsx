import React from 'react';
import './dataTable.css';

interface DataTableColumn {
  header: string;
  accessor: string;
  render?: (row: any) => React.ReactNode;
}

interface DataTableProps {
  columns: DataTableColumn[];
  data: any[];
  onRowAction?: (row: any, action: string) => void;
}

export const DataTable: React.FC<DataTableProps> = ({ columns, data, onRowAction }) => {
  return (
    <div className="datatable-container">
      <div className="table-wrapper">
        <table className="datatable">
          <thead>
            <tr>
              {columns.map((col) => (
                <th key={col.accessor}>{col.header}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data.map((row, rowIndex) => (
              <tr key={rowIndex}>
                {columns.map((col) => (
                  <td key={col.accessor}>
                    {col.render ? col.render(row) : row[col.accessor]}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};
