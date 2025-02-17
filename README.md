# Real-Time Data Sync (Postgres → Kafka → C# Consumer → Cloud Postgres)

This project demonstrates a **Change Data Capture (CDC)** pipeline using **Debezium** + **Kafka** to replicate data from a **local Postgres** database into a **cloud Postgres** database (e.g., AWS RDS). A **C# consumer** reads CDC events from Kafka and applies inserts/updates/deletes to the cloud DB in real-time. Optionally, **Prometheus** and **Grafana** are included for metrics and dashboards.

---

## 1. Overview

- **Local Postgres**: Runs in Docker, configured with logical decoding.
- **Debezium (Kafka Connect)**: Captures changes from local Postgres (`INSERT`, `UPDATE`, `DELETE`) and publishes them to a Kafka topic.
- **Kafka**: Receives CDC events; acts as a buffer/queue.
- **C# Consumer**: Subscribes to the Kafka topic, parses Debezium messages, and **upserts** or **deletes** in the cloud Postgres DB.
- **Cloud Postgres**: Hosted on AWS RDS. Stores final replicated data.
- **Prometheus & Grafana**: For monitoring metrics (message throughput, CDC latency, consumer metrics and some inbuilt metrics).

**Key Features**:
- **Idempotent** writes via `INSERT ... ON CONFLICT DO UPDATE`.
- **Real-time** data sync from local → cloud DB.
- **Handles** inserts, updates, deletes consistently.
- **Logging** in the C# consumer to track events.

---

## 2. Prerequisites

- **Docker** & **Docker Compose** installed.
- **.NET 6 or 7 SDK** (if you want to rebuild the C# consumer locally).
- **AWS RDS** Postgres instance (or any other cloud Postgres) accessible from your machine.

---

## 3. Project Structure
Project_SYNC/ (example name) ├── CloudSyncConsumer/ │ ├── Program.cs │ ├── CloudSyncConsumer.csproj │ ├── Dockerfile │ └── ... ├── Kafka/ │ ├── docker-compose.yml (or combined in a single compose) │ └── connector-config.json ├── prometheus/ │ └── prometheus.yml (optional) └── ...


- **CloudSyncConsumer**: The C# consumer code + Dockerfile.
- **Kafka**: Docker Compose configs for Kafka, Zookeeper, Debezium, etc.
- **prometheus**: Config for Prometheus (optional).

---

## 4. Quick Start

1. **Clone or Download** this repository.

2. **Configure** your environment:
   - **Local Postgres** credentials (in `docker-compose.yml`).
   - **Cloud DB** credentials in `CLOUD_DB_CONNECTION` environment variable (see `.env` or `docker-compose.yml`).

3. **Start the Stack**:
   ```bash
   docker-compose up -d

This brings up Zookeeper, Kafka, local Postgres, Debezium, (optionally) Prometheus, Grafana, and the C# consumer container.

4. **Register Debezium Connector (if not auto-registered)**:
   ```bash
      curl -i -X POST -H "Accept:application/json" -H "Content-Type:application/json" \
          --data @connector-config.json \
          http://localhost:8083/connectors/
   
  Adjust the `connector-config.json` to match your local Postgres settings (host, port, user, etc.)

5. **Check logs**:
    ```bash   
      docker logs -f csharp-consumer
You should see the consumer subscribing to `local-postgres.public.mytable`

6. **Verify**:
  - Insert or update rows in local Postgres:
    ```bash
     docker exec -it local-postgres psql -U postgres -d testdb \
    -c "INSERT INTO mytable (name, description) VALUES ('Test Sync','CDC event');"
  - The consumer logs show an upsert event.
  - **Cloud Postgres** table should reflect the new data.

---

## 5. Testing

- **Insert** multiple rows locally, check they appear in the cloud DB.
- **Update** or **Delete** locally, verify the consumer logs and final cloud DB state.
- View logs in `docker logs -f csharp-consumer` or the Debezium container (`docker logs kafka-connect`).

---

## 6. Prometheus & Grafana

- If you included Prometheus and Grafana in `docker-compose.yml`, open:
  - **Prometheus**: http://localhost:9090
  - **Grafana**: http://localhost:3000 (default user/pass = admin/admin)
- **Data Source** in Grafana: point to http://prometheus:9090.
- Add or import dashboards. If you exposed csharp-consumer metrics on port 1234 and added it to `prometheus.yml`, you can track CDC latency, upserts, etc.

---

## 7. Cleanup

    docker-compose down
Stops all containers. Data in volumes may persist (especially for Postgres). Remove volumes if you want a fresh start:
    
    docker-compose down -v

---
    
## 8. License & Credits

- This project uses:
  - **Debezium** (Apache License 2.0)
  - **Confluent** Kafka images
  - **Postgres** official Docker image
  - **C#** code with `.NET`
See Debezium Documentation and prometheus-net for more details.

---
