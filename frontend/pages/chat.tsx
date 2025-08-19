import Link from 'next/link';
import { useState } from 'react';

export default function Chat() {
  const [query, setQuery] = useState('');
  const [topK, setTopK] = useState(5);
  const [answer, setAnswer] = useState('');
  const [sources, setSources] = useState<string[]>([]);

  const submit = async () => {
    const res = await fetch(`${process.env.NEXT_PUBLIC_API_BASE_URL}/api/chat/query`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query, topK })
    });
    const data = await res.json();
    setAnswer(data.answer);
    setSources(data.sources);
  };

  return (
    <div className="container">
      <h1>Chat with Documents</h1>
      <input
        placeholder="Ask a question"
        value={query}
        onChange={e => setQuery(e.target.value)}
      />
      <input
        type="number"
        min={1}
        value={topK}
        onChange={e => setTopK(Number(e.target.value))}
      />
      <button onClick={submit}>Ask</button>
      <p>{answer}</p>
      <ul>
        {sources.map((s, i) => (
          <li key={i}>{s}</li>
        ))}
      </ul>
      <Link href="/"><button>Back</button></Link>
      <style jsx>{`
        .container {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 0.5rem;
          padding: 2rem;
        }
        input {
          width: 100%;
          max-width: 400px;
          padding: 0.5rem;
        }
        button {
          padding: 0.5rem 1rem;
        }
        ul {
          list-style: inside;
          max-width: 400px;
        }
      `}</style>
    </div>
  );
}
