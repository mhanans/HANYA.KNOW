import Link from 'next/link';

export default function NotFound() {
  return (
    <div className="container">
      <h1>Page Not Found</h1>
      <p>Sorry, we couldn't find the page you're looking for.</p>
      <Link href="/"><button>Go Home</button></Link>
      <style jsx>{`
        .container {
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          height: 100vh;
          gap: 1rem;
        }
        button {
          padding: 0.5rem 1rem;
        }
      `}</style>
    </div>
  );
}
