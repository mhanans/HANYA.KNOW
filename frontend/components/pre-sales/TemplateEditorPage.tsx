import { useRouter } from 'next/router';
import { useEffect, useMemo, useState } from 'react';
import { apiFetch } from '../../lib/api';

interface TemplateItem {
  itemId: string;
  itemName: string;
  itemDetail: string;
}

interface TemplateSection {
  sectionName: string;
  type: string;
  items: TemplateItem[];
}

interface ProjectTemplate {
  id?: number;
  templateName: string;
  estimationColumns: string[];
  sections: TemplateSection[];
}

const createEmptyTemplate = (): ProjectTemplate => ({
  templateName: '',
  estimationColumns: [],
  sections: [],
});

interface TemplateEditorPageProps {
  templateId?: number;
  mode: 'create' | 'edit';
}

export default function TemplateEditorPage({ templateId, mode }: TemplateEditorPageProps) {
  const router = useRouter();
  const isCreate = mode === 'create';

  const [template, setTemplate] = useState<ProjectTemplate>(createEmptyTemplate);
  const [loading, setLoading] = useState(!isCreate);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [dirty, setDirty] = useState(false);
  const [columnInput, setColumnInput] = useState('');

  useEffect(() => {
    if (isCreate) {
      setTemplate(createEmptyTemplate());
      setLoading(false);
      setDirty(false);
      return;
    }
    if (!templateId) {
      setLoading(true);
      return;
    }
    const load = async () => {
      setLoading(true);
      setError('');
      try {
        const res = await apiFetch(`/api/templates/${templateId}`);
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectTemplate = await res.json();
        setTemplate({
          id: data.id,
          templateName: data.templateName,
          estimationColumns: data.estimationColumns ?? [],
          sections: data.sections ?? [],
        });
        setDirty(false);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load template');
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [isCreate, templateId]);

  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (dirty) {
        e.preventDefault();
        e.returnValue = '';
      }
    };
    if (typeof window !== 'undefined') {
      window.addEventListener('beforeunload', handler);
      return () => window.removeEventListener('beforeunload', handler);
    }
  }, [dirty]);

  const updateTemplate = (updater: (current: ProjectTemplate) => ProjectTemplate) => {
    setTemplate(prev => {
      const next = updater(prev);
      if (next !== prev) setDirty(true);
      return next;
    });
    setNotice('');
  };

  const addColumn = () => {
    if (!columnInput.trim()) return;
    const value = columnInput.trim();
    updateTemplate(prev => ({
      ...prev,
      estimationColumns: [...prev.estimationColumns, value],
    }));
    setColumnInput('');
  };

  const moveColumn = (index: number, delta: number) => {
    updateTemplate(prev => {
      const next = [...prev.estimationColumns];
      const target = index + delta;
      if (target < 0 || target >= next.length) return prev;
      [next[index], next[target]] = [next[target], next[index]];
      return { ...prev, estimationColumns: next };
    });
  };

  const removeColumn = (index: number) => {
    updateTemplate(prev => ({
      ...prev,
      estimationColumns: prev.estimationColumns.filter((_, i) => i !== index),
    }));
  };

  const addSection = () => {
    updateTemplate(prev => ({
      ...prev,
      sections: [
        ...prev.sections,
        { sectionName: 'New Section', type: 'Project-Level', items: [] },
      ],
    }));
  };

  const updateSection = (index: number, updater: (section: TemplateSection) => TemplateSection) => {
    updateTemplate(prev => {
      const sections = prev.sections.map((section, idx) => (idx === index ? updater(section) : section));
      return { ...prev, sections };
    });
  };

  const moveSection = (index: number, delta: number) => {
    updateTemplate(prev => {
      const sections = [...prev.sections];
      const target = index + delta;
      if (target < 0 || target >= sections.length) return prev;
      [sections[index], sections[target]] = [sections[target], sections[index]];
      return { ...prev, sections };
    });
  };

  const removeSection = (index: number) => {
    updateTemplate(prev => ({
      ...prev,
      sections: prev.sections.filter((_, i) => i !== index),
    }));
  };

  const addItem = (sectionIndex: number) => {
    updateSection(sectionIndex, section => ({
      ...section,
      items: [
        ...section.items,
        { itemId: `ITEM-${section.items.length + 1}`, itemName: 'New Item', itemDetail: '' },
      ],
    }));
  };

  const updateItem = (sectionIndex: number, itemIndex: number, updater: (item: TemplateItem) => TemplateItem) => {
    updateSection(sectionIndex, section => ({
      ...section,
      items: section.items.map((item, idx) => (idx === itemIndex ? updater(item) : item)),
    }));
  };

  const moveItem = (sectionIndex: number, itemIndex: number, delta: number) => {
    updateSection(sectionIndex, section => {
      const items = [...section.items];
      const target = itemIndex + delta;
      if (target < 0 || target >= items.length) return section;
      [items[itemIndex], items[target]] = [items[target], items[itemIndex]];
      return { ...section, items };
    });
  };

  const removeItem = (sectionIndex: number, itemIndex: number) => {
    updateSection(sectionIndex, section => ({
      ...section,
      items: section.items.filter((_, idx) => idx !== itemIndex),
    }));
  };

  const canSave = useMemo(() => template.templateName.trim().length > 0, [template.templateName]);

  const save = async () => {
    if (!canSave) return;
    setSaving(true);
    setError('');
    setNotice('');
    try {
      const payload = JSON.stringify(template);
      const url = isCreate ? '/api/templates' : `/api/templates/${templateId}`;
      const method = isCreate ? 'POST' : 'PUT';
      const res = await apiFetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: payload,
      });
      if (!res.ok) {
        throw new Error(await res.text());
      }
      if (isCreate) {
        const created = await res.json();
        setTemplate(created);
        setDirty(false);
        setNotice('Template created successfully.');
        router.replace(`/pre-sales/project-templates/${created.id}`);
      } else {
        setDirty(false);
        setNotice('Template saved successfully.');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save template');
    } finally {
      setSaving(false);
    }
  };

  const cancel = () => {
    if (dirty && !confirm('Discard unsaved changes?')) return;
    router.push('/pre-sales/project-templates');
  };

  return (
    <div className="page-container">
      <div className="page-header">
        <div>
          <h1>{isCreate ? 'Create Project Template' : 'Edit Project Template'}</h1>
          <p>Bangun struktur estimasi lengkap dengan seksi, item, dan kolom dinamis.</p>
        </div>
        <div style={{ display: 'flex', gap: '8px' }}>
          <button className="btn btn-secondary" onClick={cancel}>Cancel</button>
          <button className="btn btn-primary" disabled={!canSave || saving} onClick={save}>
            {saving ? 'Saving…' : 'Save Template'}
          </button>
        </div>
      </div>

      {loading ? (
        <div className="card"><p>Loading template…</p></div>
      ) : (
        <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
          <div>
            <label className="form-label">Template Name</label>
            <input
              className="form-input"
              value={template.templateName}
              onChange={e => updateTemplate(prev => ({ ...prev, templateName: e.target.value }))}
              placeholder="AI Discovery Sprint"
            />
          </div>

          <div>
            <label className="form-label">Estimation Columns</label>
            <div className="tag-input">
              <div className="tags">
                {template.estimationColumns.map((column, index) => (
                  <span className="tag" key={column + index}>
                    {column}
                    <span className="tag-actions">
                      <button type="button" onClick={() => moveColumn(index, -1)} title="Move up">↑</button>
                      <button type="button" onClick={() => moveColumn(index, 1)} title="Move down">↓</button>
                      <button type="button" onClick={() => removeColumn(index)} title="Remove">×</button>
                    </span>
                  </span>
                ))}
              </div>
              <div className="tag-input-row">
                <input
                  className="form-input"
                  placeholder="Add estimation column"
                  value={columnInput}
                  onChange={e => setColumnInput(e.target.value)}
                  onKeyDown={e => {
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      addColumn();
                    }
                  }}
                />
                <button type="button" className="btn btn-secondary" onClick={addColumn}>Add</button>
              </div>
            </div>
          </div>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2>Sections &amp; Items</h2>
            <button type="button" className="btn btn-secondary" onClick={addSection}>Add Section</button>
          </div>

          {template.sections.length === 0 ? (
            <p className="muted">Belum ada seksi. Tambahkan seksi untuk mulai membangun template.</p>
          ) : (
            <div className="section-editor">
              {template.sections.map((section, sectionIndex) => (
                <div className="section-card" key={`${section.sectionName}-${sectionIndex}`}>
                  <div className="section-header">
                    <input
                      className="form-input"
                      value={section.sectionName}
                      onChange={e => updateSection(sectionIndex, current => ({ ...current, sectionName: e.target.value }))}
                      placeholder="Section name"
                    />
                    <select
                      className="form-select"
                      value={section.type}
                      onChange={e => updateSection(sectionIndex, current => ({ ...current, type: e.target.value }))}
                    >
                      <option value="Project-Level">Project-Level</option>
                      <option value="App-Level">App-Level</option>
                    </select>
                  </div>

                  <div className="section-actions">
                    <button type="button" onClick={() => moveSection(sectionIndex, -1)} disabled={sectionIndex === 0}>↑</button>
                    <button
                      type="button"
                      onClick={() => moveSection(sectionIndex, 1)}
                      disabled={sectionIndex === template.sections.length - 1}
                    >↓</button>
                    <button type="button" onClick={() => removeSection(sectionIndex)}>×</button>
                  </div>

                  <div className="items-list">
                    {section.items.length === 0 ? (
                      <p className="muted">Belum ada item. Tambahkan item di seksi ini.</p>
                    ) : (
                      section.items.map((item, itemIndex) => (
                        <div className="item-row" key={`${item.itemId}-${itemIndex}`}>
                          <input
                            className="form-input"
                            value={item.itemId}
                            onChange={e => updateItem(sectionIndex, itemIndex, current => ({ ...current, itemId: e.target.value }))}
                            placeholder="Item ID"
                          />
                          <input
                            className="form-input"
                            value={item.itemName}
                            onChange={e => updateItem(sectionIndex, itemIndex, current => ({ ...current, itemName: e.target.value }))}
                            placeholder="Item name"
                          />
                          <input
                            className="form-input"
                            value={item.itemDetail}
                            onChange={e => updateItem(sectionIndex, itemIndex, current => ({ ...current, itemDetail: e.target.value }))}
                            placeholder="Item detail"
                          />
                          <div className="item-actions">
                            <button type="button" onClick={() => moveItem(sectionIndex, itemIndex, -1)} disabled={itemIndex === 0}>↑</button>
                            <button
                              type="button"
                              onClick={() => moveItem(sectionIndex, itemIndex, 1)}
                              disabled={itemIndex === section.items.length - 1}
                            >↓</button>
                            <button type="button" onClick={() => removeItem(sectionIndex, itemIndex)}>×</button>
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                  <button type="button" className="btn btn-secondary" onClick={() => addItem(sectionIndex)}>Add Item</button>
                </div>
              ))}
            </div>
          )}

          {error && <p className="error">{error}</p>}
          {notice && <p className="success">{notice}</p>}
        </div>
      )}
    </div>
  );
}
