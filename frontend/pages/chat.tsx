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
    <div>
      <h1>Chat</h1>
      <input value={query} onChange={e => setQuery(e.target.value)} />
      <input type="number" value={topK} onChange={e => setTopK(Number(e.target.value))} />
      <button onClick={submit}>Ask</button>
      <p>{answer}</p>
      <ul>{sources.map((s, i) => <li key={i}>{s}</li>)}</ul>
    </div>
  );
}
