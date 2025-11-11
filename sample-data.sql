-- Sample initial data for HANYA.KNOW

-- Categories
INSERT INTO categories (name) VALUES ('General') ON CONFLICT DO NOTHING;

-- UI pages
INSERT INTO ui_pages (key) VALUES
  ('dashboard'),
  ('documents'),
  ('categories'),
  ('upload'),
  ('document-analytics'),
  ('chat'),
  ('chat-history'),
  ('source-code'),
  ('cv'),
  ('data-sources'),
  ('invoice-verification'),
  ('pre-sales-project-timelines'),
  ('pre-sales-project-templates'),
  ('pre-sales-assessment-workspace'),
  ('pre-sales-cost-estimations'),
  ('admin-presales-history'),
  ('users'),
  ('roles'),
  ('role-ui'),
  ('tickets'),
  ('pic-summary'),
  ('timeline-estimation-references'),
  ('settings')
ON CONFLICT DO NOTHING;

-- Roles and users
INSERT INTO roles (name, all_categories) VALUES ('admin', TRUE) ON CONFLICT DO NOTHING;
-- password: "password"
INSERT INTO users (username, password) VALUES ('admin', '$2b$10$45c4DJ7lkTkvEVsONn7FHeBreQ6L3LcGsFLnVTZAaBC3ffe7iErsK') ON CONFLICT DO NOTHING;

-- Assign admin role to admin user
INSERT INTO user_roles (user_id, role_id)
SELECT u.id, r.id FROM users u, roles r
WHERE u.username = 'admin' AND r.name = 'admin'
ON CONFLICT DO NOTHING;

-- Grant all UI access to admin role
INSERT INTO role_ui (role_id, ui_id)
SELECT r.id, u.id FROM roles r CROSS JOIN ui_pages u
WHERE r.name = 'admin'
ON CONFLICT DO NOTHING;

-- Timeline estimation reference samples
INSERT INTO timeline_estimation_references (phase_name, input_man_hours, input_resource_count, output_duration_days) VALUES
  ('Application Development', 120, 2, 25),
  ('Testing & Bug Fixing', 80, 2, 15)
ON CONFLICT DO NOTHING;

-- Ticket categories
INSERT INTO ticket_categories (ticket_type, description, sample_json) VALUES
  ('Technical', 'Technical issues', '{"example": "Cannot log in"}'),
  ('Billing', 'Billing questions', '{"example": "Invoice missing"}')
ON CONFLICT DO NOTHING;

-- PICs
INSERT INTO pics (name, availability) VALUES
  ('Alice', TRUE),
  ('Bob', FALSE)
ON CONFLICT DO NOTHING;

-- PIC category assignments
INSERT INTO pic_ticket_categories (pic_id, ticket_category_id)
SELECT p.id, c.id FROM pics p, ticket_categories c
WHERE p.name = 'Alice' AND c.ticket_type = 'Technical'
ON CONFLICT DO NOTHING;

INSERT INTO pic_ticket_categories (pic_id, ticket_category_id)
SELECT p.id, c.id FROM pics p, ticket_categories c
WHERE p.name = 'Bob' AND c.ticket_type = 'Billing'
ON CONFLICT DO NOTHING;

-- Pre-sales project templates
INSERT INTO project_templates (template_name, template_data, created_by_user_id, created_at, last_modified_at)
SELECT
  'AI Discovery Sprint',
  $$
  {
    "templateName": "AI Discovery Sprint",
    "estimationColumns": [
      "Solution Architect",
      "Business Analyst",
      "Quality Engineer"
    ],
    "sections": [
      {
        "sectionName": "Discovery",
        "type": "Project-Level",
        "items": [
          {
            "itemId": "DISC-001",
            "itemName": "Stakeholder Interviews",
            "itemDetail": "Conduct interviews to capture goals and constraints."
          },
          {
            "itemId": "DISC-002",
            "itemName": "Current State Review",
            "itemDetail": "Audit existing solutions, integrations, and data flows."
          }
        ]
      },
      {
        "sectionName": "Solution Design",
        "type": "Project-Level",
        "items": [
          {
            "itemId": "DES-101",
            "itemName": "Architecture Blueprint",
            "itemDetail": "Draft the target architecture and technology stack."
          },
          {
            "itemId": "DES-102",
            "itemName": "Implementation Roadmap",
            "itemDetail": "Define milestones, risks, and delivery phasing."
          }
        ]
      }
    ]
  }
  $$::jsonb,
  (SELECT id FROM users WHERE username = 'admin' LIMIT 1),
  NOW(),
  NOW()
WHERE NOT EXISTS (
  SELECT 1 FROM project_templates WHERE template_name = 'AI Discovery Sprint'
);

-- Assessment workflow steps
INSERT INTO assessment_step_definitions (status, step, step_name, description, job_status, job_outputs) VALUES
  ('Draft', 1, 'Draft assessment', 'Assessment data has been saved as a draft before automation begins.', NULL, ARRAY[]::text[]),
  (
    'Pending',
    2,
    'Inputs captured',
    'Scope document and template references are stored and ready for AI generation.',
    'Pending',
    ARRAY['scope_document_path', 'scope_document_mime_type', 'original_template_json', 'reference_assessments_json']
  ),
  (
    'GenerationInProgress',
    3,
    'Generating assessment items',
    'The automation is creating tailored assessment items from the provided inputs.',
    'GenerationInProgress',
    ARRAY['raw_generation_response']
  ),
  (
    'GenerationComplete',
    4,
    'Assessment items generated',
    'Item generation finished successfully and serialized for later steps.',
    'GenerationComplete',
    ARRAY['raw_generation_response', 'generated_items_json']
  ),
  (
    'FailedGeneration',
    5,
    'Generation failed',
    'Item generation encountered an error and can be resumed after repair.',
    'FailedGeneration',
    ARRAY['last_error']
  ),
  (
    'EstimationInProgress',
    6,
    'Estimating delivery effort',
    'The automation is estimating delivery effort for the generated items.',
    'EstimationInProgress',
    ARRAY['raw_estimation_response']
  ),
  (
    'EstimationComplete',
    7,
    'Estimation finished',
    'Effort estimation completed and analysis results are being stored.',
    'EstimationComplete',
    ARRAY['raw_estimation_response', 'final_analysis_json']
  ),
  (
    'FailedEstimation',
    8,
    'Estimation failed',
    'Effort estimation encountered an error and can be resumed after repair.',
    'FailedEstimation',
    ARRAY['last_error']
  ),
  (
    'Complete',
    9,
    'Assessment complete',
    'Assessment processing finished successfully and results are ready for review.',
    'Complete',
    ARRAY['generated_items_json', 'final_analysis_json']
  ),
  (
    'Completed',
    10,
    'Assessment archived',
    'Assessment was completed and subsequently archived or confirmed.',
    'Complete',
    ARRAY['generated_items_json', 'final_analysis_json']
  )
ON CONFLICT (status) DO UPDATE
SET
  step = EXCLUDED.step,
  step_name = EXCLUDED.step_name,
  description = EXCLUDED.description,
  job_status = EXCLUDED.job_status,
  job_outputs = EXCLUDED.job_outputs;


INSERT INTO presales_roles (role_name, expected_level, cost_per_day) VALUES
  ('Project Manager', 'Lead', 950),
  ('Business Analyst', 'Senior', 700),
  ('Business Analyst', 'Junior', 620),
  ('Developer', 'Senior', 800),
  ('Developer', 'Junior', 520),
  ('Quality Engineer', 'Specialist', 610)
ON CONFLICT (role_name, expected_level) DO UPDATE
SET cost_per_day = EXCLUDED.cost_per_day;

INSERT INTO presales_activities (activity_name, display_order) VALUES
  ('Project Preparation', 1),
  ('Analysis & Design', 2),
  ('Application Development', 3),
  ('Testing & Bug Fixing', 4),
  ('Deployment & Handover', 5)
ON CONFLICT (activity_name) DO UPDATE
SET display_order = EXCLUDED.display_order;

INSERT INTO presales_item_activities (item_name, activity_name) VALUES
  ('Stakeholder Interviews', 'Project Preparation'),
  ('Current State Review', 'Analysis & Design'),
  ('Architecture Blueprint', 'Analysis & Design'),
  ('Implementation Roadmap', 'Deployment & Handover')
ON CONFLICT (item_name) DO UPDATE
SET activity_name = EXCLUDED.activity_name;

INSERT INTO presales_estimation_column_roles (estimation_column, role_name) VALUES
  ('Solution Architect', 'Project Manager'),
  ('Business Analyst', 'Business Analyst'),
  ('Quality Engineer', 'Quality Engineer'),
  ('Development', 'Developer'),
  ('Development Support', 'Developer')
ON CONFLICT (estimation_column, role_name) DO NOTHING;
