-- 0004: facilities (depots first), pre-announced visits and the gate log (T30, T52)

CREATE TABLE facilities (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    type             text NOT NULL CHECK (type IN ('depot', 'terminal', 'warehouse', 'customer_site')),
    name             text NOT NULL CHECK (length(trim(name)) BETWEEN 2 AND 200),
    country          char(2) NOT NULL CHECK (country ~ '^[A-Z]{2}$'),
    city             text,
    address          text,
    -- Simple circular geofence until PostGIS polygons arrive (T85).
    lat              double precision CHECK (lat BETWEEN -90 AND 90),
    lon              double precision CHECK (lon BETWEEN -180 AND 180),
    radius_m         int CHECK (radius_m BETWEEN 50 AND 5000),
    -- The facility can exist before its operator joins (needed for gate waiting times).
    operator_org_id  uuid REFERENCES organizations (id),
    status           text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive')),
    created_at       timestamptz NOT NULL DEFAULT now(),
    CHECK ((lat IS NULL) = (lon IS NULL))
);
CREATE INDEX ix_facilities_operator ON facilities (operator_org_id);
CREATE INDEX ix_facilities_country_type ON facilities (country, type) WHERE status = 'active';

-- A trucking company announces a truck or container is coming (T52).
CREATE TABLE expected_visits (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    facility_id       uuid NOT NULL REFERENCES facilities (id),
    trucking_org_id   uuid NOT NULL REFERENCES organizations (id),
    truck_id          uuid REFERENCES trucks (id),
    container_number  text CHECK (container_number ~ '^[A-Z]{3}[UJZ][0-9]{7}$'),
    purpose           text NOT NULL CHECK (purpose IN ('empty_pickup', 'empty_return', 'full_pickup', 'full_delivery')),
    window_from       timestamptz NOT NULL,
    window_to         timestamptz NOT NULL,
    status            text NOT NULL DEFAULT 'expected' CHECK (status IN ('expected', 'arrived', 'completed', 'cancelled')),
    created_by        uuid NOT NULL REFERENCES users (id),
    created_at        timestamptz NOT NULL DEFAULT now(),
    CHECK (window_to > window_from),
    CHECK (truck_id IS NOT NULL OR container_number IS NOT NULL)
);
CREATE INDEX ix_expected_visits_facility ON expected_visits (facility_id, window_from) WHERE status = 'expected';
CREATE INDEX ix_expected_visits_org ON expected_visits (trucking_org_id, window_from);

-- The gate log: one row per truck visit, gate-in to gate-out (T52).
-- Unregistered trucks are logged by plate; truck_id stays empty.
CREATE TABLE facility_visits (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    facility_id        uuid NOT NULL REFERENCES facilities (id),
    truck_id           uuid REFERENCES trucks (id),
    plate_country      char(2) NOT NULL CHECK (plate_country ~ '^[A-Z]{2}$'),
    plate_normalized   text NOT NULL,
    trucking_org_id    uuid REFERENCES organizations (id),
    expected_visit_id  uuid REFERENCES expected_visits (id),
    container_in       text CHECK (container_in ~ '^[A-Z]{3}[UJZ][0-9]{7}$'),
    container_out      text CHECK (container_out ~ '^[A-Z]{3}[UJZ][0-9]{7}$'),
    gate_in_at         timestamptz NOT NULL DEFAULT now(),
    gate_out_at        timestamptz,
    method             text NOT NULL CHECK (method IN ('manual', 'qr', 'gps', 'gate_system')),
    recorded_in_by     uuid REFERENCES users (id),
    recorded_out_by    uuid REFERENCES users (id),
    note               text,
    created_at         timestamptz NOT NULL DEFAULT now(),
    CHECK (gate_out_at IS NULL OR gate_out_at >= gate_in_at)
);
CREATE INDEX ix_facility_visits_facility_in ON facility_visits (facility_id, gate_in_at DESC);
CREATE INDEX ix_facility_visits_org ON facility_visits (trucking_org_id, gate_in_at DESC);
-- A plate can be inside a facility only once at a time.
CREATE UNIQUE INDEX ux_facility_visits_inside ON facility_visits (facility_id, plate_country, plate_normalized)
    WHERE gate_out_at IS NULL;
