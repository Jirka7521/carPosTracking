-- Runs automatically the first time the database is initialized.
-- Enables PostGIS so you can use geometry/geography columns and spatial queries.
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;
