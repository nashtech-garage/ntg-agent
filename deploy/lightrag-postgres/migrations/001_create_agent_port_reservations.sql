-- 001_create_agent_port_reservations.sql
--
-- Global LightRAG host-port ledger for the shared Docker host.
--
-- WHY
--   Every developer runs their own local SQL Server, but the whole team shares ONE Ubuntu
--   Docker host. Allocating ports by scanning each developer's local DB meant two people
--   independently picked the same port (e.g. 20001) and the second `docker start` failed
--   with "port is already allocated". This table is the single authority every Orchestrator
--   reserves through, so UNIQUE(port) makes cross-developer double-allocation impossible.
--
-- SEMANTICS
--   * One row per agent (PRIMARY KEY agent_id) — a port is identity-bound to its agent.
--   * UNIQUE(port) is the arbiter: two agents can never hold the same port.
--   * A reservation is released only when the agent is deleted, returning the port to the pool.
--   * The valid port range stays application-configured (LightRag:PortRangeStart/PortRangeEnd),
--     deliberately NOT hard-coded as a CHECK here, so the range can be retuned without a migration.
--
-- HOW TO RUN (once, against the shared Postgres — the app does NOT create this table)
--   From the Mac, over the `ssh -L 55432:127.0.0.1:5432` tunnel:
--     psql -h localhost -p 55432 -U postgres -d uploaded-documents \
--          -f deploy/lightrag-postgres/migrations/001_create_agent_port_reservations.sql
--
--   Or directly on the VM:
--     psql -h localhost -p 5432 -U postgres -d uploaded-documents \
--          -f 001_create_agent_port_reservations.sql
--
--   To verify afterwards:
--     \d agent_port_reservations

\c "uploaded-documents"

CREATE TABLE agent_port_reservations (
    agent_id    uuid        NOT NULL,
    port        integer     NOT NULL,
    reserved_at timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_agent_port_reservations      PRIMARY KEY (agent_id),
    CONSTRAINT uq_agent_port_reservations_port UNIQUE (port)
);

COMMENT ON TABLE agent_port_reservations IS
    'Global ledger of LightRAG container host-port reservations, shared by every developer''s Orchestrator. One row per agent; UNIQUE(port) prevents cross-developer port collisions on the shared Docker host.';
