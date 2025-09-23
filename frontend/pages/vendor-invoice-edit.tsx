import Head from 'next/head';
import Link from 'next/link';
import { ChangeEvent, FormEvent, useEffect, useRef, useState } from 'react';

interface SidebarLink {
  href: string;
  label: string;
  icon: string;
  key: string;
}

const vendorLinks: { title: string; links: SidebarLink[] }[] = [
  {
    title: 'Vendor Portal',
    links: [
      { href: '#', label: 'Published GRNs', icon: 'fa-archive', key: 'vendor-grn-list' },
      { href: '#', label: 'Commercial Invoice Submission', icon: 'fa-upload', key: 'vendor-invoice-create' },
      { href: '#', label: 'Awaiting Tax Invoice', icon: 'fa-file-text-o', key: 'vendor-tax-invoice-index' },
      { href: '/vendor-invoice-edit', label: 'Commercial Invoice Revision', icon: 'fa-repeat', key: 'vendor-invoice-edit' },
      { href: '#', label: 'Tax Invoice Fix', icon: 'fa-file-text', key: 'vendor-tax-invoice-edit' },
      { href: '#', label: 'Data Change Request', icon: 'fa-id-badge', key: 'vendor-dcr-create' },
      { href: '#', label: 'Non-PO Submission', icon: 'fa-qrcode', key: 'vendor-non-po-create' },
    ],
  },
  {
    title: 'General',
    links: [
      { href: '/login', label: 'Logout', icon: 'fa-sign-out', key: 'logout' },
      { href: '#', label: 'Back to UI Index', icon: 'fa-arrow-left', key: 'ui-index' },
    ],
  },
];

const rejectionDetails = [
  { label: 'Invoice Number', value: 'INV-2024-2999' },
  { label: 'Reviewed By', value: 'Finance Supervisor' },
  {
    label: 'Reason',
    value: 'Mismatch between GRN totals and invoice grand total. Please correct the amount.',
  },
  { label: 'Requested On', value: '03 Jun 2024, 09:17' },
];

export default function VendorInvoiceEditPage() {
  const [notes, setNotes] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [formValidated, setFormValidated] = useState(false);
  const [fileError, setFileError] = useState('');
  const [submitMessage, setSubmitMessage] = useState('');
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    document.body.classList.add('invoice-revision-body');
    return () => {
      document.body.classList.remove('invoice-revision-body');
    };
  }, []);

  useEffect(() => {
    const closeOnOutsideClick = (event: MouseEvent) => {
      if (!dropdownRef.current) return;
      if (!dropdownRef.current.contains(event.target as Node)) {
        setDropdownOpen(false);
      }
    };

    if (dropdownOpen) {
      window.addEventListener('click', closeOnOutsideClick);
    }

    return () => {
      window.removeEventListener('click', closeOnOutsideClick);
    };
  }, [dropdownOpen]);

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    setSubmitMessage('');
    setFileError('');
    setFormValidated(false);
    const selected = event.target.files?.[0] ?? null;
    if (selected && selected.type !== 'application/pdf' && !selected.name.toLowerCase().endsWith('.pdf')) {
      setFile(null);
      setFileError('Only PDF files are supported.');
      return;
    }
    setFile(selected);
  };

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setSubmitMessage('');
    setFormValidated(true);

    if (!file) {
      setFileError('Please upload the corrected commercial invoice.');
      return;
    }

    setFileError('');
    setSubmitMessage('Your revised invoice has been queued for review.');
  };

  return (
    <div className="invoice-revision-page" data-page="vendor-invoice-edit">
      <Head>
        <title>Astemo Invoice Portal - Commercial Invoice Revision</title>
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <link
          href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css"
          rel="stylesheet"
          integrity="sha384-X2b6xmA5E9O+V1YxAECEV4VpWwfvX2gYdEx+kt1/3uzMdGII4XESyqCCX5p1GfUv"
          crossOrigin="anonymous"
        />
        <link
          rel="stylesheet"
          href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/4.7.0/css/font-awesome.min.css"
          integrity="sha512-SfZ8kd0vVYt1vVbWcv16O3MHuvb6jVWeItPxX2VINeodIZ6Tn6PvxI6Bfq5lHppZArYAjS4C6+XJQGqY8JStA=="
          crossOrigin="anonymous"
          referrerPolicy="no-referrer"
        />
        <link
          href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"
          rel="stylesheet"
        />
      </Head>

      <div className="wrapper">
        <nav id="sidebar">
          <div className="sidebar-header">
            <h3>Astemo</h3>
            <p>IDBM Invoice Portal</p>
          </div>
          <ul className="nav flex-column">
            {vendorLinks.map(section => (
              <li key={section.title} className="sidebar-section">
                <div className="sidebar-section-label">{section.title}</div>
                <ul className="nav flex-column sidebar-subnav">
                  {section.links.map(link => {
                    const isActive = link.key === 'vendor-invoice-edit';
                    const linkClass = `nav-link${isActive ? ' active' : ''}`;
                    const content = (
                      <>
                        <i className={`fa ${link.icon}`} aria-hidden="true"></i>
                        <span>{link.label}</span>
                      </>
                    );
                    return (
                      <li key={link.key} className="nav-item">
                        {link.href.startsWith('/') ? (
                          <Link href={link.href} className={linkClass}>
                            {content}
                          </Link>
                        ) : (
                          <a href={link.href} className={linkClass}>
                            {content}
                          </a>
                        )}
                      </li>
                    );
                  })}
                </ul>
              </li>
            ))}
          </ul>
        </nav>

        <div id="content">
          <nav className="navbar navbar-expand-lg navbar-light bg-light">
            <div className="container-fluid">
              <span className="navbar-brand mb-0 h1">VENDOR WORKSPACE</span>
              <div className="d-flex ms-auto">
                <div className={`dropdown${dropdownOpen ? ' show' : ''}`} ref={dropdownRef}>
                  <button
                    className="btn btn-light dropdown-toggle"
                    type="button"
                    aria-expanded={dropdownOpen}
                    onClick={() => setDropdownOpen(prev => !prev)}
                  >
                    <i className="fa fa-user-circle me-2" aria-hidden="true"></i>
                    Ayu Paramita
                  </button>
                  <ul className={`dropdown-menu dropdown-menu-end${dropdownOpen ? ' show' : ''}`}>
                    <li><span className="dropdown-item-text text-muted">PT Sakura Komponent</span></li>
                    <li><hr className="dropdown-divider" /></li>
                    <li><a className="dropdown-item" href="#">Profile</a></li>
                    <li><a className="dropdown-item" href="#">Settings</a></li>
                    <li><hr className="dropdown-divider" /></li>
                    <li><a className="dropdown-item" href="/login">Logout</a></li>
                  </ul>
                </div>
              </div>
            </div>
          </nav>

          <main className="main-content">
            <div className="page-heading">
              <nav aria-label="breadcrumb">
                <ol className="breadcrumb">
                  <li className="breadcrumb-item"><a href="#">Commercial Invoice Revision</a></li>
                  <li className="breadcrumb-item active" aria-current="page">Resubmit Rejected Invoice</li>
                </ol>
              </nav>
              <h1 className="page-title">Resubmit Rejected Invoice</h1>
              <p className="page-subtitle">
                Review the rejection reason, correct the document, and upload the revised commercial invoice.
              </p>
            </div>

            {submitMessage && (
              <div className="alert alert-success" role="status">
                <i className="fa fa-check-circle me-2" aria-hidden="true"></i>
                {submitMessage}
              </div>
            )}

            <div className="card mb-4">
              <div className="card-header">
                <h2 className="card-title h5 mb-0">Rejection Details</h2>
              </div>
              <dl className="data-list">
                {rejectionDetails.map(detail => (
                  <div key={detail.label} className="data-row">
                    <dt>{detail.label}</dt>
                    <dd>{detail.value}</dd>
                  </div>
                ))}
              </dl>
            </div>

            <div className="card">
              <div className="card-header">
                <h2 className="card-title h5 mb-0">Upload Corrected Invoice</h2>
              </div>
              <div className="card-body">
                <form className={`row g-4${formValidated ? ' was-validated' : ''}`} noValidate onSubmit={handleSubmit}>
                  <div className="col-12">
                    <label className="form-label" htmlFor="revision-notes">Vendor Notes to Reviewer</label>
                    <textarea
                      id="revision-notes"
                      className="form-control"
                      rows={4}
                      placeholder="Optional: explain what was corrected"
                      value={notes}
                      onChange={event => setNotes(event.target.value)}
                    ></textarea>
                  </div>
                  <div className="col-12">
                    <label className="form-label" htmlFor="revision-file">
                      Upload Corrected Commercial Invoice PDF<span className="text-danger">*</span>
                    </label>
                    <input
                      type="file"
                      id="revision-file"
                      className={`form-control${(formValidated && !file) || fileError ? ' is-invalid' : ''}`}
                      accept="application/pdf"
                      onChange={handleFileChange}
                    />
                    <div className="form-help">Ensure the PDF reflects the accurate amounts and includes necessary signatures.</div>
                    <div className={`invalid-feedback${(formValidated && !file) || fileError ? ' d-block' : ''}`}>
                      {fileError || 'Please upload the corrected commercial invoice.'}
                    </div>
                  </div>
                  <div className="col-12">
                    <div className="form-actions">
                      <a href="#" className="btn btn-outline-secondary">
                        <i className="fa fa-times me-2" aria-hidden="true"></i>
                        Cancel
                      </a>
                      <button type="submit" className="btn btn-primary">
                        <i className="fa fa-upload me-2" aria-hidden="true"></i>
                        Submit Revision
                      </button>
                    </div>
                  </div>
                </form>
              </div>
            </div>
          </main>
        </div>
      </div>

      <style jsx global>{`
        body.invoice-revision-body {
          background-color: #f4f7f9;
          color: #212529;
          font-family: 'Inter', sans-serif;
        }
        .invoice-revision-page {
          --sidebar-width: 260px;
          --sidebar-bg: #212529;
          --sidebar-link-color: #adb5bd;
          --sidebar-link-hover-color: #f8f9fa;
          --sidebar-link-active-color: #ffffff;
          --sidebar-link-active-bg: #0d6efd;
        }
        .invoice-revision-page .wrapper {
          display: flex;
          width: 100%;
          min-height: 100vh;
        }
        .invoice-revision-page #sidebar {
          width: var(--sidebar-width);
          min-width: var(--sidebar-width);
          background: var(--sidebar-bg);
          color: #ffffff;
          transition: all 0.3s ease;
        }
        .invoice-revision-page #sidebar .sidebar-header {
          padding: 20px;
          background: #1a1d20;
          text-align: center;
        }
        .invoice-revision-page #sidebar .sidebar-header h3 {
          font-size: 1.5rem;
          margin-bottom: 0.25rem;
        }
        .invoice-revision-page #sidebar .sidebar-header p {
          font-size: 0.9rem;
          color: #6c757d;
          margin: 0;
        }
        .invoice-revision-page #sidebar .nav {
          padding: 0;
        }
        .invoice-revision-page #sidebar .sidebar-section {
          list-style: none;
        }
        .invoice-revision-page #sidebar .sidebar-section-label {
          padding: 0.75rem 1.5rem;
          font-size: 0.75rem;
          color: #6c757d;
          text-transform: uppercase;
          font-weight: 700;
          letter-spacing: 0.5px;
        }
        .invoice-revision-page #sidebar .sidebar-subnav {
          list-style: none;
          margin: 0;
          padding: 0;
        }
        .invoice-revision-page #sidebar .sidebar-subnav .nav-item {
          list-style: none;
        }
        .invoice-revision-page #sidebar .nav-link {
          display: flex;
          align-items: center;
          gap: 0.75rem;
          padding: 0.75rem 1.5rem;
          color: var(--sidebar-link-color);
          font-size: 0.95rem;
          border-left: 3px solid transparent;
          transition: background-color 0.2s ease, color 0.2s ease;
        }
        .invoice-revision-page #sidebar .nav-link:hover {
          color: var(--sidebar-link-hover-color);
          background: #343a40;
          border-left-color: #495057;
        }
        .invoice-revision-page #sidebar .nav-link.active {
          color: var(--sidebar-link-active-color);
          background: var(--sidebar-link-active-bg);
          border-left-color: #ffffff;
        }
        .invoice-revision-page #sidebar .nav-link .fa {
          width: 20px;
          text-align: center;
        }
        .invoice-revision-page #content {
          width: 100%;
          min-height: 100vh;
          background: transparent;
        }
        .invoice-revision-page .navbar {
          padding: 1rem 1.5rem;
          background-color: #ffffff;
          border-bottom: 1px solid #dee2e6;
          box-shadow: 0 2px 4px rgba(0, 0, 0, 0.05);
        }
        .invoice-revision-page .main-content {
          padding: 2rem;
        }
        .invoice-revision-page .page-heading {
          margin-bottom: 2rem;
        }
        .invoice-revision-page .page-title {
          font-size: 1.75rem;
          font-weight: 700;
          margin-bottom: 0.5rem;
        }
        .invoice-revision-page .page-subtitle {
          font-size: 1rem;
          color: #6c757d;
        }
        .invoice-revision-page .breadcrumb {
          font-size: 0.875rem;
          margin-bottom: 0.5rem;
        }
        .invoice-revision-page .card {
          border: none;
          box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
          border-radius: 0.5rem;
        }
        .invoice-revision-page .card-header,
        .invoice-revision-page .card-footer {
          background-color: #ffffff;
          border-color: #e9ecef;
        }
        .invoice-revision-page .card-title {
          margin-bottom: 0;
        }
        .invoice-revision-page .data-list {
          margin: 0;
          padding: 1.5rem;
        }
        .invoice-revision-page .data-row {
          display: flex;
          flex-wrap: wrap;
          padding: 0.5rem 0;
          border-bottom: 1px solid #e9ecef;
        }
        .invoice-revision-page .data-row:last-child {
          border-bottom: none;
        }
        .invoice-revision-page .data-row dt {
          flex: 0 0 180px;
          font-weight: 600;
          color: #6c757d;
        }
        .invoice-revision-page .data-row dd {
          margin: 0;
          flex: 1 1 auto;
          font-weight: 500;
        }
        .invoice-revision-page .form-help {
          margin-top: 0.5rem;
          font-size: 0.85rem;
          color: #6c757d;
        }
        .invoice-revision-page .form-actions {
          display: flex;
          flex-wrap: wrap;
          gap: 0.75rem;
          justify-content: flex-end;
        }
        .invoice-revision-page .btn-outline-secondary {
          display: inline-flex;
          align-items: center;
        }
        .invoice-revision-page .btn-primary {
          display: inline-flex;
          align-items: center;
          background-color: #0d6efd;
        }
        .invoice-revision-page .btn-primary:hover {
          background-color: #0b5ed7;
        }
        .invoice-revision-page .btn-outline-secondary:hover {
          color: #0d6efd;
          border-color: #0d6efd;
          background-color: #ffffff;
        }
        .invoice-revision-page .breadcrumb a {
          color: #0d6efd;
          text-decoration: none;
        }
        .invoice-revision-page .breadcrumb a:hover {
          text-decoration: underline;
        }
        @media (max-width: 575.98px) {
          .invoice-revision-page .data-row dt {
            flex-basis: 100%;
            margin-bottom: 0.25rem;
          }
        }
        @media (max-width: 991.98px) {
          .invoice-revision-page #sidebar {
            position: fixed;
            z-index: 1000;
            height: 100%;
          }
          .invoice-revision-page #content {
            margin-left: var(--sidebar-width);
          }
        }
      `}</style>
    </div>
  );
}
