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
3. Apply the provided schema (includes hybrid vector + language-agnostic full-text indexes):
    ```bash
    psql -d <db> -f schema.sql
    ```
   `schema.sql` defines `embedding vector(768)`; adjust the dimension if your model outputs a different size.

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

3. Restore and run the API:
   ```bash
   dotnet restore
   dotnet run
   ```

## Frontend
1. Install dependencies:
   ```bash
   cd ../frontend
   npm install
   ```
2. Create `.env.local` with:
   ```bash
   NEXT_PUBLIC_API_BASE_URL=http://localhost:5000
   ```
3. Start the dev server:
   ```bash
   npm run dev
   ```

Visit `http://localhost:3000` for the UI. Use `/ingest` to upload PDFs (multiple files supported) or text and `/chat` to query stored knowledge with citations and relevance warnings.
