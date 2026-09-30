CREATE SCHEMA IF NOT EXISTS aiakos;

CREATE TABLE aiakos.tenant (
    tenant_id  uuid        PRIMARY KEY,
    slug       text        NOT NULL UNIQUE CHECK (slug ~ '^[a-z0-9][a-z0-9-]{0,62}$'),
    name       text        NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO aiakos.tenant (tenant_id, slug, name)
VALUES ('00000000-0000-0000-0000-000000000001', 'default', 'Default tenant');
