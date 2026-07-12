-- Creates all 4 service databases.
-- Runs automatically on first container start via docker-entrypoint-initdb.d

CREATE DATABASE eswmp_assignment;
CREATE DATABASE eswmp_rules;
CREATE DATABASE eswmp_work;

-- Default POSTGRES_DB (set via docker-compose environment) is eswmp_core,
-- created automatically by the postgres image entrypoint.
