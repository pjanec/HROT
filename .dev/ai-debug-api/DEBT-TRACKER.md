# AI Debug & Test API — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| ADA-01-D01 | ADA-BATCH-01 | `EventSerializationHelper` entity-ref resolution deferred — `IGuidResolver` parameter accepted but not used until `NetworkEntityMap` is wired in. | P3 | ADA-BATCH-02+ | OPEN |
| ADA-01-D02 | ADA-BATCH-01 review | POST endpoints require `Content-Length` (HttpListener returns 411 on bodyless POST). MCP/clients must send it (fetch does automatically); document for manual `curl`. | P3 | ADA-PM | OPEN |
| ADA-01-D03 | ADA-BATCH-01 review | `/shutdown` breaks only the headless `Run()` loop, not the windowed Raylib loop. Acceptable (API headless-primary). | P3 | — | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)

---

## Known pre-existing issues to carry (not introduced by this workstream)

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| PRE-001 | DESIGN Group N / test-health | `SimTransformBridgeSystem.RotationToHeadingDeg` mishandles degenerate pitch-down (90°) rotation — returns 90 instead of 0. Not a blocker for planar authoring; fix only if vertical orientations matter. | P3 | — | OPEN |

---

## Deferred (future engine work, explicitly out of scope of these tasks)

- **Keyed multi-checkpoints** — only the single preview slot exists today (ADA-P3-T01). Retaining multiple
  named snapshots simultaneously needs a dedicated snapshot service (must not bypass `PreviewClusterOpHandler`).
- **Per-TKB-type attribute discovery** — `JsonAttributeCompiler.RegisteredPaths` is a single global registry
  (ADA-P8-T01); per-type narrowing is a future enhancement.
- **Live event streaming** (SSE/WebSocket/DDS) — superseded by event history for now.
- **MCP control of the Replay Browser** (`-m replaybrowser`) for post-mortem analysis — see DESIGN Future Directions.
- **Preview-recording ledger entry** — to make preview `.fdp`s visible in the Replay Browser GUI dropdown.
