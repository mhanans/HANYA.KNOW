# HANYA.KNOW

MVP knowledge base with retrieval augmented generation.

## Backend
- ASP.NET Core Web API (requires .NET 9 SDK v9.0.304)
- PostgreSQL with pgvector for embeddings (see `schema.sql` and `sample-data.sql` for seed data)
- API endpoints:
  - `POST /api/ingest` – upload text or one/many PDF files; PDF pages are stored individually so citations include page numbers
- `GET /api/documents` – list ingested documents
- `PUT /api/documents` – update a document's category
- `DELETE /api/documents` – remove a document by source
- `GET /api/documents/summary` – return a short summary for a document
- `GET /api/categories` – list categories
  - `POST /api/categories` – create a category
  - `PUT /api/categories/{id}` – rename a category
  - `DELETE /api/categories/{id}` – delete a category (fails if in use)
- `GET /api/roles` – list roles and their category access
- `POST /api/roles` – create a role
- `PUT /api/roles/{id}` – update a role's permissions
- `DELETE /api/roles/{id}` – delete a role
- Roles include UI access permissions
- `POST /api/login` – authenticate a user
- `POST /api/logout` – end the current session
- `GET /api/me` – fetch the currently authenticated user
- `GET /api/users` – list users and their roles
- `POST /api/users` – create a user
- `PUT /api/users/{id}` – update a user and role assignments
- `DELETE /api/users/{id}` – remove a user
- `GET /api/settings` – fetch general application settings
- `PUT /api/settings` – update application settings such as name and logo
- `GET /api/ui` – list available UI pages for role mapping
  - `POST /api/vector/search` – vector similarity search
- `POST /api/chat/query` – hybrid vector + full-text retrieval with scored citations; returns a friendly message prompting knowledge upload when no relevant context is found
- `GET /api/chat/history` – list chat conversations
- `GET /api/chat/history/{id}` – retrieve a conversation's messages
- `GET /api/stats` – usage metrics for the dashboard
  - `GET /api/recommendations` – list CV recommendations
- `POST /api/recommendations` – generate a recommendation
- `POST /api/recommendations/{id}/retry` – regenerate an existing recommendation
- `POST /api/recommendations/{id}/retry-summary` – regenerate JSON candidate summaries
- Full-text search uses the language-agnostic `simple` configuration so non-English documents are indexed

All requests to the API must include an `X-API-KEY` header matching the `ApiKey` value in the backend configuration. Users must also authenticate via `POST /api/login`; authenticated sessions are stored in a cookie and required for all other endpoints, which respond with `401 Unauthorized` when accessed anonymously. Interactive documentation is available at `/swagger`, where you can supply the key via the **Authorize** button and try each endpoint directly from the browser.

## Frontend
- Next.js client with pages for managing documents, roles, chatting, and generating CV-based recommendations.
- User login/logout with role-based access control.
- Role-to-UI mapping controls which pages each role can access.
- General settings page to update application name and logo.
- Dashboard shows counts of chats, documents, categories, and users with quick links to common tasks.
- Chat answers include numbered citations with relevance scores.
- Documents can be tagged with categories for targeted queries; manage categories, upload new PDFs, analyze documents, and filter questions by category.

## Configuration
Default embedding uses a local Ollama instance with `nomic-embed-text`.

- `ConnectionStrings:Postgres`
- `Embedding: { BaseUrl, Model, Provider, Dimensions }`
- `Llm: { Provider (openai|gemini), ApiKey, Model }`
- `Chat: { CooldownSeconds }` – minimum seconds a client must wait between chat requests
- `ApiKey` – shared secret required in `X-API-KEY` header for all API calls
- `NEXT_PUBLIC_API_BASE_URL` and `NEXT_PUBLIC_API_KEY` for the frontend (configure in `.env.local`; see `frontend/.env.local.example`)

To verify your embedding service responds correctly, send a sample request (adjust the URL and model for your setup):

```bash
curl -X POST http://localhost:11434/embed -H 'Content-Type: application/json' \
     -d '{"model":"nomic-embed-text","input":"hello"}'
```
The response must contain a non-empty array of numbers. If the payload differs, the server will surface the raw snippet in the error message.

`Dimensions` defaults to 768 and must match the `vector(<dim>)` in `schema.sql`.
