-- 0002: phone OTP login and refresh tokens (T20)

CREATE TABLE otp_challenges (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    phone        text NOT NULL CHECK (phone ~ '^\+[1-9][0-9]{6,14}$'),
    code_hash    text NOT NULL,           -- HMAC of the code, never the code itself
    attempts     int  NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    expires_at   timestamptz NOT NULL,
    consumed_at  timestamptz,
    client_ip    text,
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_otp_challenges_phone_created ON otp_challenges (phone, created_at DESC);

CREATE TABLE refresh_tokens (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      uuid NOT NULL REFERENCES users (id),
    token_hash   text NOT NULL UNIQUE,    -- SHA-256 of the token, never the token itself
    device_name  text,
    expires_at   timestamptz NOT NULL,
    revoked_at   timestamptz,
    replaced_by  uuid REFERENCES refresh_tokens (id),
    created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_refresh_tokens_user ON refresh_tokens (user_id);
