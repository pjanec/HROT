# SumoSharp — vendored sources (READ-ONLY in HROT)

- repository: https://github.com/pjanec/SumoSharp
- ref: `hrot/slice2`
- commit: `eb88338d3a02376bac0c4cdd779bc879122a29fe`
- synced: 2026-11-17
- projects: Sim.Core Sim.Ingest Sim.Replication Sim.Replication.Dds Sim.Pedestrians Sim.LiveCity Sim.Evac Sim.Viewer.Motion Sim.Host Sim.Scene (+ src/Shared packaging/SumoSharp)

⛔ Do not edit these files in HROT. Change SumoSharp upstream, then run `scripts/sync-sumosharp.sh <ref>`;
`scripts/sync-sumosharp.sh --verify` fails if this copy differs from upstream at the commit above.
📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §11 (R-4046, R-4048).
