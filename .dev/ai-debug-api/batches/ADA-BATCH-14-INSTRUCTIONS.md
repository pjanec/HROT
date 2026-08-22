# ADA-BATCH-14: Managed-Event Discovery (T06b) + Manual-Assist Focus/Annotations (P9) + MCP

**Batch Number:** ADA-BATCH-14 (final tracked batch)
**Tasks:** ADA-P1-T06b (managed-event discovery — closes ADA-04-D02) + ADA-P9-T01 (focus + annotations) + MCP tools
**Phase:** Cleanup + manual-session assistance
**Estimated Effort:** ~10 hours
**Executor:** sonnet (T06b needs an engine-enumeration decision)
**Priority:** MEDIUM (completes the workstream's tracked scope)
**Dependencies:** Phase 1 + P-MCP + BATCH-04..13.

---

## Onboarding & Workflow

Two loose ends: (1) make `/commands` ALSO list managed events (currently unmanaged-only — ADA-04-D02);
(2) the Tier-3 manual-session helpers (focus camera on an entity + debug annotations) for a human watching.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/ai-debug-api/reviews/ADA-BATCH-12/13-REVIEW.md` (the live gate; the false-green-verify recurring
   failure — RE-RUN `npm run verify` to a real tally; assert the real symptom, not the happy path).
3. **Design:** `.dev/ai-debug-api/DESIGN.md` — Group F (commands/discovery + focus); the ADA-04-D02 note.
4. **Task detail:** `.dev/ai-debug-api/TASK-DETAIL.md` — ADA-P1-T06b (in TASK-TRACKER), ADA-P9-T01.

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY. RE-RUN `npm run verify`
> to a real PASS tally before reporting — do NOT claim verify-green without the tally (this has bitten twice).
> Run the FULL build.

### Context
- **T06b:** `GET /commands` currently lists only unmanaged `[EventId]` events via
  `EventType.GetAllRegistered()` (which is `where T : unmanaged`). Managed events (e.g. `SpawnEntityCommand`,
  `MissionControlIntent`) are NOT discoverable. Managed event streams live in `FdpEventBus._managedStreams`
  (registered via `RegisterManaged<T>()` / first `PublishManaged<T>`). Decide the cleanest enumeration:
  - a bus-level `GetRegisteredManagedEventTypes()` seam (returns the Types of registered managed streams), OR
  - an assembly scan for managed-event types (by a marker/convention), if there's a reliable marker.
  Either way, MERGE managed events into `/commands` output (tag each entry `managed:true/false`) with the same
  `JsonShapeDescriber` field schema. Document the completeness caveat (a lazily-registered managed event only
  appears once registered, if you use the bus path).
- **P9:** `POST /entities/{networkId}/focus` → publish `CenterOnEntityCommand` (already registered).
  `POST /annotations {…}` → draw markers via the gizmo `DebugPrimitiveBuffer`. Publish-only / buffer-write;
  the actual camera move + gizmo render happen in the render loop (NOT headless) → those are manual-verify.

---

## Endpoints
- `GET /commands` — extend to include managed events (tagged `managed:true`), merged with the existing
  unmanaged list. (Closes ADA-04-D02.)
- `POST /entities/{networkId}/focus` → publish `CenterOnEntityCommand` (marshalled). Return `{ focused:true }`.
- `POST /annotations {type, ...}` → write to the gizmo `DebugPrimitiveBuffer` (highlight entity/point/line).
  Return `{ added:true }` (or an annotation id).

## MCP tools
- Add `focus_entity` (1:1 with `/focus`), `add_annotation` (1:1 with `/annotations`). `list_commands` output
  now includes managed events — no new tool needed, just richer output. Update README. Extend `verify.mjs`.

## Verification (headless-verifiable parts + honest manual-verify notes)
- **Tier-1 (EditorHarness):**
  - T06b: `GET /commands` now includes a known managed event (e.g. `SpawnEntityCommand`) with a field schema,
    AND still includes the unmanaged ones. Assert both present + the `managed` tag.
  - focus: `POST …/focus` publishes a `CenterOnEntityCommand` → it appears in `GetEvents("world",
    "CenterOnEntityCommand")` (verifiable headless — the command publish, not the camera move).
  - annotations: `POST /annotations` results in an entry in the `DebugPrimitiveBuffer` (assert the buffer
    received it, headless — the render is not verifiable).
- **Tier-2 (live headless / MCP `verify.mjs`):** `list_commands` includes a managed event; `focus_entity
  {1000}` → ok + the command in event history; `add_annotation {...}` → ok. Re-runnable; no orphans. RE-RUN
  `npm run verify` green.
- **Manual-verify only (document, do not fake):** the actual camera centering (P9 SC#1) and the marker being
  visible on the map (P9 SC#2) require a windowed session — note them as manual-verify in the report; the
  headless gate covers the publish/buffer-write path only.
- `dotnet build IOS-IG-SimHost.sln`; `dotnet test … --filter "FullyQualifiedName~DebugApi"`.

## Constraints (hard)
- `/commands` managed-event enumeration must not break the existing unmanaged output or the existing tests.
- focus/annotations are publish-only / buffer-write, marshalled; never touch the render loop from the API
  thread. Frozen `TestAssets`; never the production scan path; never regenerate snapshots.

## Deliverables
- Code + green tests; extended MCP `verify.mjs`; README updated.
- `.dev/ai-debug-api/reports/ADA-BATCH-14-REPORT.md` (DEV-GUIDE format): built, decisions (managed-event
  enumeration approach + completeness caveat), FULL `dotnet test` summary, the headless reproduce output
  (managed event in `/commands`; focus command in history; annotation buffered) + explicit manual-verify
  notes for the visual parts, blockers, debt → DEBT-TRACKER (close ADA-04-D02).
