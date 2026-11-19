-- 0005: digital EIR - Equipment Interchange Receipt (T33)
-- The depot records container condition at gate-in or gate-out with photos and damage marks,
-- issues it, and the driver confirms or disputes it in the app.

CREATE TABLE eirs (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    number               bigint GENERATED ALWAYS AS IDENTITY UNIQUE,  -- printed as EIR-000123
    facility_visit_id    uuid NOT NULL REFERENCES facility_visits (id),
    facility_id          uuid NOT NULL REFERENCES facilities (id),
    trucking_org_id      uuid REFERENCES organizations (id),
    container_number     text NOT NULL CHECK (container_number ~ '^[A-Z]{3}[UJZ][0-9]{7}$'),
    direction            text NOT NULL CHECK (direction IN ('in', 'out')),
    condition            text NOT NULL CHECK (condition IN ('sound', 'damaged')),
    seal_number          text,
    remarks              text,
    status               text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'issued', 'confirmed', 'disputed')),
    recorded_by          uuid NOT NULL REFERENCES users (id),
    issued_at            timestamptz,
    driver_user_id       uuid REFERENCES users (id),
    driver_responded_at  timestamptz,
    driver_comment       text,
    created_at           timestamptz NOT NULL DEFAULT now(),
    UNIQUE (facility_visit_id, direction, container_number),
    CHECK (status = 'draft' OR issued_at IS NOT NULL),
    CHECK (status NOT IN ('confirmed', 'disputed') OR driver_responded_at IS NOT NULL),
    CHECK (status <> 'disputed' OR length(trim(coalesce(driver_comment, ''))) > 0)
);
CREATE INDEX ix_eirs_facility ON eirs (facility_id, created_at DESC);
CREATE INDEX ix_eirs_trucking_org ON eirs (trucking_org_id, created_at DESC);
CREATE INDEX ix_eirs_container ON eirs (container_number);

CREATE TABLE eir_damage_marks (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    eir_id       uuid NOT NULL REFERENCES eirs (id) ON DELETE CASCADE,
    side         text NOT NULL CHECK (side IN ('front', 'rear', 'left', 'right', 'roof', 'floor', 'door', 'interior')),
    position     text,
    damage_type  text NOT NULL CHECK (damage_type IN ('dent', 'hole', 'cut', 'bent', 'rust', 'crack', 'missing_part', 'dirty', 'other')),
    note         text,
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_eir_damage_marks_eir ON eir_damage_marks (eir_id);

-- Photos from the depot (party = 'facility') or from the driver when disputing (party = 'driver').
CREATE TABLE eir_photos (
    eir_id      uuid NOT NULL REFERENCES eirs (id) ON DELETE CASCADE,
    file_id     uuid NOT NULL REFERENCES files (id),
    party       text NOT NULL CHECK (party IN ('facility', 'driver')),
    side        text CHECK (side IN ('front', 'rear', 'left', 'right', 'roof', 'floor', 'door', 'interior')),
    added_by    uuid NOT NULL REFERENCES users (id),
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (eir_id, file_id)
);

-- Who uploaded a file, so access can be checked (T38).
ALTER TABLE files ADD COLUMN uploaded_by uuid REFERENCES users (id);
