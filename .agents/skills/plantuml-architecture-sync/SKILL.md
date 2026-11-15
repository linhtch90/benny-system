---
name: plantuml-architecture-sync
description: >-
  Use this skill when creating, updating, or rendering PlantUML (.puml)
  system design and sequence diagrams in docs/architecture/.
---

# Skill: Synchronize and Render PlantUML Architecture Diagrams

All system design diagrams live in `docs/architecture/` as version-controlled `.puml` files alongside their rendered `.svg` and `.png` outputs.

## Diagram Inventory
1. `docs/architecture/system-architecture.puml` — C4 Container / Component topology of Edge, Core Services, L1/L2 Cache, Kafka Backbone, Polyglot Data Stores, and Observability.
2. `docs/architecture/data-flow-sequence.puml` — End-to-end synchronous fast path (<15ms P99) and asynchronous event streaming / OLAP ingestion sequence.
3. `docs/architecture/scaling-resilience-topology.puml` — Multi-AZ high availability, replication, Polly v8 resilience, and KEDA/HPA horizontal scaling topology.

## Workflow to Update & Render Diagrams
1. Edit the target `.puml` file in `docs/architecture/`.
   - Note: Avoid `!theme` directives that require newer PlantUML versions if running on system PlantUML `1.2020.x`; use explicit `skinparam` blocks instead.
   - In sequence diagrams, use `par ... else ... end` for parallel fragments.
2. Render both SVG and PNG formats using the CLI:
   ```bash
   plantuml -tsvg docs/architecture/*.puml
   plantuml -tpng docs/architecture/*.puml
   ```
3. Verify that the command exits with code `0` and that both `.svg` and `.png` files are updated.
