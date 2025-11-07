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
- `GET /api/templates` – list project template metadata for the Pre-Sales module
- `GET /api/templates/{id}` – fetch a project template definition
- `POST /api/templates` – create a project template
- `PUT /api/templates/{id}` – update a project template
- `DELETE /api/templates/{id}` – delete a project template
- `GET /api/roles` – list roles and their category access
- `POST /api/roles` – create a role
- `PUT /api/roles/{id}` – update a role's permissions
- `DELETE /api/roles/{id}` – delete a role
- Roles include UI access permissions
- `POST /api/login` – authenticate a user
- `POST /api/login/sso` – authenticate a user through Accelist SSO using the email returned by TAM Passport
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
- `POST /api/chat/source-code` – answer questions about the tracked source code repository with inline citations to files and line ranges
- `GET /api/source-code/status` – report the latest source code sync job, duration, and processed counts
- `POST /api/source-code/sync` – crawl the configured source folder, regenerate embeddings, and update the `code_embeddings` table
- `GET /api/github/status` – report whether the signed-in user has connected a GitHub account
- `GET /api/github/login` – return the GitHub authorization URL for the signed-in user
- `POST /api/github/exchange` – complete the GitHub OAuth handshake with the provided code and state
- `POST /api/github/logout` – disconnect the stored GitHub token for the signed-in user
- `GET /api/github/repos` – list the repositories accessible to the connected GitHub account
- `GET /api/chat/history` – list chat conversations
- `GET /api/chat/history/{id}` – retrieve a conversation's messages
- `GET /api/stats` – usage metrics for the dashboard
- `GET /api/ticketcategories` – list ticket categories
- `GET /api/pics` – list PICs with categories, availability, and ticket counts
- `GET /api/pics/{id}/tickets` – list tickets handled by a PIC
- `GET /api/tickets` – list submitted tickets
- `POST /api/tickets` – create a ticket and automatically assign category and PIC via AI; tickets include a `reason` explaining the category choice or why assignment failed
- `POST /api/assessment/analyze` – create a draft project assessment for the selected template and uploaded scope document
- `POST /api/assessment/save` – persist assessment edits
- `POST /api/assessment/{id}/status` – update the status of a saved assessment (for example mark a draft as completed)
- `GET /api/assessment/{id}/export` – download a saved assessment as Excel
- `POST /api/tickets/{id}/retry-summary` – regenerate JSON assignment summary for a ticket
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
- Submit and track support tickets with automatic AI-driven categorization and assignment to available PICs.
- Dedicated **Pre-Sales** workspace covering project template management, the live assessment grid with Excel export, and the Presales AI history table for reopening saved assessments, exporting bundles, or marking drafts as completed without opening the editor (see `docs/pre-sales-ai-project-assessment-engine.md`). The workspace now routes assessments through a **Timeline Estimator** service before timeline and cost generation: `Presales Workspace → Timeline Estimator → Timeline Generation → Estimated Cost Generation`. The estimator derives a structured timeline summary from assessment data using historical reference projects, so the total duration may differ from the sum of per-phase durations when phases run in parallel or overlap.

## Configuration
Default embedding uses a local Ollama instance with `nomic-embed-text`.

- `ConnectionStrings:Postgres`
- `Embedding: { BaseUrl, Model, Provider, Dimensions }`
- `Llm: { Provider (openai|gemini), ApiKey, Model }`
- `Chat: { CooldownSeconds }` – minimum seconds a client must wait between chat requests
- `SourceCode: { DefaultTopK, SimilarityThreshold, PromptTemplate, SourceDirectory, IncludeExtensions, ExcludeDirectories, ChunkSize, ChunkOverlap }` – tuning and ingestion options for the Source Code Q&A feature
- `GitHub: { ClientId, ClientSecret, RedirectUri, Scopes }` – OAuth app credentials used to sign in with GitHub and import repositories prior to a sync. `Scopes` defaults to `repo read:user` when omitted.
- `AccelistSso: { Host, AppId, RedirectUri, Scope }` – TAM Passport configuration used to render the login widget and verify tokens
- `ApiKey` – shared secret required in `X-API-KEY` header for all API calls
- `NEXT_PUBLIC_API_BASE_URL` and `NEXT_PUBLIC_API_KEY` for the frontend (configure in `.env.local`; see `frontend/.env.local.example`)
- `NEXT_PUBLIC_ACCELIST_SSO_HOST`, `NEXT_PUBLIC_ACCELIST_SSO_APP_ID`, `NEXT_PUBLIC_ACCELIST_SSO_REDIRECT_URI`, and optional `NEXT_PUBLIC_ACCELIST_SSO_SCOPE` to enable the "Login with Accelist SSO" button on the frontend

To verify your embedding service responds correctly, send a sample request (adjust the URL and model for your setup):

```bash
curl -X POST http://localhost:11434/embed -H 'Content-Type: application/json' \
     -d '{"model":"nomic-embed-text","input":"hello"}'
```
The response must contain a non-empty array of numbers. If the payload differs, the server will surface the raw snippet in the error message.

- `Dimensions` defaults to 768 and must match the `vector(<dim>)` in `schema.sql`.

### Source Code Q&A Ingestion

Visit the **Source Code Q&A** page in the admin UI (or call `POST /api/source-code/sync`) to index the repository under `backend/source-code/`. The backend walks the folder, chunks files by line ranges, generates embeddings, and upserts records into the `code_embeddings` table. Use the page controls or `GET /api/source-code/status` to see the last run, duration, and number of processed files/chunks. The job automatically skips common build directories such as `node_modules`, `.git`, `dist`, `bin`, and `obj`.

If you enable GitHub login for repository imports, configure your GitHub OAuth application's redirect URI to the Source Code page (for example `https://your-frontend.example.com/source-code`). After GitHub redirects back, the page exchanges the code for an access token and offers a repository/branch picker before triggering the sync job.
