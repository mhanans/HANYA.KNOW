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
    <div className="category-select">
      {options.map(opt => (
        <label key={opt.id} className="option">
          <input
            type="checkbox"
            checked={selected.includes(opt.id)}
            onChange={() => toggle(opt.id)}
          />
          <span>{opt.name}</span>
        </label>
      ))}
    </div>
  );
};

