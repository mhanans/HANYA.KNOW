import React from 'react';
import './categorySelect.css';

export interface Category {
  id: number;
  name: string;
}

export interface CategorySelectProps {
  options: Category[];
  selected: number[];
  onChange: (selected: number[]) => void;
}

export const CategorySelect: React.FC<CategorySelectProps> = ({ options, selected, onChange }) => {
  const toggle = (id: number) => {
    if (selected.includes(id)) {
      onChange(selected.filter(s => s !== id));
    } else {
      onChange([...selected, id]);
    }
  };

  return (
    <div className="category-tags-container">
      {options.map(opt => (
        <button
          key={opt.id}
          className={`category-tag ${selected.includes(opt.id) ? 'selected' : ''}`}
          onClick={() => toggle(opt.id)}
          type="button"
        >
          {opt.name}
        </button>
      ))}
    </div>
  );
};

