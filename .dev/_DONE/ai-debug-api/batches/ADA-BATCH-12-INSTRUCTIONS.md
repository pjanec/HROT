# ADA-BATCH-12: AI Behavior Traces — Arming Seam + Extraction (Group K) + MCP tools

**Batch Number:** ADA-BATCH-12
**Tasks:** ADA-P6-T01 (live trace arming seam) + ADA-P6-T02 (trace extraction) + Group K MCP tools
**Phase:** Phase 6 — per-entity BTree/HSM/blueprint behavior traces. **Contains the ONE genuine new engine seam.**
**Estimated Effort:** ~24 hours (highest-uncertainty batch — real engine code, not wiring)
**Executor:** sonnet
**Priority:** HIGH (the sim exists to run AI behaviors — this is the core diagnostic for them)
**Dependencies:** Phase 1 + P-MCP + BATCH-07..11.

---

## Onboarding & Workflow

Extract live per-entity AI behavior traces (BTree active-node path + history; HSM active state + transitions;
blueprint live state). This REQUIRES first *arming* tracing so the engine allocates trace buffers — that
arming is the one piece of genuinely new engine code in this whole workstream. **Investigate carefully and
verify the buffer actually populates — do NOT assume.**

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/_DONE/ai-debug-api/reviews/ADA-BATCH-09/10/11-REVIEW.md` (the live-reproduce gate; the false-green-verify
   recurring failure — RE-RUN `npm run verify` to a real tally before reporting).
3. **Design:** `.dev/_DONE/ai-debug-api/DESIGN.md` — Group K (AI behavior traces) + New Work #1 (the arming seam).
4. **Task detail:** `.dev/_DONE/ai-debug-api/TASK-DETAIL.md` — ADA-P6-T01, ADA-P6-T02.

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY. If the engine seam proves
> intractable in the available time, report exactly what you found and what's blocking — do NOT fake trace
> data or claim populated buffers you didn't observe. The lead re-runs everything live.

### CURRENT STATE (lead-confirmed — important)
- The editor builds **`new AiTracerCoordinator()`** at `EditorSubsystem.cs:690` — the BASE class. Its
  `BeginObservingAssetImpl`/`EndObservingAssetImpl` are **`protected virtual {}` NO-OPs**
  (`Hrot/Editor/Hrot.Editor.AiShared/Debug/AiTracerCoordinator.cs:58,61`). So nothing currently sets
  `DebugState.Flags`, and `TraceBufferLifecycleSystem` never allocates the trace buffers via this path. THIS
  IS THE SEAM TO IMPLEMENT.
- `_btreeDebugSession`/`_hsmDebugSession` are built from that coordinator (`EditorSubsystem.cs:691-692`).
- Public coordinator API: `AddObserver(Guid assetId, TraceLevel)`, `RemoveObserver(Guid assetId)`,
  `IsObserving`, `GetEffectiveLevel`. The overridable hooks are `BeginObservingAssetImpl(assetId, level)` /
  `EndObservingAssetImpl(assetId)`.

### Infra to study / reuse
- `TraceBufferLifecycleSystem` + `BTreeTraceWorkingMemory1024` / `HsmTraceWorkingMemory1024`
  (`FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/`) — how `DebugState.Flags` drives buffer allocation; how
  an entity is matched to a behavior `assetId`.
- `BTreeDebugSession.GetCurrentStateSnapshot`/`GetRecentNodeHistory`
  (`Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/`), `HsmDebugSession` equivalents,
  `BlueprintDebugSession.CaptureLiveState(entity, assetId)` (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`).
- Blueprints trace via `DebugProbe.Sink`, NOT `AiTracerCoordinator` — handle per asset type.
- `EventSerializationHelper.SerializeToJson` for readable DTO output.
- The `test-move` scenario's entity 1000 (M2 Bradley) has `BrainBTreeState` + `BrainBlackboard` → a live
  BTree-driven entity to arm/extract against.

---

## Scope / Endpoints (authoritative spec in TASK-DETAIL / DESIGN Group K)
### Arming (T01) — the engine seam
- Implement an editor `AiTracerCoordinator` subclass (or equivalent wiring) whose `BeginObservingAssetImpl`
  sets `DebugState.Flags` on entities running `assetId` so `TraceBufferLifecycleSystem` allocates their
  `BTreeTraceWorkingMemory1024`/`HsmTraceWorkingMemory1024`; `EndObservingAssetImpl` clears it. Wire this
  subclass in place of the base `new AiTracerCoordinator()` at `EditorSubsystem.cs:690` (keep the debug
  sessions working). Register a blueprint `DebugMap` for field decode; route blueprint entities via
  `DebugProbe.Sink`.
- `POST /trace/observe {networkId | assetId, on}` → arm/disarm (resolve networkId→assetId as needed).

### Extraction (T02)
- `GET /entities/{networkId}/trace` → BTree: active node path + recent node history; HSM: active leaf +
  recent transitions; blueprint: `CaptureLiveState` snapshot (no pause). Serialize via `EventSerializationHelper`.

## MCP tools (Group K)
- Add `observe_trace` (1:1 with `/trace/observe`) and `get_entity_trace` (1:1 with `/entities/{id}/trace`).
  Update README + ADA-06-D01 (Group K). Extend `verify.mjs` with an arm→tick→extract flow. RE-RUN verify green.

## Verification (the buffer-allocation proof is the crux)
- **Tier-1 (EditorHarness):**
  1. Confirm a target BTree entity's `BTreeTraceWorkingMemory1024` is ABSENT/empty before arming; after
     `observe {on:true}` + pumping a few frames, it is PRESENT/populated. (This proves the seam — the base
     no-op would leave it empty.) Disarm → allocation/population stops.
  2. `GET /entities/{id}/trace` returns the active node path + recent history for the armed BTree entity.
  3. HSM equivalent if an HSM entity is reachable in a fixture; blueprint via `DebugProbe.Sink` if reachable.
     If a given behavior type isn't reachable in the harness, cover what IS and log the rest as honest debt.
- **Tier-2 (live headless / MCP `verify.mjs`):** load test-move → `observe_trace {networkId:1000, on:true}` →
  play/step a few → `get_entity_trace {1000}` returns a non-empty BTree trace (active node path). Re-runnable;
  no orphans. RE-RUN `npm run verify` to a real PASS tally before reporting.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- This is the one genuine engine seam — **prove buffer allocation actually happens** (absent→populated), don't
  assume the base no-op got overridden. Live-only (replay traces need no arming; out of scope here).
- Extraction marshalled (touches `_world`/sessions). Serialize via `EventSerializationHelper`.
- Don't break existing debug-session wiring or hot-reload (`_aiCoordinator.OnReload*` hooks reference the
  tracer/sessions). Frozen `TestAssets`; never the production scan path; never regenerate snapshots.

## Deliverables
- Code (the arming subclass + endpoints + MCP tools) + green tests + extended MCP verify + README.
- `.dev/_DONE/ai-debug-api/reports/ADA-BATCH-12-REPORT.md` (DEV-GUIDE format): built, decisions/deviations, the
  **buffer absent→populated proof** + FULL `dotnet test` summary + the live arm→extract reproduce output
  (a real BTree active-node path), blockers, debt → DEBT-TRACKER (update ADA-06-D01 for Group K; log any
  behavior type not covered).
