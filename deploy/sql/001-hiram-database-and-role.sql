-- ADR-016: Hiram shares the EasyStok Postgres server with a dedicated database and a least-privilege
-- role. Run as a Postgres admin with psql. Do NOT use --single-transaction: CREATE DATABASE cannot run
-- inside a transaction block.
--
-- The password below is a placeholder. Set the real one out of band; it must match the password in
-- ConnectionStrings__Hiram.

-- 1. Least-privilege login role: no superuser, no createdb, no createrole, no bypassrls, no replication.
--    This is what keeps the EasyStok P0 class (a role that bypasses RLS) from coming back.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hiram') THEN
        CREATE ROLE hiram WITH LOGIN PASSWORD 'CHANGE_ME'
            NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION;
    END IF;
END
$$;

-- 2. Dedicated database owned by the hiram role. Idempotent via \gexec (psql only).
SELECT 'CREATE DATABASE hiram OWNER hiram'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'hiram')\gexec

-- 3. Only the hiram role connects to the hiram database.
REVOKE ALL ON DATABASE hiram FROM PUBLIC;
GRANT ALL PRIVILEGES ON DATABASE hiram TO hiram;

-- 4. Cross-database isolation (operator action on the EasyStok database, NOT run here):
--    By default any role may CONNECT to any database. To stop the hiram role from even connecting to
--    the ERP database, run against the EasyStok database as admin:
--        REVOKE CONNECT ON DATABASE <easystok_db> FROM PUBLIC;
--        GRANT  CONNECT ON DATABASE <easystok_db> TO <easystok_role>;
--    On Postgres 15+ PUBLIC has no CREATE on the public schema and table access needs explicit grants,
--    so even without this the hiram role cannot read EasyStok tables; the revoke closes the connect path.
