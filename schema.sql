-- PostgreSQL schema for HANYA.KNOW
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    id SERIAL PRIMARY KEY,
    source TEXT NOT NULL,
    page INT,
    content TEXT NOT NULL,
    content_tsv tsvector GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED,
    embedding vector(768) NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_documents_embedding
    ON documents USING ivfflat (embedding vector_l2_ops);

CREATE INDEX IF NOT EXISTS idx_documents_content_tsv
    ON documents USING gin (content_tsv);
