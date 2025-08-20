-- PostgreSQL schema for HANYA.KNOW
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS categories (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS documents (
    id SERIAL PRIMARY KEY,
    source TEXT NOT NULL,
    page INT,
    content TEXT NOT NULL,
    content_tsv tsvector GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED,
    embedding vector(768) NOT NULL,
    category_id INT REFERENCES categories(id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS idx_documents_embedding
    ON documents USING ivfflat (embedding vector_l2_ops);

CREATE INDEX IF NOT EXISTS idx_documents_content_tsv
    ON documents USING gin (content_tsv);

CREATE INDEX IF NOT EXISTS idx_documents_category
    ON documents(category_id);

CREATE TABLE IF NOT EXISTS chats (
    id SERIAL PRIMARY KEY,
    question TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS cv_recommendations (
    id SERIAL PRIMARY KEY,
    position TEXT NOT NULL,
    details TEXT NOT NULL,
    summary TEXT NOT NULL,
    summary_json TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS roles (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    all_categories BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS role_categories (
    role_id INT REFERENCES roles(id) ON DELETE CASCADE,
    category_id INT REFERENCES categories(id) ON DELETE CASCADE,
    PRIMARY KEY(role_id, category_id)
);
