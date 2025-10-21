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
  ('pre-sales-project-templates'),
  ('pre-sales-assessment-workspace'),
  ('admin-presales-history'),
  ('users'),
  ('roles'),
  ('role-ui'),
  ('tickets'),
  ('pic-summary'),
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

