import { DragEvent, FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import Modal from '../../components/Modal';
import { apiFetch } from '../../lib/api';

interface StagedFile {
  id: string;
  file: File;
  projectName: string;
  clientType: string;
  status: 'ready' | 'error';
  errorMessage?: string;
}

interface KnowledgeBaseDocument {
  id: number;
  originalFileName: string;
  projectName?: string | null;
  documentType?: string | null;
  processingStatus: string;
  errorMessage?: string | null;
  dateProcessed?: string | null;
  processedAt?: string | null;
  uploadedAt?: string | null;
  updatedAt?: string | null;
}

interface UploadFeedback {
  type: 'success' | 'error';
  message: string;
}

const clientTypeOptions = [
  'CR',
  'New Application',
  'Enhancement',
  'Upgrade',
  'Migration',
  'Maintenance',
  'Other',
];

function formatBytes(bytes: number) {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  const value = bytes / Math.pow(1024, exponent);
  return `${value.toFixed(value >= 10 || exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}

function normalizeStatus(status: string) {
  const normalized = status.toLowerCase();
  if (normalized.includes('fail')) {
    return { label: 'Failed', className: 'status-failed' };
  }
  if (normalized.includes('success') || normalized.includes('indexed')) {
    return { label: 'Successfully Indexed', className: 'status-success' };
  }
  if (normalized.includes('pending')) {
    return { label: 'Pending', className: 'status-pending' };
  }
  return { label: 'Processing', className: 'status-processing' };
}

function pickProcessedDate(doc: KnowledgeBaseDocument) {
  return doc.dateProcessed || doc.processedAt || doc.updatedAt || doc.uploadedAt || '';
}

function ConfirmDialog({
  isOpen,
  title,
  message,
  confirmLabel,
  confirmLoading,
  onConfirm,
  onCancel,
}: {
  isOpen: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  confirmLoading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <Modal
      isOpen={isOpen}
      onClose={onCancel}
      title={title}
      footer={
        <div className="modal-footer-actions">
          <button className="btn secondary" onClick={onCancel} disabled={confirmLoading}>
            Cancel
          </button>
          <button className="btn btn-danger" onClick={onConfirm} disabled={confirmLoading}>
            {confirmLoading ? 'Processing…' : confirmLabel}
          </button>
        </div>
      }
    >
      <p>{message}</p>
    </Modal>
  );
}

export default function PresalesAiHistoryPage() {
  const [dragActive, setDragActive] = useState(false);
  const [stagedFiles, setStagedFiles] = useState<StagedFile[]>([]);
  const [feedback, setFeedback] = useState<UploadFeedback | null>(null);
  const [isProcessing, setIsProcessing] = useState(false);
  const [documents, setDocuments] = useState<KnowledgeBaseDocument[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(false);
  const [documentsError, setDocumentsError] = useState('');
  const [deleteTarget, setDeleteTarget] = useState<KnowledgeBaseDocument | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [reprocessingId, setReprocessingId] = useState<number | null>(null);

  const readyFiles = useMemo(() => stagedFiles.filter(file => file.status === 'ready'), [stagedFiles]);

  const handleFiles = useCallback((files: File[]) => {
    setFeedback(null);
    setStagedFiles(prev => {
      const existingNames = new Set(prev.map(item => item.file.name + item.file.size));
      const next: StagedFile[] = [...prev];
      files.forEach(file => {
        const key = file.name + file.size;
        if (existingNames.has(key)) return;
        if (!file.name.toLowerCase().endsWith('.pdf')) {
          next.push({
            id: `${Date.now()}-${Math.random()}`,
            file,
            projectName: '',
            clientType: '',
            status: 'error',
            errorMessage: 'File format not supported. Only .pdf is allowed.',
          });
          existingNames.add(key);
          return;
        }
        next.push({
          id: `${Date.now()}-${Math.random()}`,
          file,
          projectName: '',
          clientType: '',
          status: 'ready',
        });
        existingNames.add(key);
      });
      return next;
    });
  }, []);

  const onDrop = useCallback(
    (event: DragEvent<HTMLDivElement>) => {
      event.preventDefault();
      event.stopPropagation();
      setDragActive(false);
      const files = Array.from(event.dataTransfer?.files ?? []);
      if (files.length > 0) handleFiles(files);
    },
    [handleFiles]
  );

  const onBrowse = useCallback((event: FormEvent<HTMLInputElement>) => {
    const files = Array.from(event.currentTarget.files ?? []);
    if (files.length > 0) handleFiles(files);
    event.currentTarget.value = '';
  }, [handleFiles]);

  const removeStagedFile = useCallback((id: string) => {
    setStagedFiles(prev => prev.filter(item => item.id !== id));
  }, []);

  const updateStagedMetadata = useCallback((id: string, field: 'projectName' | 'clientType', value: string) => {
    setStagedFiles(prev => prev.map(item => (item.id === id ? { ...item, [field]: value } : item)));
  }, []);

  const clearStaged = useCallback(() => {
    setStagedFiles([]);
    setFeedback(null);
  }, []);

  const loadDocuments = useCallback(async () => {
    setDocumentsLoading(true);
    setDocumentsError('');
    try {
      const response = await apiFetch('/api/knowledge-base/documents');
      if (!response.ok) throw new Error('Unable to load documents.');
      const data: KnowledgeBaseDocument[] = await response.json();
      setDocuments(data);
    } catch (error) {
      setDocumentsError(error instanceof Error ? error.message : 'Unexpected error loading documents.');
    } finally {
      setDocumentsLoading(false);
    }
  }, []);

  const submitUpload = useCallback(async () => {
    if (readyFiles.length === 0) return;
    setIsProcessing(true);
    setFeedback(null);
    try {
      const formData = new FormData();
      const metadataPayload = readyFiles.map(item => ({
        projectName: item.projectName || undefined,
        clientType: item.clientType || undefined,
        originalFileName: item.file.name,
      }));
      readyFiles.forEach(item => {
        formData.append('files', item.file, item.file.name);
      });
      formData.append('metadata', JSON.stringify(metadataPayload));
      const response = await apiFetch('/api/knowledge-base/upload', {
        method: 'POST',
        body: formData,
      });
      if (!response.ok) {
        let message = 'Unable to process files.';
        try {
          const data = await response.json();
          message = data?.detail || data?.message || message;
        } catch {
          try {
            message = await response.text();
          } catch {
            /* ignore */
          }
        }
        throw new Error(message || 'Upload failed');
      }
      setFeedback({ type: 'success', message: 'Processing started. Documents will appear below once indexing completes.' });
      clearStaged();
      await loadDocuments();
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to submit documents.';
      setFeedback({ type: 'error', message });
    } finally {
      setIsProcessing(false);
    }
  }, [clearStaged, loadDocuments, readyFiles]);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  const confirmDelete = useCallback(async () => {
    if (!deleteTarget) return;
    setIsDeleting(true);
    try {
      const response = await apiFetch(`/api/knowledge-base/documents/${deleteTarget.id}`, { method: 'DELETE' });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || 'Failed to delete document.');
      }
      setDeleteTarget(null);
      await loadDocuments();
    } catch (error) {
      setFeedback({ type: 'error', message: error instanceof Error ? error.message : 'Failed to delete document.' });
    } finally {
      setIsDeleting(false);
    }
  }, [deleteTarget, loadDocuments]);

  const triggerReprocess = useCallback(
    async (documentId: number) => {
      setReprocessingId(documentId);
      setFeedback(null);
      try {
        const response = await apiFetch(`/api/knowledge-base/documents/${documentId}/reprocess`, { method: 'POST' });
        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || 'Failed to re-process document.');
        }
        setFeedback({ type: 'success', message: 'Document has been queued for re-processing.' });
        await loadDocuments();
      } catch (error) {
        setFeedback({ type: 'error', message: error instanceof Error ? error.message : 'Failed to re-process document.' });
      } finally {
        setReprocessingId(null);
      }
    },
    [loadDocuments]
  );

  return (
    <div className="page-container presales-history-page">
      <div className="page-header">
        <div>
          <h1>Presales AI History</h1>
          <p className="hint">Upload and curate historical assessments to enrich the knowledge base.</p>
        </div>
        <button className="btn secondary" onClick={loadDocuments} disabled={documentsLoading}>
          {documentsLoading ? 'Refreshing…' : 'Refresh List'}
        </button>
      </div>

      <section className="card">
        <div className="card-header">
          <h2>Upload Historical Documents</h2>
          <p className="hint">Drag &amp; drop PDF files or browse from your device. Metadata helps improve retrieval quality.</p>
        </div>
        <div
          className={`kb-upload-zone${dragActive ? ' drag-active' : ''}`}
          onDragOver={event => {
            event.preventDefault();
            setDragActive(true);
          }}
          onDragLeave={event => {
            event.preventDefault();
            setDragActive(false);
          }}
          onDrop={onDrop}
        >
          <input id="kb-file-input" type="file" multiple accept="application/pdf" onChange={onBrowse} />
          <label htmlFor="kb-file-input">
            <span className="upload-icon">📂</span>
            <span className="upload-text">Drag &amp; drop PDF files here or <strong>click to browse</strong></span>
            <span className="upload-hint">Accepted format: .pdf</span>
          </label>
        </div>

        {stagedFiles.length > 0 && (
          <div className="kb-staged-list">
            {stagedFiles.map(item => (
              <div key={item.id} className="kb-staged-item">
                <div className="kb-staged-header">
                  <div>
                    <strong>{item.file.name}</strong>
                    <span className="kb-file-size">{formatBytes(item.file.size)}</span>
                  </div>
                  <div className="kb-staged-actions">
                    {item.status === 'ready' ? (
                      <span className="status-badge status-ready">Ready for upload</span>
                    ) : (
                      <span className="status-badge status-failed" title={item.errorMessage}>
                        {item.errorMessage ?? 'Not supported'}
                      </span>
                    )}
                    <button className="icon-button" onClick={() => removeStagedFile(item.id)} title="Remove from list">
                      ✕
                    </button>
                  </div>
                </div>
                {item.status === 'ready' && (
                  <div className="kb-metadata-grid">
                    <label htmlFor={`project-${item.id}`}>Project Name</label>
                    <input
                      id={`project-${item.id}`}
                      type="text"
                      value={item.projectName}
                      placeholder="e.g. Project Phoenix"
                      onChange={event => updateStagedMetadata(item.id, 'projectName', event.target.value)}
                    />
                    <label htmlFor={`client-${item.id}`}>Client Type</label>
                    <select
                      id={`client-${item.id}`}
                      value={item.clientType}
                      onChange={event => updateStagedMetadata(item.id, 'clientType', event.target.value)}
                    >
                      <option value="">Select client type (optional)</option>
                      {clientTypeOptions.map(option => (
                        <option key={option} value={option}>{option}</option>
                      ))}
                    </select>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        <div className="kb-upload-actions">
          <button className="btn secondary" onClick={clearStaged} disabled={stagedFiles.length === 0 || isProcessing}>
            Clear List
          </button>
          <button
            className="btn btn-primary"
            onClick={submitUpload}
            disabled={readyFiles.length === 0 || isProcessing}
          >
            {isProcessing ? 'Processing…' : 'Process & Add to Knowledge Base'}
          </button>
        </div>
        {feedback && (
          <p className={feedback.type === 'error' ? 'error' : 'success'}>{feedback.message}</p>
        )}
      </section>

      <section className="card">
        <div className="card-header">
          <h2>Knowledge Base Documents</h2>
          <p className="hint">Monitor the processing status of indexed documents and manage their lifecycle.</p>
        </div>
        {documentsError && <p className="error">{documentsError}</p>}
        <div className="kb-table-wrapper">
          <table className="kb-table">
            <thead>
              <tr>
                <th>File Name</th>
                <th>Project Name</th>
                <th>Document Type</th>
                <th>Date Processed</th>
                <th>Status</th>
                <th className="kb-actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {documentsLoading ? (
                <tr>
                  <td colSpan={6} className="kb-empty">Loading documents…</td>
                </tr>
              ) : documents.length === 0 ? (
                <tr>
                  <td colSpan={6} className="kb-empty">No documents found yet.</td>
                </tr>
              ) : (
                documents.map(doc => {
                  const status = normalizeStatus(doc.processingStatus ?? '');
                  const processedDate = pickProcessedDate(doc);
                  const processedDateValue = processedDate ? new Date(processedDate) : null;
                  const processedDateLabel = processedDateValue && !Number.isNaN(processedDateValue.getTime())
                    ? processedDateValue.toLocaleString()
                    : '—';
                  return (
                    <tr key={doc.id}>
                      <td>{doc.originalFileName}</td>
                      <td>{doc.projectName ?? '—'}</td>
                      <td>{doc.documentType ?? '—'}</td>
                      <td>{processedDateLabel}</td>
                      <td>
                        <span className={`status-badge ${status.className}`} title={doc.errorMessage ?? undefined}>
                          {status.label}
                        </span>
                        {status.className === 'status-failed' && doc.errorMessage && (
                          <span className="kb-error-tooltip">ℹ {doc.errorMessage}</span>
                        )}
                      </td>
                      <td className="kb-actions-column">
                        <div className="kb-row-actions">
                          <button
                            className="icon-button danger"
                            onClick={() => setDeleteTarget(doc)}
                            title="Delete document"
                            disabled={isDeleting && deleteTarget?.id === doc.id}
                          >
                            🗑️
                          </button>
                          <button
                            className="icon-button"
                            onClick={() => triggerReprocess(doc.id)}
                            title="Re-process document"
                            disabled={reprocessingId === doc.id}
                          >
                            {reprocessingId === doc.id ? '⏳' : '🔁'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </section>

      <ConfirmDialog
        isOpen={!!deleteTarget}
        title="Remove document from knowledge base"
        message={
          deleteTarget
            ? `Are you sure you want to delete "${deleteTarget.originalFileName}" and remove all associated vectors?`
            : ''
        }
        confirmLabel="Delete"
        confirmLoading={isDeleting}
        onConfirm={confirmDelete}
        onCancel={() => {
          if (!isDeleting) setDeleteTarget(null);
        }}
      />
    </div>
  );
}
