import Link from 'next/link';

export default function NotFound() {
  return (
    <div className="page-container">
      <div className="card" style={{ textAlign: 'center', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        <h1>Page Not Found</h1>
        <p>Sorry, we couldn't find the page you're looking for.</p>
        <Link href="/" className="btn btn-primary">Go Home</Link>
      </div>
    </div>
  );
}
