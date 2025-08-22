import React from 'react';

interface Option {
  id: number;
  name: string;
}

interface Props {
  options: Option[];
  selected: number[];
  onChange: (ids: number[]) => void;
}

export default function TagInput({ options, selected, onChange }: Props) {
  const available = options.filter(o => !selected.includes(o.id));
  const add = (id: number) => onChange([...selected, id]);
  const remove = (id: number) => onChange(selected.filter(x => x !== id));
  return (
    <div className="tag-input">
      <div className="tags">
        {selected.map(id => {
          const opt = options.find(o => o.id === id);
          return (
            <span className="tag" key={id}>
              {opt?.name}
              <button type="button" onClick={() => remove(id)}>×</button>
            </span>
          );
        })}
      </div>
      {available.length > 0 && (
        <select className="form-select" value="" onChange={e => { const val = Number(e.target.value); if (val) add(val); }}>
          <option value="">Select...</option>
          {available.map(o => (
            <option key={o.id} value={o.id}>{o.name}</option>
          ))}
        </select>
      )}
    </div>
  );
}
