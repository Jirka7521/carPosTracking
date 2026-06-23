# PostgreSQL + PostGIS container

PostgreSQL **18.4** with the **PostGIS 3.7** geospatial extension, for storing car
position data. The image is multi-arch and runs natively on arm64 (ARM Ubuntu).

## Setup

1. Copy the env template and set a real password:

   ```bash
   cp .env.example .env
   nano .env
   ```

2. Start it:

   ```bash
   docker compose up -d
   ```

3. Check it's healthy:

   ```bash
   docker compose ps
   docker compose logs -f postgres
   ```

PostGIS is enabled automatically on first start (see `initdb/01-enable-postgis.sql`).

## Connecting

```bash
# from the host
psql -h localhost -p 5432 -U carpos -d carpos

# or via the container
docker compose exec postgres psql -U carpos -d carpos
```

Verify PostGIS:

```sql
SELECT postgis_full_version();
```

## Data & lifecycle

- Data persists in the named Docker volume `pgdata` and survives `docker compose down`.
- Scripts in `initdb/` run **only once**, when the data volume is first created. To
  re-run them you must wipe the data.

```bash
docker compose down           # stop, keep data
docker compose down -v        # stop AND delete the data volume (destroys the DB)
docker compose pull           # fetch newer image, then `up -d` to recreate
```

## Notes

- `.env` holds the password and is gitignored — never commit it.
- To avoid exposing the DB to the network, change the port mapping in
  `docker-compose.yml` to `"127.0.0.1:${POSTGRES_PORT:-5432}:5432"`.
