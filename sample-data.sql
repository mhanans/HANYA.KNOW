-- Sample initial data for HANYA.KNOW

-- Categories
INSERT INTO categories (name) VALUES ('General') ON CONFLICT DO NOTHING;

-- UI pages
INSERT INTO ui_pages (key) VALUES
  ('documents'),
  ('chat'),
  ('categories'),
  ('roles'),
  ('role-ui'),
  ('users'),
  ('settings')
ON CONFLICT DO NOTHING;

-- Roles and users
INSERT INTO roles (name, all_categories) VALUES ('admin', TRUE) ON CONFLICT DO NOTHING;
INSERT INTO users (username, password) VALUES ('admin', 'password') ON CONFLICT DO NOTHING;

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

