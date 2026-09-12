# ADA-BATCH-14 Review (Managed-Event Discovery T06b + Manual-Assist Focus/Annotations P9 + MCP)

**Verdict:** ACCEPTED (first pass). **Reviewer:** dev lead (full build + diff + live reproduce of the
headless-verifiable parts + independent `npm run verify` + orphan check). **Final tracked batch; closes
ADA-04-D02.**

## Verified independently (lead)
- **Full-solution build** → 0 errors (the shared `FdpEventBus.GetRegisteredManagedEventTypes()` addition is
  additive; full build confirms no ripple).
- `dotnet test … --filter DebugApi` → **115/115** (8 new).
- **Live reproduce:**
  - `GET /commands` → **91 total (20 managed + 71 unmanaged)**; `SpawnEntityCommand` present and tagged
    `managed:true`; unmanaged events still present. Closes ADA-04-D02. ✅
  - `POST /entities/1000/focus` → `ok, focused:true`; `CenterOnEntityCommand` appears in
    `GET /events?bus=world&type=CenterOnEntityCommand` (1 event). ✅
  - `POST /annotations {sphere}` → `ok, added:true`, buffer received it (`bufferCount` incremented). ✅
- `npm run verify` → **203/0, VERIFICATION PASSED**, orphan before=0 after=0.

## Diff review
- T06b: `FdpEventBus.GetRegisteredManagedEventTypes()` snapshots `_managedStreams` → CLR types (thread-safe;
  documented caveat: only registered/published-at-least-once managed events appear). `ListCommands` merges
  managed (`managed:true`) + unmanaged with `JsonShapeDescriber` schemas, dedup by name; `SendCommand` also
  resolves managed types. Clean, doesn't disturb the existing unmanaged path.
- P9: `FocusEntity` publishes `CenterOnEntityCommand` (marshalled); `AddAnnotation` writes sphere/anchor/line
  to an optional `DebugPrimitiveBuffer` (null-safe; editor wires `_gizmoBuffer`). `focus_entity` +
  `add_annotation` MCP tools (49 total).

## Manual-verify only (correctly disclosed, not faked)
- The actual camera centering after `focus_entity`, and the gizmo marker being visible on the map after
  `add_annotation`, require a windowed Raylib session (`DataDrivenGizmoSystem`). The headless gate covers the
  publish/buffer-write path; the visual render is noted as manual-verify in the report. Honest and correct —
  these are genuinely not headless-verifiable.

## Lesson / closeout
The agent disclosed the visual limits honestly rather than claiming them — the right call for the two
Tier-3 success conditions that are inherently visual. With this batch the tracked scope is complete: every
API group A–N has live-verified headless behavior + a 1:1 MCP tool, and the only un-automated checks are the
two genuinely-visual P9 cases.
