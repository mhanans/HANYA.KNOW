ALTER TABLE presales_roles ADD COLUMN IF NOT EXISTS monthly_salary numeric DEFAULT 0;
ALTER TABLE presales_roles ADD COLUMN IF NOT EXISTS rate_per_day numeric DEFAULT 0;
