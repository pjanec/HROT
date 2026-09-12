# ADA-BATCH-12 Review (AI Behavior Traces — arming seam + extraction, Group K + MCP)

**Verdict:** ACCEPTED (first pass). **Reviewer:** dev lead (full build + diff + the live buffer
absent→populated proof + live trace extraction + independent `npm run verify` + orphan check).
**This was the one genuine new engine seam — and it holds up live.**

## Verified independently (lead)
- Full build → 0 errors. `dotnet test … --filter DebugApi` → **97/97** (5 new).
- **The crux — engine seam proof on the live process:**
  - `GET /entities/1000/trace` BEFORE arming → `traceArmed:false`, `nodeHistory:[]` (empty — the base-no-op
    signature).
  - `POST /trace/observe {networkId:1000, on:true}` → `armed:true`; play + step 20.
  - AFTER → `traceArmed:true`, `activeNode` populated, **`nodeHistory` 0→1** (`{nodeVisualId, status:"Running",
    timestamp}`). The buffer was allocated and recorded — i.e. `DebugState.Flags` was actually set and
    `TraceBufferLifecycleSystem` ran, NOT the base no-op. ✅
- **`npm run verify` (independent) → 178/0, VERIFICATION PASSED**, orphan before=0 after=0. Step 13 drives the
  full arm → step → extract (BTree trace) → disarm cycle through MCP.

## Diff review
- **`EditorAiTracerCoordinator`** (new): overrides the base no-op; `ArmEntity`/`DisarmEntity` publish
  `PatchDebugStateCommand{Behavior:{EnableTraceBuffer:true/false}}` → `DebugStatePatchSystem` (Input) →
  `TraceBufferLifecycleSystem` (BeforeSync) allocates `BTreeTraceWorkingMemory1024`/`HsmTraceWorkingMemory1024`.
  Wired in place of `new AiTracerCoordinator()` at `EditorSubsystem.cs:690`; debug sessions + hot-reload hooks
  preserved. Sound and well-reasoned.
- **Entity-centric arming** (networkId → Entity → command) rather than asset-centric — correct call: the
  runtime `BehaviorState.ActiveBehaviorHash` is an `int` with no Guid mapping, so asset-centric arming isn't
  expressible. This is exactly the right primitive for the Debug API. (Asset-centric deferred: ADA-12-D02.)
- `GetEntityTrace` handles BTree (active node + history), HSM (`HsmDebugSession`), blueprint
  (`CaptureLiveState`, needs `BlueprintBound`). Serialized via `EventSerializationHelper`. MCP `observe_trace`
  + `get_entity_trace` added (44 tools).

## Debt (migrated by lead to the central tracker — agent logged it only in the report)
- **ADA-12-D01:** blueprint trace needs `BlueprintBound`; full `DebugProbe.Sink` path deferred.
- **ADA-12-D02:** asset-centric arming deferred (no runtime Guid→hash map); entity-centric implemented.
- **ADA-12-D03:** HSM not Tier-1-tested (no HSM entity in fixtures); code path exists, BTree fully verified.
- Updated ADA-06-D01: Group K MCP tools done; only Group L (mutation) remains.

## Lesson
The highest-uncertainty batch (real new engine code) and it passed the crux on the first review — because the
crux was specified as a concrete observable: `nodeHistory` empty→populated, not "traces work." That absent→
populated assertion is exactly what distinguishes a real arming seam from the base no-op, and it's what I
checked live. Spec the falsifiable symptom and the gate becomes decisive.
