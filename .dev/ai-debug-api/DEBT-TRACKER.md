# AI Debug & Test API — Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| ADA-01-D01 | ADA-BATCH-01 | `EventSerializationHelper` entity-ref resolution deferred — `IGuidResolver` parameter accepted but not used until `NetworkEntityMap` is wired in. | P3 | ADA-BATCH-02+ | OPEN |
| ADA-01-D02 | ADA-BATCH-01 review | POST endpoints require `Content-Length` (HttpListener returns 411 on bodyless POST). MCP/clients must send it (fetch does automatically); document for manual `curl`. | P3 | ADA-PM | OPEN |
| ADA-01-D03 | ADA-BATCH-01 review | `/shutdown` breaks only the headless `Run()` loop, not the windowed Raylib loop. Acceptable (API headless-primary). | P3 | — | OPEN |
| ADA-02-D01 | ADA-BATCH-02 | `/scenario/load?waitForReady` polls cluster state once per job-queue drain in a host-side loop (`ScenarioReadyMaxPolls=600`). Robust but coarse; a one-shot completion TCS keyed off `ClusterStateUpdateEvent` would be cleaner. | P3 | ADA-P2+ | OPEN |
| ADA-02-D02 | ADA-BATCH-02 | `/scenario/save` uses `IEditorLogic.SaveScenarioAs(name)` which writes under the editor's `ScenariosRoot` (environment-dependent). Tier-1 save round-trip is verified via the explicit-path `SaveScenario(file)` instead; a hermetic name-based save round-trip is not yet covered. | P3 | ADA-P1+ | OPEN |
| ADA-02-D03 | ADA-BATCH-02 | C2 headless process smoke (`DebugApiHeadlessSmokeTests`) is gated behind `ADA_RUN_HEADLESS_SMOKE=1` (editor boot is heavy/env-sensitive). Runnable on demand; the dev lead re-runs it manually. | P3 | ADA-PM | OPEN |
| ADA-02-D04 | ADA-BATCH-02 | `DebugApiService.Step(count)` delegates to the time facade's `Step()`, which sets the controller delta; the actual tick is applied on the next main-loop `Update()` drain. Exact N-tick advance is therefore loop-coupled (the Tier-1 test asserts non-regressing `totalTime`, not exact +N). | P3 | ADA-P3+ | OPEN |

| ADA-04-D01 | ADA-BATCH-04 | `/entities/command` wait-gating: `awaited:true` correlated-ack path (poll bus across ticks for `MissionControlAckEvent` by `RequestId`) not implemented. Returns `awaited:false, reason:"ack-wait not yet supported"` when sim is advancing. Requires multi-tick continuation mechanism. | P3 | ADA-BATCH-05+ | OPEN |
| ADA-04-D02 | ADA-BATCH-04 | `/commands` enumerates only unmanaged `[EventId]` events (`EventType.GetAllRegistered()` is `where T : unmanaged`). Managed events (SpawnEntityCommand, MissionControlIntent) are not discoverable via `/commands`. A managed event registry or reflection scan would be needed to cover them. Promoted to task T06b. | P3 | T06b | OPEN |
| ADA-05-D01 | ADA-BATCH-05 | `/tkb/types` `disType` serializes as the CLR type name (`"Fdp.Core.DISEntityType"`) rather than a meaningful DIS value — `DISEntityType.ToString()` is not overridden. Cosmetic; `tkbType`/`name`/`categoryPath` are still useful. Revisit if DIS-type filtering is needed. (`categoryPath` also empty for some templates that don't set it.) | P3 | (best-effort) | OPEN |
| ADA-06-D01 | ADA-BATCH-06 | MCP tools for Groups H (checkpoint), I (recording/replay), J (logs), K (traces), L (mutation) are absent — their HTTP endpoints are not yet built. Group G (breakpoints) landed in ADA-BATCH-07. Tools will be added in the batch that implements each group's endpoints. Documented in tools/ai-debug-mcp/README.md. | P3 | per endpoint batch | PARTIALLY RESOLVED (G done) |
| ADA-07-D01 | ADA-BATCH-07 | ~~End-to-end breakpoint hit coverage gap~~: **RESOLVED in ADA-BATCH-07 FIX 2**. A real `PropertyMatchDto` (SimTransform.Position.X GreaterThan -1e9) fires in the headless runner: `play` → set breakpoint → `isPaused:true`, `pausedTick>0`, `lastHit.networkId=1000`, `hitCount=1` all confirmed. Automated in `verify.mjs` Step 10c. Lead-verified manually and automated test passes green. | P3 | — | RESOLVED |

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
