-- TraceCarrier local schema bootstrap
-- This script runs once when the PostgreSQL data volume is created.

BEGIN;

CREATE TABLE IF NOT EXISTS units (
    id BIGSERIAL PRIMARY KEY,
    unit_id VARCHAR(64) NOT NULL UNIQUE,
    current_process VARCHAR(100) NOT NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'created',
    next_process_available_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS carriers (
    id BIGSERIAL PRIMARY KEY,
    carrier_id VARCHAR(64) NOT NULL UNIQUE,
    status VARCHAR(40) NOT NULL DEFAULT 'active',
    next_process_available_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS carrier_units (
    id BIGSERIAL PRIMARY KEY,
    carrier_id BIGINT NOT NULL REFERENCES carriers(id) ON DELETE CASCADE,
    unit_id BIGINT NOT NULL REFERENCES units(id) ON DELETE CASCADE,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_carrier_units UNIQUE (carrier_id, unit_id),
    CONSTRAINT uq_carrier_units_unit UNIQUE (unit_id)
);

CREATE TABLE IF NOT EXISTS process_history (
    id BIGSERIAL PRIMARY KEY,
    unit_id BIGINT NOT NULL REFERENCES units(id) ON DELETE CASCADE,
    process_name VARCHAR(100) NOT NULL,
    start_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    end_time TIMESTAMPTZ NULL,
    required_time_seconds INTEGER NOT NULL CHECK (required_time_seconds >= 0),
    ready_for_next_process_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed BOOLEAN NOT NULL DEFAULT FALSE,
    notes TEXT NULL,
    CONSTRAINT chk_process_time_window CHECK (
        end_time IS NULL OR end_time >= start_time
    ),
    CONSTRAINT chk_process_ready_for_next CHECK (
        ready_for_next_process_at >= start_time
    )
);

CREATE TABLE IF NOT EXISTS carrier_process_history (
    id BIGSERIAL PRIMARY KEY,
    carrier_id BIGINT NOT NULL REFERENCES carriers(id) ON DELETE CASCADE,
    process_name VARCHAR(100) NOT NULL,
    start_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    end_time TIMESTAMPTZ NULL,
    required_time_seconds INTEGER NOT NULL CHECK (required_time_seconds >= 0),
    ready_for_next_process_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed BOOLEAN NOT NULL DEFAULT FALSE,
    notes TEXT NULL,
    CONSTRAINT chk_carrier_process_time_window CHECK (
        end_time IS NULL OR end_time >= start_time
    ),
    CONSTRAINT chk_carrier_process_ready_for_next CHECK (
        ready_for_next_process_at >= start_time
    )
);

ALTER TABLE IF EXISTS units
    ADD COLUMN IF NOT EXISTS next_process_available_at TIMESTAMPTZ NULL;

ALTER TABLE IF EXISTS carriers
    ADD COLUMN IF NOT EXISTS next_process_available_at TIMESTAMPTZ NULL;

ALTER TABLE IF EXISTS process_history
    ADD COLUMN IF NOT EXISTS ready_for_next_process_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

ALTER TABLE IF EXISTS carrier_process_history
    ADD COLUMN IF NOT EXISTS ready_for_next_process_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_units_set_updated_at ON units;
CREATE TRIGGER trg_units_set_updated_at
BEFORE UPDATE ON units
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

DROP TRIGGER IF EXISTS trg_carriers_set_updated_at ON carriers;
CREATE TRIGGER trg_carriers_set_updated_at
BEFORE UPDATE ON carriers
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

CREATE INDEX IF NOT EXISTS idx_units_status ON units(status);
CREATE INDEX IF NOT EXISTS idx_units_current_process ON units(current_process);
CREATE INDEX IF NOT EXISTS idx_units_next_process_available_at ON units(next_process_available_at);

CREATE INDEX IF NOT EXISTS idx_carriers_status ON carriers(status);
CREATE INDEX IF NOT EXISTS idx_carriers_next_process_available_at ON carriers(next_process_available_at);

CREATE INDEX IF NOT EXISTS idx_carrier_units_carrier_id ON carrier_units(carrier_id);
CREATE INDEX IF NOT EXISTS idx_carrier_units_unit_id ON carrier_units(unit_id);

CREATE INDEX IF NOT EXISTS idx_process_history_unit_id ON process_history(unit_id);
CREATE INDEX IF NOT EXISTS idx_process_history_unit_completed ON process_history(unit_id, completed);
CREATE INDEX IF NOT EXISTS idx_process_history_process_name ON process_history(process_name);
CREATE INDEX IF NOT EXISTS idx_process_history_ready_for_next ON process_history(ready_for_next_process_at);

CREATE INDEX IF NOT EXISTS idx_carrier_process_history_carrier_id ON carrier_process_history(carrier_id);
CREATE INDEX IF NOT EXISTS idx_carrier_process_history_carrier_completed ON carrier_process_history(carrier_id, completed);
CREATE INDEX IF NOT EXISTS idx_carrier_process_history_process_name ON carrier_process_history(process_name);
CREATE INDEX IF NOT EXISTS idx_carrier_process_history_ready_for_next ON carrier_process_history(ready_for_next_process_at);

COMMIT;
