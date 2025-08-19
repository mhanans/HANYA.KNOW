import Link from 'next/link';

export default function Home() {
  return (
    <div className="container">
      <div className="card home-card">
        <h1>HANYA.KNOW</h1>
        <p className="tagline">Upload your documents and ask questions about them.</p>
        <div className="actions">
          <Link href="/ingest"><button>Upload Document</button></Link>
          <Link href="/chat"><button>Ask the Docs</button></Link>
        </div>
      </div>
      <style jsx>{`
        .home-card {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 1rem;
          text-align: center;
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
      `}</style>
    </div>
  );
}
