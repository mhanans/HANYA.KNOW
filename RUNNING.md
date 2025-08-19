# Running and Configuration Guide

This guide walks through configuring and running the HANYA.KNOW MVP.

## Prerequisites
- [.NET 9 SDK (v9.0.304)](https://dotnet.microsoft.com/)
- [Node.js](https://nodejs.org/) and npm
- PostgreSQL with the [pgvector](https://github.com/pgvector/pgvector) extension
- An HTTP embedding service that returns `float[]` vectors

## Database Setup
1. Start PostgreSQL and create a database for the app.
2. Enable pgvector:
   ```sql
   CREATE EXTENSION IF NOT EXISTS vector;
   ```
3. Apply the provided schema:
   ```bash
   psql -d <db> -f schema.sql
   ```

## Backend
1. Navigate to the backend project:
   ```bash
   cd backend
   ```
2. Configure settings in `appsettings.json` or via environment variables:
   - `ConnectionStrings:Postgres` – PostgreSQL connection string
   - `Embedding:BaseUrl` – URL of embedding service
   - `Embedding:Model` – model name to request from the embedding service
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

Visit `http://localhost:3000` for the UI. Use `/ingest` to upload PDFs or text and `/chat` to query stored knowledge.
