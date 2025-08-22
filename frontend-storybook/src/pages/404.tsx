import Link from 'next/link';

export default function NotFound() {
  return (
    <div className="container">
      <div className="card notfound-card">
        <h1>Page Not Found</h1>
        <p>Sorry, we couldn&apos;t find the page you&apos;re looking for.</p>
        <Link href="/" className="btn">Go Home</Link>
      </div>
      <style jsx>{`
        .notfound-card {
          text-align: center;
          display: flex;
          flex-direction: column;
          gap: 1rem;
        }
      `}</style>
    </div>
  );
}
