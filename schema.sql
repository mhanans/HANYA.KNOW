-- PostgreSQL schema for HANYA.KNOW
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

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

CREATE TABLE IF NOT EXISTS user_github_tokens (
    user_id INT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    access_token TEXT NOT NULL,
    token_type TEXT NOT NULL,
    scope TEXT NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW()
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

CREATE TABLE IF NOT EXISTS project_templates (
    id SERIAL PRIMARY KEY,
    template_name TEXT NOT NULL,
    template_data JSONB NOT NULL,
    created_by_user_id INT REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_modified_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_project_templates_name
    ON project_templates (template_name);

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

CREATE TABLE IF NOT EXISTS project_assessments (
    id SERIAL PRIMARY KEY,
    template_id INT REFERENCES project_templates(id) ON DELETE CASCADE,
    project_name TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Draft',
    assessment_data JSONB NOT NULL,
    created_by_user_id INT REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_modified_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_project_assessments_template
    ON project_assessments(template_id);

CREATE INDEX IF NOT EXISTS idx_project_assessments_status
    ON project_assessments(status);

CREATE TABLE IF NOT EXISTS assessment_step_definitions (
    status TEXT PRIMARY KEY,
    step INT NOT NULL CHECK (step >= 1),
    step_name TEXT NOT NULL,
    description TEXT NOT NULL,
    job_status TEXT,
    job_outputs TEXT[] NOT NULL DEFAULT '{}',
    UNIQUE (step)
);

CREATE INDEX IF NOT EXISTS idx_assessment_step_definitions_step
    ON assessment_step_definitions(step);

CREATE TABLE IF NOT EXISTS assessment_jobs (
    id SERIAL PRIMARY KEY,
    project_name TEXT NOT NULL DEFAULT '',
    template_id INT NOT NULL REFERENCES project_templates(id) ON DELETE CASCADE,
    analysis_mode TEXT NOT NULL DEFAULT 'Interpretive',
    output_language TEXT NOT NULL DEFAULT 'Indonesian',
    status TEXT NOT NULL,
    step INT NOT NULL DEFAULT 1,
    scope_document_path TEXT NOT NULL,
    scope_document_mime_type TEXT NOT NULL DEFAULT 'application/octet-stream',
    scope_document_has_manhour BOOLEAN NOT NULL DEFAULT FALSE,
    detected_scope_manhour BOOLEAN,
    manhour_detection_notes TEXT,
    original_template_json TEXT NOT NULL,
    reference_assessments_json TEXT,
    reference_documents_json TEXT,
    raw_generation_response TEXT,
    generated_items_json TEXT,
    raw_estimation_response TEXT,
    raw_manual_assessment_json TEXT,
    final_analysis_json TEXT,
    last_error TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_modified_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_assessment_jobs_status
    ON assessment_jobs(status);

CREATE INDEX IF NOT EXISTS idx_assessment_jobs_template
    ON assessment_jobs(template_id);

CREATE TABLE IF NOT EXISTS knowledge_base_documents (
    id SERIAL PRIMARY KEY,
    original_file_name TEXT NOT NULL,
    storage_path TEXT NOT NULL,
    project_name TEXT,
    document_type TEXT,
    client_type TEXT,
    project_completion_date DATE,
    processing_status TEXT NOT NULL DEFAULT 'Pending',
    error_message TEXT,
    chunk_count INT DEFAULT 0,
    uploaded_by_user_id INT REFERENCES users(id) ON DELETE SET NULL,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_knowledge_base_documents_status
    ON knowledge_base_documents(processing_status);

CREATE TABLE IF NOT EXISTS knowledge_base_chunks (
    id SERIAL PRIMARY KEY,
    document_id INT REFERENCES knowledge_base_documents(id) ON DELETE CASCADE,
    chunk_index INT NOT NULL,
    page_number INT,
    content TEXT NOT NULL,
    embedding vector(768) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_knowledge_base_chunks_document
    ON knowledge_base_chunks(document_id);

CREATE INDEX IF NOT EXISTS idx_knowledge_base_chunks_embedding
    ON knowledge_base_chunks USING ivfflat (embedding vector_l2_ops);

CREATE TABLE IF NOT EXISTS code_embeddings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    file_path TEXT NOT NULL,
    symbol_name TEXT,
    content TEXT NOT NULL,
    start_line INTEGER,
    end_line INTEGER,
    checksum TEXT NOT NULL,
    embedding vector(768) NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_code_embeddings_embedding
    ON code_embeddings USING hnsw (embedding vector_l2_ops);

CREATE INDEX IF NOT EXISTS idx_code_embeddings_file_path
    ON code_embeddings(file_path);

CREATE TABLE IF NOT EXISTS code_sync_jobs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    status TEXT NOT NULL,
    details TEXT,
    file_count INTEGER,
    chunk_count INTEGER,
    duration_seconds DOUBLE PRECISION
);

CREATE INDEX IF NOT EXISTS idx_code_sync_jobs_started_at
    ON code_sync_jobs(started_at DESC);

CREATE TABLE IF NOT EXISTS ticket_ai_assignments (
    id SERIAL PRIMARY KEY,
    ticket_id INT REFERENCES tickets(id) ON DELETE CASCADE,
    response TEXT NOT NULL,
    response_json TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS presales_roles (
    role_name TEXT NOT NULL,
    expected_level TEXT NOT NULL DEFAULT '',
    cost_per_day NUMERIC(18,2) NOT NULL DEFAULT 0,
    PRIMARY KEY (role_name, expected_level)
);

CREATE TABLE IF NOT EXISTS presales_activities (
    activity_name TEXT PRIMARY KEY,
    display_order INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS presales_item_activities (
    section_name TEXT NOT NULL DEFAULT '',
    item_name TEXT NOT NULL DEFAULT '',
    activity_name TEXT NOT NULL REFERENCES presales_activities(activity_name) ON DELETE CASCADE,
    display_order INT NOT NULL DEFAULT 0,
    PRIMARY KEY (section_name, item_name)
);

ALTER TABLE presales_item_activities
    ADD COLUMN IF NOT EXISTS section_name TEXT NOT NULL DEFAULT '';

ALTER TABLE presales_item_activities
    ADD COLUMN IF NOT EXISTS display_order INT NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE constraint_name = 'presales_item_activities_pkey'
          AND table_name = 'presales_item_activities'
          AND table_schema = 'public'
    ) THEN
        BEGIN
            EXECUTE 'ALTER TABLE presales_item_activities DROP CONSTRAINT presales_item_activities_pkey';
        EXCEPTION WHEN invalid_table_definition THEN
            NULL;
        END;
    END IF;
END $$;

ALTER TABLE presales_item_activities
    ADD CONSTRAINT presales_item_activities_pkey PRIMARY KEY (section_name, item_name);

CREATE TABLE IF NOT EXISTS presales_estimation_column_roles (
    estimation_column TEXT NOT NULL,
    role_name TEXT NOT NULL,
    PRIMARY KEY(estimation_column, role_name)
);

CREATE TABLE IF NOT EXISTS presales_team_types (
    id SERIAL PRIMARY KEY,
    team_type_name TEXT NOT NULL,
    min_man_days INT NOT NULL DEFAULT 0,
    max_man_days INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS presales_team_type_roles (
    id SERIAL PRIMARY KEY,
    team_type_id INT NOT NULL REFERENCES presales_team_types(id) ON DELETE CASCADE,
    role_name TEXT NOT NULL,
    headcount NUMERIC(4,2) NOT NULL DEFAULT 1.0
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE table_name = 'presales_team_type_roles'
          AND constraint_name = 'presales_team_type_roles_unique_role'
    ) THEN
        ALTER TABLE presales_team_type_roles
            ADD CONSTRAINT presales_team_type_roles_unique_role UNIQUE (team_type_id, role_name);
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.constraint_column_usage
        WHERE table_name = 'presales_estimation_column_roles'
          AND constraint_name = 'presales_estimation_column_roles_role_name_expected_level_fkey'
    ) THEN
        BEGIN
            EXECUTE 'ALTER TABLE presales_estimation_column_roles DROP CONSTRAINT IF EXISTS presales_estimation_column_roles_role_name_expected_level_fkey';
        EXCEPTION WHEN undefined_object THEN
            NULL;
        END;
    END IF;
END $$;

ALTER TABLE presales_estimation_column_roles DROP CONSTRAINT IF EXISTS presales_estimation_column_roles_pkey;

WITH duplicates AS (
    SELECT estimation_column, role_name, MIN(ctid) AS keep_ctid
    FROM presales_estimation_column_roles
    GROUP BY estimation_column, role_name
    HAVING COUNT(*) > 1
)
DELETE FROM presales_estimation_column_roles target
USING duplicates d
WHERE target.estimation_column = d.estimation_column
  AND target.role_name = d.role_name
  AND target.ctid <> d.keep_ctid;

ALTER TABLE presales_estimation_column_roles
    DROP COLUMN IF EXISTS expected_level;

ALTER TABLE presales_estimation_column_roles
    ADD CONSTRAINT presales_estimation_column_roles_pkey PRIMARY KEY (estimation_column, role_name);

CREATE TABLE IF NOT EXISTS assessment_timelines (
    assessment_id INT REFERENCES project_assessments(id) ON DELETE CASCADE,
    version INT NOT NULL DEFAULT 0,
    project_name TEXT NOT NULL,
    template_name TEXT NOT NULL,
    generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    timeline_data JSONB NOT NULL,
    PRIMARY KEY (assessment_id, version)
);

CREATE TABLE IF NOT EXISTS assessment_timeline_attempts (
    id SERIAL PRIMARY KEY,
    assessment_id INT NOT NULL REFERENCES project_assessments(id) ON DELETE CASCADE,
    project_name TEXT NOT NULL,
    template_name TEXT NOT NULL,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    raw_response TEXT,
    error TEXT,
    success BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_assessment_timeline_attempts_assessment_id
    ON assessment_timeline_attempts(assessment_id);

CREATE TABLE IF NOT EXISTS timeline_estimation_references (
    id SERIAL PRIMARY KEY,
    project_scale TEXT NOT NULL CHECK (project_scale <> ''),
    phase_durations JSONB NOT NULL CHECK (jsonb_typeof(phase_durations) = 'object'),
    total_duration_days INT NOT NULL CHECK (total_duration_days > 0),
    resource_allocation JSONB NOT NULL CHECK (jsonb_typeof(resource_allocation) = 'object')
);


CREATE TABLE IF NOT EXISTS assessment_timeline_estimations (
    assessment_id INT PRIMARY KEY REFERENCES project_assessments(id) ON DELETE CASCADE,
    project_name TEXT NOT NULL,
    template_name TEXT NOT NULL,
    generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    estimation_data JSONB NOT NULL,
    raw_input_data JSONB
);

CREATE TABLE IF NOT EXISTS cost_estimations (
    assessment_id INT PRIMARY KEY REFERENCES project_assessments(id) ON DELETE CASCADE,
    project_name TEXT NOT NULL,
    template_name TEXT NOT NULL,
    generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    result_json JSONB NOT NULL
);

CREATE TABLE IF NOT EXISTS assessment_prototypes (
    assessment_id INT PRIMARY KEY REFERENCES project_assessments(id) ON DELETE CASCADE,
    project_name TEXT NOT NULL,
    generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    storage_path TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Completed'
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'assessment_prototypes'
          AND column_name = 'status'
    ) THEN
        ALTER TABLE assessment_prototypes ADD COLUMN status TEXT NOT NULL DEFAULT 'Completed';
    END IF;
END $$;

