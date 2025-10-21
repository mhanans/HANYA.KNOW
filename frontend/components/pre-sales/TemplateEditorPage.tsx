import { useRouter } from 'next/router';
import { useEffect, useMemo, useState } from 'react';
import type { DragEvent as ReactDragEvent } from 'react';
import { apiFetch } from '../../lib/api';

interface TemplateItem {
  itemId: string;
  itemName: string;
  itemDetail: string;
  uid?: string;
}

interface TemplateSection {
  sectionName: string;
  type: string;
  items: TemplateItem[];
  uid?: string;
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

const generateUid = () => Math.random().toString(36).slice(2, 10);

const withGeneratedIds = (template: ProjectTemplate): ProjectTemplate => ({
  ...template,
  sections: (template.sections ?? []).map(section => ({
    ...section,
    uid: section.uid ?? generateUid(),
    items: (section.items ?? []).map(item => ({
      ...item,
      uid: item.uid ?? generateUid(),
    })),
  })),
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
  const [expandedSections, setExpandedSections] = useState<Set<string>>(new Set());
  const [dragSectionUid, setDragSectionUid] = useState<string | null>(null);
  const [dragSectionOverUid, setDragSectionOverUid] = useState<string | null>(null);
  const [dragItemRef, setDragItemRef] = useState<{ sectionUid: string; itemUid: string } | null>(null);
  const [dragItemOverRef, setDragItemOverRef] = useState<
    { sectionUid: string; itemUid: string | 'end' } | null
  >(null);

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
        const next = withGeneratedIds({
          id: data.id,
          templateName: data.templateName,
          estimationColumns: data.estimationColumns ?? [],
          sections: data.sections ?? [],
        });
        setTemplate(next);
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

  useEffect(() => {
    setExpandedSections(prev => {
      const next = new Set<string>();
      template.sections.forEach(section => {
        if (!section.uid) return;
        if (prev.has(section.uid)) {
          next.add(section.uid);
        }
      });
      if (template.sections.length > prev.size) {
        template.sections.forEach(section => {
          if (!section.uid) return;
          if (!prev.has(section.uid)) {
            next.add(section.uid);
          }
        });
      }
      return next;
    });
  }, [template.sections]);

  const updateTemplate = (updater: (current: ProjectTemplate) => ProjectTemplate) => {
    setTemplate(prev => {
      const next = updater(prev);
      const normalised = withGeneratedIds(next);
      if (normalised !== prev) setDirty(true);
      return normalised;
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
        { sectionName: 'New Section', type: 'Project-Level', items: [], uid: generateUid() },
      ],
    }));
  };

  const updateSection = (index: number, updater: (section: TemplateSection) => TemplateSection) => {
    updateTemplate(prev => {
      const sections = prev.sections.map((section, idx) => (idx === index ? updater(section) : section));
      return { ...prev, sections };
    });
  };

  const updateSectionByUid = (uid: string, updater: (section: TemplateSection) => TemplateSection) => {
    const sectionIndex = template.sections.findIndex(section => section.uid === uid);
    if (sectionIndex === -1) return;
    updateSection(sectionIndex, updater);
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
        {
          itemId: `ITEM-${section.items.length + 1}`,
          itemName: 'New Item',
          itemDetail: '',
          uid: generateUid(),
        },
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

  const toggleSection = (uid?: string) => {
    if (!uid) return;
    setExpandedSections(prev => {
      const next = new Set(prev);
      if (next.has(uid)) {
        next.delete(uid);
      } else {
        next.add(uid);
      }
      return next;
    });
  };

  const canSave = useMemo(() => template.templateName.trim().length > 0, [template.templateName]);

  const preparePayload = (input: ProjectTemplate) => ({
    ...input,
    sections: input.sections.map(section => ({
      sectionName: section.sectionName,
      type: section.type,
      items: section.items.map(item => ({
        itemId: item.itemId,
        itemName: item.itemName,
        itemDetail: item.itemDetail,
      })),
    })),
  });

  const handleSectionDragStart = (uid?: string) => (event: ReactDragEvent) => {
    if (!uid) {
      event.preventDefault();
      return;
    }
    const handle = (event.target as HTMLElement).closest('.section-drag-handle');
    if (!handle) {
      event.preventDefault();
      return;
    }
    setDragSectionUid(uid);
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', uid);
  };

  const handleSectionDragOver = (uid?: string) => (event: ReactDragEvent) => {
    if (!dragSectionUid || !uid || dragSectionUid === uid) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    if (dragSectionOverUid !== uid) {
      setDragSectionOverUid(uid);
    }
  };

  const handleSectionDragLeave = (uid?: string) => () => {
    setDragSectionOverUid(prev => (prev === uid ? null : prev));
  };

  const handleSectionDrop = (uid?: string) => (event: ReactDragEvent) => {
    if (!dragSectionUid || !uid) return;
    event.preventDefault();
    event.stopPropagation();
    setDragSectionOverUid(null);
    const fromIndex = template.sections.findIndex(section => section.uid === dragSectionUid);
    const toIndex = template.sections.findIndex(section => section.uid === uid);
    if (fromIndex === -1 || toIndex === -1 || fromIndex === toIndex) {
      setDragSectionUid(null);
      return;
    }
    updateTemplate(prev => {
      const sections = [...prev.sections];
      const [moved] = sections.splice(fromIndex, 1);
      sections.splice(toIndex, 0, moved);
      return { ...prev, sections };
    });
    setDragSectionUid(null);
  };

  const handleSectionDragEnd = () => {
    setDragSectionUid(null);
    setDragSectionOverUid(null);
  };

  const handleItemDragStart = (sectionUid?: string, itemUid?: string) => (event: ReactDragEvent) => {
    if (!sectionUid || !itemUid) {
      event.preventDefault();
      return;
    }
    const handle = (event.target as HTMLElement).closest('.item-drag-handle');
    if (!handle) {
      event.preventDefault();
      return;
    }
    setDragItemRef({ sectionUid, itemUid });
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', `${sectionUid}:${itemUid}`);
  };

  const handleItemDragOver = (sectionUid?: string, itemUid?: string | 'end') => (event: ReactDragEvent) => {
    if (!dragItemRef || !sectionUid || dragItemRef.sectionUid !== sectionUid) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    const targetUid = itemUid ?? 'end';
    if (!dragItemOverRef || dragItemOverRef.sectionUid !== sectionUid || dragItemOverRef.itemUid !== targetUid) {
      setDragItemOverRef({ sectionUid, itemUid: targetUid });
    }
  };

  const handleItemDragLeave = (sectionUid?: string, itemUid?: string | 'end') => () => {
    setDragItemOverRef(prev => {
      if (!prev) return prev;
      const targetUid = itemUid ?? 'end';
      if (prev.sectionUid === sectionUid && prev.itemUid === targetUid) {
        return null;
      }
      return prev;
    });
  };

  const handleItemDrop = (sectionUid?: string, itemUid?: string | 'end') => (event: ReactDragEvent) => {
    if (!dragItemRef || !sectionUid || dragItemRef.sectionUid !== sectionUid) return;
    event.preventDefault();
    event.stopPropagation();
    const targetUid = itemUid ?? 'end';
    if (targetUid !== 'end' && targetUid === dragItemRef.itemUid) {
      setDragItemOverRef(null);
      setDragItemRef(null);
      return;
    }
    updateSectionByUid(sectionUid, section => {
      const items = [...section.items];
      const fromIndex = items.findIndex(item => item.uid === dragItemRef.itemUid);
      if (fromIndex === -1) return section;
      const [moved] = items.splice(fromIndex, 1);
      const targetIndex = items.findIndex(item => item.uid === targetUid);
      const insertionIndex = targetUid === 'end' ? items.length : targetIndex === -1 ? items.length : targetIndex;
      items.splice(insertionIndex, 0, moved);
      return { ...section, items };
    });
    setDragItemRef(null);
    setDragItemOverRef(null);
  };

  const handleItemDragEnd = () => {
    setDragItemRef(null);
    setDragItemOverRef(null);
  };

  const save = async () => {
    if (!canSave) return;
    setSaving(true);
    setError('');
    setNotice('');
    try {
      const payload = JSON.stringify(preparePayload(template));
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
        setTemplate(withGeneratedIds(created));
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
        <div className="card template-editor-card">
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
            <div className="tag-input enhanced">
              <div className="tags">
                {template.estimationColumns.length === 0 && (
                  <span className="tag muted">No estimation columns yet.</span>
                )}
                {template.estimationColumns.map((column, index) => (
                  <span className="tag" key={column + index}>
                    <span className="tag-label">{column}</span>
                    <span className="tag-actions">
                      <button type="button" onClick={() => moveColumn(index, -1)} title="Move up" aria-label="Move column up">
                        ↑
                      </button>
                      <button type="button" onClick={() => moveColumn(index, 1)} title="Move down" aria-label="Move column down">
                        ↓
                      </button>
                      <button type="button" onClick={() => removeColumn(index)} title="Remove" aria-label="Remove column">
                        ×
                      </button>
                    </span>
                  </span>
                ))}
              </div>
              <div className="tag-input-row">
                <input
                  className="form-input"
                  placeholder="e.g. Estimated Hours"
                  value={columnInput}
                  onChange={e => setColumnInput(e.target.value)}
                  onKeyDown={e => {
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      addColumn();
                    }
                  }}
                />
                <button type="button" className="btn btn-secondary" onClick={addColumn}>
                  Add Column
                </button>
              </div>
            </div>
          </div>

          <div className="template-editor-toolbar">
            <div>
              <h2>Sections &amp; Items</h2>
              <p className="muted">Kelompokkan struktur template dalam kartu untuk navigasi yang lebih mudah.</p>
            </div>
            <button type="button" className="btn btn-secondary" onClick={addSection}>
              Add Section
            </button>
          </div>

          {template.sections.length === 0 ? (
            <div className="empty-state">
              <h3>Belum ada seksi</h3>
              <p>Buat seksi pertama Anda untuk mulai membangun blueprint proyek.</p>
              <button type="button" className="btn btn-secondary" onClick={addSection}>
                Create Section
              </button>
            </div>
          ) : (
            <div className="section-editor">
              {template.sections.map((section, sectionIndex) => {
                const sectionUid = section.uid ?? String(sectionIndex);
                const expanded = expandedSections.has(sectionUid);
                return (
                  <div
                    className={`section-card${
                      dragSectionUid === sectionUid ? ' is-dragging' : ''
                    }${dragSectionOverUid === sectionUid ? ' drag-over' : ''}`}
                    key={sectionUid}
                    onDragOver={handleSectionDragOver(sectionUid)}
                    onDrop={handleSectionDrop(sectionUid)}
                    onDragLeave={handleSectionDragLeave(sectionUid)}
                  >
                    <div className="section-card-header">
                      <button
                        className="section-drag-handle"
                        draggable
                        onDragStart={handleSectionDragStart(sectionUid)}
                        onDragEnd={handleSectionDragEnd}
                        aria-label="Drag to reorder section"
                      >
                        ☰
                      </button>
                      <div className="section-card-title">
                        <input
                          className="form-input section-name-input"
                          value={section.sectionName}
                          onChange={e =>
                            updateSection(sectionIndex, current => ({ ...current, sectionName: e.target.value }))
                          }
                          placeholder="Section name"
                        />
                        <div className="section-type-select">
                          <label>
                            Type
                            <select
                              className="form-select"
                              value={section.type}
                              onChange={e =>
                                updateSection(sectionIndex, current => ({ ...current, type: e.target.value }))
                              }
                            >
                              <option value="Project-Level">Project-Level</option>
                              <option value="App-Level">App-Level</option>
                            </select>
                          </label>
                        </div>
                      </div>
                      <div className="section-card-controls">
                        <div className="section-order-buttons">
                          <button
                            type="button"
                            onClick={() => moveSection(sectionIndex, -1)}
                            disabled={sectionIndex === 0}
                            aria-label="Move section up"
                          >
                            ↑
                          </button>
                          <button
                            type="button"
                            onClick={() => moveSection(sectionIndex, 1)}
                            disabled={sectionIndex === template.sections.length - 1}
                            aria-label="Move section down"
                          >
                            ↓
                          </button>
                        </div>
                        <button
                          type="button"
                          className="btn btn-tertiary"
                          onClick={() => toggleSection(sectionUid)}
                          aria-expanded={expanded}
                        >
                          {expanded ? 'Collapse' : 'Expand'}
                        </button>
                        <button
                          type="button"
                          className="btn btn-icon"
                          onClick={() => removeSection(sectionIndex)}
                          aria-label="Delete section"
                        >
                          ×
                        </button>
                      </div>
                    </div>
                    {expanded && (
                      <div className="section-card-body">
                        {section.items.length === 0 ? (
                          <div className="empty-items">
                            <p className="muted">Belum ada item di seksi ini.</p>
                            <button
                              type="button"
                              className="btn btn-secondary"
                              onClick={() => addItem(sectionIndex)}
                            >
                              Add First Item
                            </button>
                          </div>
                        ) : (
                          <div className="item-table">
                            <div className="item-table-header">
                              <span></span>
                              <span>ID</span>
                              <span>Item Name</span>
                              <span>Detail</span>
                              <span className="actions-column">Actions</span>
                            </div>
                            {section.items.map((item, itemIndex) => {
                              const itemUid = item.uid ?? `${sectionUid}-${itemIndex}`;
                              const isDragOver =
                                dragItemOverRef?.sectionUid === sectionUid && dragItemOverRef?.itemUid === itemUid;
                              return (
                                <div
                                  key={itemUid}
                                  className={`item-table-row${isDragOver ? ' drag-over' : ''}`}
                                  draggable
                                  onDragStart={handleItemDragStart(sectionUid, itemUid)}
                                  onDragOver={handleItemDragOver(sectionUid, itemUid)}
                                  onDrop={handleItemDrop(sectionUid, itemUid)}
                                  onDragLeave={handleItemDragLeave(sectionUid, itemUid)}
                                  onDragEnd={handleItemDragEnd}
                                >
                                  <button className="item-drag-handle" aria-label="Drag to reorder item">
                                    ⋮⋮
                                  </button>
                                  <input
                                    className="form-input"
                                    value={item.itemId}
                                    onChange={e =>
                                      updateItem(sectionIndex, itemIndex, current => ({
                                        ...current,
                                        itemId: e.target.value,
                                      }))
                                    }
                                    placeholder="Item ID"
                                  />
                                  <input
                                    className="form-input"
                                    value={item.itemName}
                                    onChange={e =>
                                      updateItem(sectionIndex, itemIndex, current => ({
                                        ...current,
                                        itemName: e.target.value,
                                      }))
                                    }
                                    placeholder="Item name"
                                  />
                                  <input
                                    className="form-input"
                                    value={item.itemDetail}
                                    onChange={e =>
                                      updateItem(sectionIndex, itemIndex, current => ({
                                        ...current,
                                        itemDetail: e.target.value,
                                      }))
                                    }
                                    placeholder="Item detail"
                                  />
                                  <div className="item-row-actions">
                                    <button
                                      type="button"
                                      onClick={() => moveItem(sectionIndex, itemIndex, -1)}
                                      disabled={itemIndex === 0}
                                      aria-label="Move item up"
                                    >
                                      ↑
                                    </button>
                                    <button
                                      type="button"
                                      onClick={() => moveItem(sectionIndex, itemIndex, 1)}
                                      disabled={itemIndex === section.items.length - 1}
                                      aria-label="Move item down"
                                    >
                                      ↓
                                    </button>
                                    <button
                                      type="button"
                                      className="btn btn-icon"
                                      onClick={() => removeItem(sectionIndex, itemIndex)}
                                      aria-label="Delete item"
                                    >
                                      ×
                                    </button>
                                  </div>
                                </div>
                              );
                            })}
                            <div
                              className={`item-drop-zone${
                                dragItemOverRef?.sectionUid === sectionUid && dragItemOverRef?.itemUid === 'end'
                                  ? ' drag-over'
                                  : ''
                              }`}
                              onDragOver={handleItemDragOver(sectionUid, 'end')}
                              onDrop={handleItemDrop(sectionUid, 'end')}
                              onDragLeave={handleItemDragLeave(sectionUid, 'end')}
                            >
                              Drop here to place item at the end
                            </div>
                          </div>
                        )}
                        {section.items.length > 0 && (
                          <button
                            type="button"
                            className="btn btn-secondary"
                            onClick={() => addItem(sectionIndex)}
                          >
                            Add Item
                          </button>
                        )}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}

          {error && <p className="error">{error}</p>}
          {notice && <p className="success">{notice}</p>}
        </div>
      )}
    </div>
  );
}
