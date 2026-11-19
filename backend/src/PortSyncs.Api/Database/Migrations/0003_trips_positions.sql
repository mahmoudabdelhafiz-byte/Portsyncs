-- 0003: trips and GPS positions (T21)
-- Location is collected only during an active trip, after the driver's consent.
-- Positions are partitioned by UTC day so retention (7 days on the free tier) is a cheap DROP.

CREATE TABLE trips (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id  uuid NOT NULL REFERENCES organizations (id),
    truck_id         uuid NOT NULL REFERENCES trucks (id),
    driver_id        uuid NOT NULL REFERENCES drivers (id),
    user_id          uuid NOT NULL REFERENCES users (id),
    status           text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'ended')),
    consent_at       timestamptz NOT NULL,
    started_at       timestamptz NOT NULL DEFAULT now(),
    ended_at         timestamptz,
    end_reason       text CHECK (end_reason IN ('driver', 'company', 'stale', 'replaced')),
    last_ts          timestamptz,
    last_lat         double precision CHECK (last_lat BETWEEN -90 AND 90),
    last_lon         double precision CHECK (last_lon BETWEEN -180 AND 180),
    last_speed_mps   real,
    last_heading     real,
    created_at       timestamptz NOT NULL DEFAULT now(),
    CHECK ((status = 'active') = (ended_at IS NULL))
);
CREATE INDEX ix_trips_org_status ON trips (organization_id, status);
-- One active trip per truck and per driver at a time.
CREATE UNIQUE INDEX ux_trips_active_truck ON trips (truck_id) WHERE status = 'active';
CREATE UNIQUE INDEX ux_trips_active_user ON trips (user_id) WHERE status = 'active';

CREATE TABLE positions (
    trip_id      uuid NOT NULL,
    ts           timestamptz NOT NULL,
    lat          double precision NOT NULL CHECK (lat BETWEEN -90 AND 90),
    lon          double precision NOT NULL CHECK (lon BETWEEN -180 AND 180),
    accuracy_m   real CHECK (accuracy_m >= 0),
    speed_mps    real CHECK (speed_mps >= 0),
    heading      real CHECK (heading >= 0 AND heading < 360),
    received_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (trip_id, ts)
) PARTITION BY RANGE (ts);

-- Creates the partition for one UTC day if it does not exist yet.
CREATE FUNCTION ensure_positions_partition(day date) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
    part text := 'positions_' || to_char(day, 'YYYYMMDD');
BEGIN
    EXECUTE format(
        'CREATE TABLE IF NOT EXISTS %I PARTITION OF positions FOR VALUES FROM (%L) TO (%L)',
        part,
        (day::timestamp AT TIME ZONE 'UTC'),
        ((day + 1)::timestamp AT TIME ZONE 'UTC'));
END $$;

-- Drops whole days older than `keep_from` (retention). Returns how many partitions were dropped.
CREATE FUNCTION drop_positions_partitions_before(keep_from date) RETURNS int
LANGUAGE plpgsql AS $$
DECLARE
    child record;
    dropped int := 0;
BEGIN
    FOR child IN
        SELECT c.relname
        FROM pg_inherits i
        JOIN pg_class c ON c.oid = i.inhrelid
        JOIN pg_class p ON p.oid = i.inhparent
        WHERE p.relname = 'positions'
          AND c.relname ~ '^positions_[0-9]{8}$'
          AND c.relname < 'positions_' || to_char(keep_from, 'YYYYMMDD')
    LOOP
        EXECUTE format('DROP TABLE %I', child.relname);
        dropped := dropped + 1;
    END LOOP;
    RETURN dropped;
END $$;

-- Partitions for the last 7 days and the next 2, so the API can accept data right after deployment.
SELECT ensure_positions_partition(d::date)
FROM generate_series(current_date - 7, current_date + 2, interval '1 day') AS d;
