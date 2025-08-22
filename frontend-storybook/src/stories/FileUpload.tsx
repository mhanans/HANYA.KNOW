import React, { useState } from 'react';

export interface FileUploadProps {
  accept?: string;
  multiple?: boolean;
  onFilesChange?: (files: File[]) => void;
}

export const FileUpload: React.FC<FileUploadProps> = ({ accept = 'application/pdf', multiple = true, onFilesChange }) => {
  const [files, setFiles] = useState<File[]>([]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const selected = Array.from(e.target.files ?? []);
    setFiles(selected);
    onFilesChange?.(selected);
  };

  return (
    <div className="file-upload">
      <input type="file" accept={accept} multiple={multiple} onChange={handleChange} />
      {files.length > 0 && (
        <ul className="file-list">
          {files.map((f) => (
            <li key={f.name}>{f.name}</li>
          ))}
        </ul>
      )}
    </div>
  );
};
