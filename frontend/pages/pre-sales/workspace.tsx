import { useEffect, useMemo, useState } from 'react';
import { apiFetch } from '../../lib/api';

interface ProjectTemplateMetadata {
  id: number;
  templateName: string;
}

interface AssessmentItem {
  itemId: string;
  itemName: string;
  itemDetail?: string;
  isNeeded: boolean;
  estimates: Record<string, number | null>;
}

interface AssessmentSection {
  sectionName: string;
  items: AssessmentItem[];
}

interface ProjectAssessment {
  id?: number;
  templateId: number;
  sections: AssessmentSection[];
}

export default function AssessmentWorkspace() {
  const [templates, setTemplates] = useState<ProjectTemplateMetadata[]>([]);
  const [selectedTemplate, setSelectedTemplate] = useState<number | ''>('');
  const [file, setFile] = useState<File | null>(null);
  const [analysisLog, setAnalysisLog] = useState<string[]>([]);
  const [progress, setProgress] = useState(0);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [assessment, setAssessment] = useState<ProjectAssessment | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const loadTemplates = async () => {
      try {
        const res = await apiFetch('/api/templates');
        if (!res.ok) throw new Error(await res.text());
        const data: ProjectTemplateMetadata[] = await res.json();
        setTemplates(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load templates');
      }
    };
    loadTemplates();
  }, []);

  const estimationColumns = useMemo(() => {
    if (!assessment) return [];
    const firstItem = assessment.sections.flatMap(s => s.items).find(() => true);
    return firstItem ? Object.keys(firstItem.estimates) : [];
  }, [assessment]);

  const toggleSection = (sectionName: string) => {
    setExpanded(prev => ({
      ...prev,
      [sectionName]: !prev[sectionName],
    }));
  };

  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (!assessment) return;
    const defaults: Record<string, boolean> = {};
    assessment.sections.forEach(section => {
      defaults[section.sectionName] = true;
    });
    setExpanded(defaults);
  }, [assessment]);

  const startAnalysis = async () => {
    if (!selectedTemplate || !file) {
      setError('Pilih template dan unggah dokumen scope terlebih dahulu.');
      return;
    }
    setError('');
    setNotice('');
    setIsAnalyzing(true);
    setAnalysisLog(['Menyiapkan dokumen…']);
    setProgress(10);
    let success = false;
    try {
      const formData = new FormData();
      formData.append('templateId', String(selectedTemplate));
      formData.append('file', file);
      const res = await apiFetch('/api/assessment/analyze', {
        method: 'POST',
        body: formData,
      });
      if (!res.ok) throw new Error(await res.text());
      const data: ProjectAssessment = await res.json();
      setAssessment(data);
      setAnalysisLog(log => [...log, 'Menjalankan analisis AI…', 'Mengompilasi hasil akhir…', 'Analisis selesai.']);
      setProgress(100);
      setNotice('Analisis selesai. Review hasil dan lakukan penyesuaian jika diperlukan.');
      success = true;
    } catch (err) {
      setAnalysisLog(log => [...log, 'Analisis gagal.']);
      setError(err instanceof Error ? err.message : 'Gagal menjalankan analisis');
    } finally {
      setIsAnalyzing(false);
      if (!success) {
        setProgress(0);
      }
    }
  };

  const updateItem = (sectionIndex: number, itemIndex: number, updater: (item: AssessmentItem) => AssessmentItem) => {
    setAssessment(prev => {
      if (!prev) return prev;
      const nextSections = prev.sections.map((section, sIndex) => {
        if (sIndex !== sectionIndex) return section;
        const items = section.items.map((item, iIndex) => (iIndex === itemIndex ? updater(item) : item));
        return { ...section, items };
      });
      return { ...prev, sections: nextSections };
    });
  };

  const computeItemTotal = (item: AssessmentItem) => {
    if (!item.isNeeded) return 0;
    return Object.values(item.estimates).reduce((total: number, value) => total + (value ?? 0), 0);
  };

  const computeSectionTotal = (section: AssessmentSection) => {
    return section.items.reduce((sum, item) => sum + computeItemTotal(item), 0);
  };

  const grandTotal = useMemo(() => {
    if (!assessment) return 0;
    return assessment.sections.reduce((sum, section) => sum + computeSectionTotal(section), 0);
  }, [assessment]);

  const saveAssessment = async () => {
    if (!assessment) return;
    setSaving(true);
    setError('');
    setNotice('');
    try {
      const res = await apiFetch('/api/assessment/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(assessment),
      });
      if (!res.ok) throw new Error(await res.text());
      const saved: ProjectAssessment = await res.json();
      setAssessment(saved);
      setNotice('Assessment berhasil disimpan.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Gagal menyimpan assessment');
    } finally {
      setSaving(false);
    }
  };

  const exportAssessment = async () => {
    if (!assessment?.id) return;
    setError('');
    try {
      const res = await apiFetch(`/api/assessment/${assessment.id}/export`);
      if (!res.ok) throw new Error(await res.text());
      const blob = await res.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `assessment-${assessment.id}.xlsx`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Gagal mengekspor assessment');
    }
  };

  return (
    <div className="page-container">
      <div className="page-header">
        <div>
          <h1>Assessment Workspace</h1>
          <p>Jalankan analisis AI, review hasil, dan finalisasi estimasi proyek.</p>
        </div>
      </div>

      <section className="card" style={{ marginBottom: '24px' }}>
        <h2>Inisiasi Analisis</h2>
        <div className="form-grid">
          <label>
            <span>Project Template</span>
            <select
              className="form-select"
              value={selectedTemplate}
              onChange={e => setSelectedTemplate(e.target.value ? Number(e.target.value) : '')}
            >
              <option value="">Pilih template</option>
              {templates.map(t => (
                <option key={t.id} value={t.id}>{t.templateName}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Scope Document</span>
            <input type="file" accept=".pdf,.doc,.docx" onChange={e => setFile(e.target.files?.[0] ?? null)} />
          </label>
          <div className="form-actions">
            <button className="btn btn-primary" onClick={startAnalysis} disabled={isAnalyzing || !selectedTemplate || !file}>
              {isAnalyzing ? 'Processing…' : 'Start Analysis'}
            </button>
          </div>
        </div>
        {isAnalyzing && (
          <div className="progress">
            <div className="progress-bar" style={{ width: `${progress}%` }}></div>
          </div>
        )}
        {analysisLog.length > 0 && (
          <ul className="analysis-log">
            {analysisLog.map((entry, index) => (
              <li key={`${entry}-${index}`}>{entry}</li>
            ))}
          </ul>
        )}
      </section>

      {assessment && (
        <section className="card">
          <div className="workspace-actions">
            <button className="btn btn-primary" onClick={saveAssessment} disabled={saving}>
              {saving ? 'Saving…' : 'Save Assessment'}
            </button>
            <button className="btn btn-secondary" onClick={exportAssessment} disabled={!assessment.id}>
              Export to Excel
            </button>
          </div>

          <div className="assessment-grid">
            {assessment.sections.map((section, sectionIndex) => (
              <div className="assessment-section" key={section.sectionName}>
                <button className="section-toggle" onClick={() => toggleSection(section.sectionName)}>
                  <span>{expanded[section.sectionName] === false ? '▶' : '▼'}</span>
                  <strong>{section.sectionName}</strong>
                  <span className="section-total">{computeSectionTotal(section).toLocaleString()}</span>
                </button>
                {expanded[section.sectionName] !== false && (
                  <table className="assessment-table">
                    <thead>
                      <tr>
                        <th>Item ID</th>
                        <th>Item Name</th>
                        <th>Needed?</th>
                        <th>Detail</th>
                        {estimationColumns.map(column => (
                          <th key={column}>{column}</th>
                        ))}
                        <th>Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      {section.items.map((item, itemIndex) => (
                        <tr key={item.itemId || `${section.sectionName}-${itemIndex}`}>
                          <td>{item.itemId}</td>
                          <td>{item.itemName}</td>
                          <td>
                            <input
                              type="checkbox"
                              checked={item.isNeeded}
                              onChange={e => updateItem(sectionIndex, itemIndex, current => ({
                                ...current,
                                isNeeded: e.target.checked,
                              }))}
                            />
                          </td>
                          <td>{item.itemDetail}</td>
                          {estimationColumns.map(column => (
                            <td key={column}>
                              <input
                                type="number"
                                className="form-input"
                                value={item.estimates[column] ?? ''}
                                disabled={!item.isNeeded}
                                onChange={e => {
                                  const value = e.target.value === '' ? null : Number(e.target.value);
                                  if (Number.isNaN(value)) return;
                                  updateItem(sectionIndex, itemIndex, current => ({
                                    ...current,
                                    estimates: { ...current.estimates, [column]: value },
                                  }));
                                }}
                              />
                            </td>
                          ))}
                          <td className="numeric">{computeItemTotal(item).toLocaleString()}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            ))}
          </div>

          <div className="grand-total">
            <span>Grand Total Manhours</span>
            <strong>{grandTotal.toLocaleString()}</strong>
          </div>
        </section>
      )}

      {error && <p className="error">{error}</p>}
      {notice && <p className="success">{notice}</p>}
    </div>
  );
}
