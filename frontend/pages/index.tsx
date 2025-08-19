import Link from 'next/link';

export default function Home() {
  return (
    <div className="container">
      <h1>HANYA.KNOW</h1>
      <p className="tagline">Upload your documents and ask questions about them.</p>
      <div className="actions">
        <Link href="/ingest"><button>Upload Document</button></Link>
        <Link href="/chat"><button>Ask the Docs</button></Link>
      </div>
      <style jsx>{`
        .container {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 100vh;
          gap: 1rem;
        }
        .tagline {
          text-align: center;
          color: #555;
        }
        .actions {
          display: flex;
          flex-direction: column;
          gap: 0.5rem;
        }
        button {
          padding: 0.5rem 1rem;
        }
      `}</style>
    </div>
  );
}
