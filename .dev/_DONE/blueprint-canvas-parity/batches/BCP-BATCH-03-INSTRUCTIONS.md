# BCP-BATCH-03: Node pin enrichment — data/value pins for all kinds (priority over mini-editors)
User: "Many node types are missing value pins completely; NodeEdit demo has them all." Enrich `NodePinSchema` so every kind projects its real data pins (exec + data), sourced from the compiler-backed pin semantics.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/_DONE/blueprint-canvas-parity/DESIGN.md` (projection-only binds).
2. The per-kind pin requirements are fully specified in Tasks 1–3 below (self-contained), each citing the compiler source of truth (`Stage2_Validate.cs`, `Stage4_TypeResolve.cs`, `Stage5_Schedule.cs`). Visual style reference: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeNodeCatalog.cs`. Hand-authored exemplars: `Hrot.Blueprints.Editor/NodeDrawers/WhenNodePaletteEntries.cs` (When/ReadEqsResult/SpawnEqsSensor already have full data pins). Verify every pin against the cited compiler code before implementing.
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS. Headless tests gate ImGui.

## Principle
Pins stay **projection-only** (no `.bp.json`/`Pin`-schema/`BlueprintJsonServices` change; byte-stability + compiler golden must stay green). Only add pins the **compiler actually consumes** for that kind (so a wired data pin is meaningful) — the investigation confirmed these from `Stage4_TypeResolve`/`Stage5_Schedule`. Do NOT invent pins the compiler ignores.

## Task 1 (biggest win) — ChannelCommandNode data parameter pins (DYNAMIC)
ChannelCommand nodes (e.g. all of MoveToAndFire) currently show exec-only. Their parameter data-input pins come from the channel-action schema. Wire an `IActionSchemaExporter` into the pin-resolution path (thread it from `EditorSubsystem`/`BlueprintDocumentFactory` → `BlueprintGraphModel` → `NodePinSchema.GetCanonicalPins(node, registry, asset, schemaExporter)`; an `ActionSchemaExporter` is already constructed in `EditorSubsystem` for the aggregator — reuse it). For a `ChannelCommandNode`, resolve `ActionSchemaEntry` via the exporter (`Lookup($"{ChannelType}.{ActionId}")` or the channel registry — verify the exact key format against `Stage2_Validate.cs:472-481`), reflect its parameter DTO type, and emit one data-IN pin per parameter (Name + TypeId) plus exec In/Out. Falls back to exec-only when the action/schema isn't found.
**Tests (`Hrot.Blueprints.Tests`):** a ChannelCommand for a known action projects its parameter data-in pins (assert names + types) + exec In/Out; unknown action → exec-only (no throw).

## Task 2 — FunctionCallNode params + return (DYNAMIC, reflection)
For a `FunctionCallNode` with `TargetTypeId`+`MethodName` set, reflect the method: each parameter → data-IN pin (name+type), return type (if non-void) → data-OUT "Return" pin; exec In/Out only when `!IsPure`. Use a safe reflection helper (resolve the `Type` by FQN across loaded assemblies; tolerate not-found → exec-only/empty as today). Unconfigured FunctionCall (no method) → current behavior.
**Tests:** a FunctionCall to a known static method projects its param data-in pins + Return data-out (assert names/types); pure vs non-pure exec presence; unknown type/method → graceful fallback.

## Task 3 — static data pins the compiler consumes
Add to `NodePinSchema`:
- **LatentDelayNode:** + `Duration` (System.Single) data-IN (compiler reads optional Duration — Stage5 BuildLatentDelayOp). Keep exec In/Out.
- **ScoreDecisionNode:** + `WinningOptionId` (System.Byte) data-OUT. Keep exec In/Out.
- **ArrayGetNode:** `Array` (data-IN), `Index` (System.Int32 data-IN), `Element` (data-OUT) + exec In/Out. Element/Array type best-effort `System.Object` (wildcard at compile time).
- **ArrayMakeNode:** keep exec-out; project a small fixed set of element data-IN pins (e.g. "0","1") of `ElementTypeId` (or System.Object) + an `Array` data-OUT. (Dynamic element-count tracking is out of scope; a sensible default is fine — note it.)
- **CastNode / LiteralNode / Get/SetVariable:** already have data pins — leave.
**Tests:** assert the new data pins (name/dir/type) for Delay, ScoreDecision, ArrayGet, ArrayMake.

## Explicitly deferred (do NOT fake) — note in report
- **ReadRankedResultNode:** output pins come from the referenced `UtilityDecisionDef` result schema (needs the decision asset). Leave as-is; document the resolver needed (load decision → result struct fields).
- **Squad nodes** (PartitionElements/AssignRoles/AdvancePhase/AcquireSlot): by compiler design they have **no node pins** (inputs/outputs resolved from working-state vars). Leave exec-only; document.
- **Branch `Condition` data-in:** only add if the compiler actually reads a Condition data pin on `BranchNode` (verify in Stage2/Stage5). If it does not, leave In/True/False exec-only and note that the blueprint Branch sources its condition differently than the demo.

## Success Criteria
- [ ] ChannelCommand + FunctionCall show their real data pins (params/return) from the schema/reflection; Delay/ScoreDecision/ArrayGet/ArrayMake show their static data pins.
- [ ] Wire-drop/by-pin picker now matches by these data pins too (data-typed wire-drop offers compatible nodes) — verify the catalog signatures pick up the enriched pins (they flow through `DescriptorToEntry` → `NodePinSchema`).
- [ ] Byte-stability + compiler golden unchanged. Build 0 errors (note: ~26 pre-existing test-project warnings exist on full rebuild — do not add new ones in touched projects). GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006; flaky sub-80ns perf re-run isolated), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot`.
- [ ] Report at `.dev/_DONE/blueprint-canvas-parity/reports/BCP-BATCH-03-REPORT.md`.

## Execution rules
- Verify the `IActionSchemaExporter` API + the ChannelType/ActionId→schema key, and the FunctionCall reflection path, against the code BEFORE coding (cite what you find). Thread the exporter additively through `BlueprintGraphModel`/`NodePinSchema` (optional param, null-safe → current behavior).
- Run suites yourself; assert real pin names/types/directions; never fake a pass. Projection-only stays mandatory (these pins are editor projection; do not persist).
- Keep changes minimal + additive; the enriched pins must flow through the existing two-pass GUID binding (connected pins still bind from links).

## Report
Document: how ChannelCommand/FunctionCall pins are resolved (the exporter/reflection APIs + key formats); the static additions; what's deferred (ReadRankedResult, squad, Branch-condition) and why; actual test counts; build 0 errors + byte-stability + golden unchanged; suggested commit message. No comprehension questions.
