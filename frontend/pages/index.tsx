import Link from 'next/link';

export default function Home() {
  return (
    <div className="card home-card">
      <h1>HANYA.KNOW</h1>
      <p className="tagline">Upload your documents and ask questions about them.</p>
      <div className="actions">
        <Link href="/documents" className="btn">Manage Documents</Link>
        <Link href="/chat" className="btn">Ask the Docs</Link>
        <Link href="/categories" className="btn">Manage Categories</Link>
      </div>
      <style jsx>{`
        .home-card {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 1rem;
          text-align: center;
          max-width: 400px;
        }
        .tagline {
          color: #666;
        }
        .actions {
          width: 100%;
          display: flex;
          flex-direction: column;
          gap: 0.75rem;
        }
        .actions :global(.btn) {
          width: 100%;
        }
      `}</style>
    </div>
  );
}
