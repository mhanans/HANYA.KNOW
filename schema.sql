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

CREATE TABLE IF NOT EXISTS document_summaries (
    source TEXT PRIMARY KEY,
    summary TEXT NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

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

CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS user_roles (
    user_id INT REFERENCES users(id) ON DELETE CASCADE,
    role_id INT REFERENCES roles(id) ON DELETE CASCADE,
    PRIMARY KEY(user_id, role_id)
);

CREATE TABLE IF NOT EXISTS ui_pages (
    id SERIAL PRIMARY KEY,
    key TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS role_ui (
    role_id INT REFERENCES roles(id) ON DELETE CASCADE,
    ui_id INT REFERENCES ui_pages(id) ON DELETE CASCADE,
    PRIMARY KEY(role_id, ui_id)
);

CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ticket_categories (
    id SERIAL PRIMARY KEY,
    ticket_type TEXT NOT NULL UNIQUE,
    description TEXT NOT NULL,
    sample_json TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS pics (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL,
    availability BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS pic_ticket_categories (
    pic_id INT REFERENCES pics(id) ON DELETE CASCADE,
    ticket_category_id INT REFERENCES ticket_categories(id) ON DELETE CASCADE,
    PRIMARY KEY(pic_id, ticket_category_id)
);

CREATE TABLE IF NOT EXISTS tickets (
    id SERIAL PRIMARY KEY,
    ticket_number TEXT NOT NULL UNIQUE,
    complaint TEXT NOT NULL,
    detail TEXT NOT NULL,
    category_id INT REFERENCES ticket_categories(id),
    pic_id INT REFERENCES pics(id),
    reason TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ticket_ai_assignments (
    id SERIAL PRIMARY KEY,
    ticket_id INT REFERENCES tickets(id) ON DELETE CASCADE,
    response TEXT NOT NULL,
    response_json TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ DEFAULT NOW()
);
