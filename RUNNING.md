# Running and Configuration Guide

This guide walks through configuring and running the HANYA.KNOW MVP.

## Prerequisites
- [.NET 9 SDK (v9.0.304)](https://dotnet.microsoft.com/)
- [Node.js](https://nodejs.org/) and npm
- PostgreSQL with the [pgvector](https://github.com/pgvector/pgvector) extension
- [Ollama](https://ollama.ai/) running locally (`ollama serve`) with the `nomic-embed-text` model (`ollama pull nomic-embed-text`)

## Database Setup
1. Start PostgreSQL and create a database for the app.
2. Enable pgvector:
   ```sql
   CREATE EXTENSION IF NOT EXISTS vector;
   ```
3. Apply the provided schema and optional sample data:
    ```bash
    psql -d <db> -f schema.sql
    psql -d <db> -f sample-data.sql # optional seed data
    ```
   `schema.sql` defines `embedding vector(768)` and tables for categories, users, UI pages, role mappings and settings.

## Backend
1. Navigate to the backend project:
   ```bash
   cd backend
   ```
2. Configure settings in `appsettings.json` or via environment variables:
   - `ConnectionStrings:Postgres` – PostgreSQL connection string
   - `Embedding:BaseUrl` – defaults to `http://localhost:11434`
   - `Embedding:Model` – defaults to `nomic-embed-text`
   - `Embedding:Provider` – defaults to `ollama`
   - `Embedding:Dimensions` – defaults to `768`; must match `schema.sql`
   - `Llm:Provider` – `openai` or `gemini`
   - `Llm:ApiKey` – API key for the chosen provider
   - `Llm:Model` – model name such as `gpt-3.5-turbo` or `gemini-pro`
   - `Chat:CooldownSeconds` – minimum seconds between chat requests per client
   - `ApiKey` – shared secret that clients must send in an `X-API-KEY` header

3. Restore and run the API:
   ```bash
   dotnet restore
   dotnet run
   ```

   Include the configured API key in every request using an `X-API-KEY` header. The running server exposes Swagger UI at `/swagger`, where you can authorize with the key and invoke endpoints for testing.

### Source Code Embeddings

1. Place the repository you want to index under `backend/source-code/` (create the folder if it does not exist).
2. Trigger a sync from the admin UI (**Source Code Q&A** page) or by calling `POST /api/source-code/sync`.
3. Monitor progress and review the latest run via `GET /api/source-code/status`. The service reports whether a job is running, the timestamps of the last execution, duration in seconds, and how many files/chunks were processed.

The backend skips common build directories (`node_modules`, `.git`, `dist`, `bin`, `obj`, etc.), chunks files by line ranges, and updates the `code_embeddings` table in a single transaction. Adjust the behaviour via the `SourceCode` section in configuration (extensions, ignored folders, chunk size/overlap).

## Frontend
1. Install dependencies:
   ```bash
   cd ../frontend
   npm install
   ```
2. Copy `.env.local.example` to `.env.local` and update values:
   ```bash
   NEXT_PUBLIC_API_BASE_URL=http://localhost:5000
   NEXT_PUBLIC_API_KEY=dummy-api-key
   ```
3. Start the dev server:
   ```bash
   npm run dev
   ```

Visit `http://localhost:3000` for the UI. Use `/documents` to view existing PDFs, `/upload` to add new ones, `/document-analytics` for summaries, and `/chat` to query stored knowledge with citations and relevance warnings.
Log in at `/login` (sample credentials `admin/password` from `sample-data.sql`). All pages require authentication and will redirect to the login screen when accessed anonymously.
Manage categories at `/categories`, roles at `/roles`, and filter chat questions by one or more categories.
Manage role-to-UI access at `/role-ui`, users at `/users`, and application settings at `/settings`.
The dashboard at `/` shows basic stats, `/chat-history` lists past conversations, and `/cv` performs job vacancy analysis from uploaded CVs.
