# HANYA.KNOW

MVP knowledge base with retrieval augmented generation.

## Backend
- ASP.NET Core Web API (requires .NET 9 SDK v9.0.304)
- PostgreSQL with pgvector for embeddings (see `schema.sql`)
- API endpoints:
  - `POST /api/ingest` – upload text or one/many PDF files; PDF pages are stored individually so citations include page numbers
  - `GET /api/documents` – list ingested documents
  - `PUT /api/documents` – update a document's category
  - `DELETE /api/documents` – remove a document by source
  - `GET /api/categories` – list categories
  - `POST /api/categories` – create a category
  - `PUT /api/categories/{id}` – rename a category
  - `DELETE /api/categories/{id}` – delete a category (fails if in use)
  - `POST /api/vector/search` – vector similarity search
  - `POST /api/chat/query` – hybrid vector + full-text retrieval with scored citations
  - `GET /api/stats` – usage metrics for the dashboard
  - `GET /api/recommendations` – list CV recommendations
  - `POST /api/recommendations` – generate a recommendation
  - `POST /api/recommendations/{id}/retry` – regenerate an existing recommendation
  - `POST /api/recommendations/{id}/retry-summary` – regenerate JSON candidate summaries
- Full-text search uses the language-agnostic `simple` configuration so non-English documents are indexed

## Frontend
- Next.js client with pages for managing documents, chatting, and generating CV-based recommendations.
- Dashboard shows counts of chats, documents, and categories.
- Chat answers include numbered citations with relevance scores.
- Documents can be tagged with categories for targeted queries; manage categories on the frontend and filter questions by category.

## Configuration
Default embedding uses a local Ollama instance with `nomic-embed-text`.

- `ConnectionStrings:Postgres`
- `Embedding: { BaseUrl, Model, Provider, Dimensions }`
- `Llm: { Provider (openai|gemini), ApiKey, Model }`
- `Chat: { CooldownSeconds }` – minimum seconds a client must wait between chat requests
- `NEXT_PUBLIC_API_BASE_URL` for frontend (defaults to `http://localhost:5000` if unset)

To verify your embedding service responds correctly, send a sample request (adjust the URL and model for your setup):

```bash
curl -X POST http://localhost:11434/embed -H 'Content-Type: application/json' \
     -d '{"model":"nomic-embed-text","input":"hello"}'
```
The response must contain a non-empty array of numbers. If the payload differs, the server will surface the raw snippet in the error message.

`Dimensions` defaults to 768 and must match the `vector(<dim>)` in `schema.sql`.
