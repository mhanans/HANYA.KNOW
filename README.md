# HANYA.KNOW

MVP knowledge base with retrieval augmented generation.

## Backend

- ASP.NET Core Web API (requires .NET 7)
- PostgreSQL with pgvector for embeddings (see `schema.sql`)
- Endpoints:
  - `POST /api/ingest`
  - `POST /api/vector/search`
  - `POST /api/chat/query`

## Frontend
- Next.js client with pages for ingesting PDFs and chatting.

## Configuration
- `ConnectionStrings:Postgres`
- `Embedding: { BaseUrl, Model }`
- `Llm: { Provider (openai|gemini), ApiKey, Model }`
- `NEXT_PUBLIC_API_BASE_URL`