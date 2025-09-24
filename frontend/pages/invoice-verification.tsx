import { ChangeEvent, FormEvent, useMemo, useState } from 'react';
import { apiFetch } from '../lib/api';

interface InvoiceRow {
  vendorName: string;
  invoiceNumber: string;
  purchaseOrderNumber: string;
  totalAmount: string;
}

interface FieldResult {
  label: string;
  matched: boolean;
  provided: string | null;
  found: string | null;
  explanation?: string | null;
}

interface VerificationResponse {
  success: boolean;
  status: 'pass' | 'fail';
  message: string;
  fields: Record<string, FieldResult>;
  extractedText: string;
  explanations?: string[];
}

export default function InvoiceVerification() {
  const [form, setForm] = useState<InvoiceRow>({ vendorName: '', invoiceNumber: '', purchaseOrderNumber: '', totalAmount: '' });
  const [rows, setRows] = useState<InvoiceRow[]>([]);
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const [pdfFile, setPdfFile] = useState<File | null>(null);
  const [verification, setVerification] = useState<VerificationResponse | null>(null);
  const [error, setError] = useState('');
  const [fileError, setFileError] = useState('');
  const [isVerifying, setIsVerifying] = useState(false);
  const [showExtracted, setShowExtracted] = useState(false);

  const selectedRow = selectedIndex !== null ? rows[selectedIndex] : null;
  const fieldEntries = useMemo(() => (verification ? Object.entries(verification.fields) : []), [verification]);

  const updateForm = (key: keyof InvoiceRow, value: string) => {
    setForm(prev => ({ ...prev, [key]: value }));
  };

  const addRow = (event: FormEvent) => {
    event.preventDefault();
    setError('');
    setVerification(null);
    if (!form.vendorName.trim() || !form.invoiceNumber.trim() || !form.purchaseOrderNumber.trim() || !form.totalAmount.trim()) {
      setError('Please complete vendor name, invoice number, PO number, and total amount before adding to the table.');
      return;
    }

    setRows(prev => [...prev, { ...form }]);
    setSelectedIndex(rows.length);
    setForm({ vendorName: '', invoiceNumber: '', purchaseOrderNumber: '', totalAmount: '' });
  };

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    setVerification(null);
    setError('');
    setFileError('');
    if (!file) {
      setPdfFile(null);
      return;
    }

    if (file.type !== 'application/pdf' && !file.name.toLowerCase().endsWith('.pdf')) {
      setFileError('Only PDF files are supported.');
      setPdfFile(null);
      return;
    }

    setPdfFile(file);
  };

  const runVerification = async () => {
    setError('');
    setFileError('');
    setVerification(null);
    if (!pdfFile) {
      setFileError('Please upload an invoice PDF before running AI verification.');
      return;
    }

    if (!selectedRow) {
      setError('Please add invoice details to the table and select a row to verify.');
      return;
    }

    const payload = new FormData();
    payload.append('file', pdfFile);
    payload.append('vendorName', selectedRow.vendorName);
    payload.append('invoiceNumber', selectedRow.invoiceNumber);
    payload.append('purchaseOrderNumber', selectedRow.purchaseOrderNumber);
    payload.append('totalAmount', selectedRow.totalAmount);

    setIsVerifying(true);
    try {
      const response = await apiFetch('/api/invoices/verify', { method: 'POST', body: payload });
      const data = await response.json();
      if (!response.ok || data.success === false) {
        setError(data.message || 'Invoice verification failed.');
        return;
      }
      setVerification(data);
      setShowExtracted(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to verify invoice.');
    } finally {
      setIsVerifying(false);
    }
  };

  return (
    <div className="page-container invoice-page">
      <header className="page-header">
        <div>
          <h1>Invoice Verification</h1>
          <p>Compare invoice metadata with OCR results from the uploaded PDF.</p>
        </div>
      </header>

      <section className="card invoice-card">
        <h2>Invoice Metadata</h2>
        <p className="invoice-helper">Fill in the invoice information and add it to the table before running the AI check.</p>
        <form className="invoice-form" onSubmit={addRow}>
          <div>
            <label htmlFor="vendorName">Vendor Name</label>
            <input
              id="vendorName"
              className="form-input"
              value={form.vendorName}
              onChange={e => updateForm('vendorName', e.target.value)}
              placeholder="e.g. PT Nusantara Logistics"
            />
          </div>
          <div>
            <label htmlFor="invoiceNumber">Invoice Number</label>
            <input
              id="invoiceNumber"
              className="form-input"
              value={form.invoiceNumber}
              onChange={e => updateForm('invoiceNumber', e.target.value)}
              placeholder="e.g. INV-2024-001"
            />
          </div>
          <div>
            <label htmlFor="purchaseOrderNumber">PO Number</label>
            <input
              id="purchaseOrderNumber"
              className="form-input"
              value={form.purchaseOrderNumber}
              onChange={e => updateForm('purchaseOrderNumber', e.target.value)}
              placeholder="e.g. PO-7788"
            />
          </div>
          <div>
            <label htmlFor="totalAmount">Total Amount</label>
            <input
              id="totalAmount"
              className="form-input"
              value={form.totalAmount}
              onChange={e => updateForm('totalAmount', e.target.value)}
              placeholder="e.g. 1250000"
            />
          </div>
          <button className="btn btn-secondary" type="submit">Add to Table</button>
        </form>

        <div className="table-wrapper">
          <table className="table invoice-table">
            <thead>
              <tr>
                <th>Vendor</th>
                <th>Invoice Number</th>
                <th>PO Number</th>
                <th>Total Amount</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && (
                <tr>
                  <td colSpan={4} className="empty-cell">No invoice data yet. Add a row to begin.</td>
                </tr>
              )}
              {rows.map((row, index) => (
                <tr
                  key={`${row.invoiceNumber}-${index}`}
                  className={selectedIndex === index ? 'selected' : ''}
                  onClick={() => {
                    setSelectedIndex(index);
                    setVerification(null);
                  }}
                >
                  <td>{row.vendorName || '—'}</td>
                  <td>{row.invoiceNumber || '—'}</td>
                  <td>{row.purchaseOrderNumber || '—'}</td>
                  <td>{row.totalAmount || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card invoice-card">
        <h2>Invoice Document</h2>
        <div className="upload-row">
          <div className="file-input-wrapper">
            <label className="file-label" htmlFor="invoice-file">Invoice PDF</label>
            <input id="invoice-file" type="file" accept="application/pdf" onChange={handleFileChange} />
            {pdfFile && <span className="file-name">{pdfFile.name}</span>}
          </div>
          <button className="btn btn-primary" type="button" onClick={runVerification} disabled={isVerifying}>
            {isVerifying ? 'Verifying…' : 'Run AI Compare'}
          </button>
        </div>
        {(error || fileError) && <p className="error">{error || fileError}</p>}
      </section>

      {verification && (
        <section className="card invoice-card">
          <div className="verification-summary">
            <span className={`status-badge ${verification.status}`}>{verification.status === 'pass' ? 'Match' : 'Mismatch'}</span>
            <p>{verification.message}</p>
          </div>
          {verification.explanations && verification.explanations.length > 0 && (
            <ul className="verification-explanations">
              {verification.explanations.map((item, index) => (
                <li key={index}>{item}</li>
              ))}
            </ul>
          )}
          <div className="verification-grid">
            {fieldEntries.map(([key, field]) => (
              <div key={key} className={`verification-field ${field.matched ? 'matched' : 'mismatch'}`}>
                <div className="verification-title">
                  <span className={`indicator ${field.matched ? 'ok' : 'fail'}`}>{field.matched ? '✓' : '✕'}</span>
                  <h3>{field.label}</h3>
                </div>
                <dl>
                  <div>
                    <dt>Input</dt>
                    <dd>{field.provided ?? '—'}</dd>
                  </div>
                  <div>
                    <dt>Detected</dt>
                    <dd>{field.found ?? 'Not found in PDF'}</dd>
                  </div>
                </dl>
                {field.explanation && <p className="verification-explanation">{field.explanation}</p>}
              </div>
            ))}
          </div>
          <button className="btn btn-secondary" type="button" onClick={() => setShowExtracted(v => !v)}>
            {showExtracted ? 'Hide OCR Text' : 'Show OCR Text'}
          </button>
          {showExtracted && (
            <pre className="extracted-preview">{verification.extractedText || 'No text extracted.'}</pre>
          )}
        </section>
      )}
    </div>
  );
}
