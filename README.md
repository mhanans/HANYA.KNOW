# HANYA.KNOW

MVP knowledge base with retrieval augmented generation.

## Backend
- ASP.NET Core Web API (requires .NET 9 SDK v9.0.304)
- PostgreSQL with pgvector for embeddings (see `schema.sql`)
- Endpoints:
  - `POST /api/ingest` – upload text or one/many PDF files
  - `POST /api/vector/search`
  - `POST /api/chat/query` – hybrid vector + full-text retrieval with scored citations
  - full-text search uses the language-agnostic `simple` configuration so non-English documents are indexed

## Frontend
- Next.js client with pages for ingesting PDFs and chatting.

## Configuration
Default embedding uses a local Ollama instance with `nomic-embed-text`.

- `ConnectionStrings:Postgres`
- `Embedding: { BaseUrl, Model, Provider, Dimensions }`
- `Llm: { Provider (openai|gemini), ApiKey, Model }`
- `NEXT_PUBLIC_API_BASE_URL` for frontend (defaults to `http://localhost:5000` if unset)

To verify your embedding service responds correctly, send a sample request (adjust the URL and model for your setup):

```bash
curl -X POST http://localhost:11434/embed -H 'Content-Type: application/json' \
     -d '{"model":"nomic-embed-text","input":"hello"}'
```
The response must contain a non-empty array of numbers. If the payload differs, the server will surface the raw snippet in the error message.

`Dimensions` defaults to 768 and must match the `vector(<dim>)` in `schema.sql`.
