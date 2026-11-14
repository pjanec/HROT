# BATCH-10: Typed WorkingState in the Entity Inspector (Feature A) + manifest Type (PREREQ)
**Tasks:** PREREQ (StatefulSlotInfo.WorkingStateType), Feature-A (tier renderers)   **Phase:** Slice 2 polish   **Est:** ~8h
**Dependencies:** Slice 2 complete (BATCH-06/08).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/_DONE/btree-ai-action-binding/LIVE-VALUE-DISPLAY-DESIGN.md` — the design (PREREQ + Feature A; Feature B is NOT in this batch).
3. Codebase-memory MCP first.

## Verified current-code facts (dev-lead-grounded — exact paths)
- **Manifest:** `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs` — `public sealed record StatefulSlotInfo(int SlotKey, int PayloadSize, uint StructureHash);` and `BehaviorDefinition.StatefulWorkingSlots : IReadOnlyList<StatefulSlotInfo>?`. `ManagedBlackboardVariable(string Name, Type Type, int ByteOffset)` is the precedent that carries a `Type`.
- **Manifest emit:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` → `EmitStatefulWorkingSlotsArray` (~lines 501-546) emits `new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({slotKey}, Marshal.SizeOf<{wsTypeFqn}>(), unchecked({hash}u ^ ...))`. It already has `wsTypeFqn` (a `global::`-qualified WorkingState type) and the node (`actNode` → `DisplayLabel`/`VisualId`).
- **Reference renderer (mirror this):** `Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs` — `RenderValue(IInspectableSession, Entity, object, out string?)`: resolves active behavior via `BehaviorState.ActiveBehaviorHash` + `BehaviorRegistryAccessor`, then for each managed var `RenderTypedDtoAtOffset(bb, v.Type, v.ByteOffset, ...)` which does `Marshal.PtrToStructure((IntPtr)(ptr+offset), dtoType)` → `ImGuiPropertyTree.Render(boxed, contextType: dtoType, out path)`.
- **Renderers to extend:** `Hrot/Engine/Hrot.Presentation/Renderers/BlueprintBlackboard{1024,4096,16384}Renderer.cs` — each `[ImGuiRenderer(typeof(BlueprintBlackboardN))]`, `IEntityAwareImGuiRenderer`, currently render `BlueprintTierSummary.Read(mem, registry)` as a 4-col table (Blueprint/Version/Size/Id). Each has a `public static BlueprintRegistry? BlueprintRegistryAccessor`.
- **Partition read API:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs` — `GetSlotCount(byte*)`, `GetSlot(byte*, i) → ref BlueprintSlotEntry`, `TryGetSlotOffset(byte*, int slotKey, out int payloadOffset)`. `BlueprintSlotEntry { int BlueprintId; uint InstanceVersion; ushort PayloadOffset; ushort PayloadSize; uint StructureHash; }`. Header magic check via `BlueprintBlackboardHeader.MagicValue`.
- **Renderer infra:** `[ImGuiRenderer(typeof(T))]` (`FDP/Engine/Fdp.Presentation/ImGui/Renderers/ImGuiRendererAttribute.cs`); `IEntityAwareImGuiRenderer.RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)` (`FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs`); `IInspectableSession.HasComponent/GetComponent` (`…/Abstractions/IInspectableSession.cs`); `ImGuiPropertyTree.Render(object?, Type? contextType, out string? doubleClickedPath)` (`…/Utils/ImGuiPropertyTree.cs`).
- **Startup wiring:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (~lines 626-637) sets the static `*Accessor` on each renderer. The 3 BlueprintBlackboard renderers already get `BlueprintRegistryAccessor`; they will additionally need a `BehaviorRegistry` accessor (add + wire).

## Task 1: PREREQ — add WorkingStateType (+ NodeLabel) to the manifest
**Files:** `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs`; `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs`.
**Scope:**
- Extend the record: `public sealed record StatefulSlotInfo(int SlotKey, int PayloadSize, uint StructureHash, Type? WorkingStateType = null, string? NodeLabel = null);` — **optional trailing params (default null)** so existing 3-arg constructions (BATCH-06/08 tests) compile unchanged.
- `EmitStatefulWorkingSlotsArray`: emit the two new args — `typeof(global::{wsTypeFqn})` and the node's `DisplayLabel` string literal (or the VisualId string if no label). Keep the dedupe-by-SlotKey behavior.
**Tests:** extend the existing emitter test (`Hrot.AiEditor.Generators.Tests` StatefulSlotKeyTests) to assert the emitted `StatefulWorkingSlots` entry now contains `typeof(` of the WorkingState type and the node label. Keep all current assertions.

## Task 2: Feature A — typed WorkingState section in the tier renderers
**Files:** the 3 `BlueprintBlackboard{1024,4096,16384}Renderer.cs`; a NEW shared helper (e.g. `Hrot/Engine/Hrot.Presentation/Renderers/StatefulWorkingStateProjection.cs`) to avoid triplicating logic; `EditorSubsystem.cs` (wire the new accessor).
**Scope:**
- Add `public static BehaviorRegistry? BehaviorRegistryAccessor` to the shared helper (or to each renderer; prefer the helper) and wire it in `EditorSubsystem` next to the existing `BlueprintRegistryAccessor` assignments.
- Shared helper `RenderWorkingState(IInspectableSession session, Entity entity, byte* memory)`:
  1. Resolve `BehaviorState.ActiveBehaviorHash` via `session.GetComponent`; `BehaviorRegistryAccessor.TryGetDefinition(hash, out def)`; if no `def.StatefulWorkingSlots`, render nothing (return).
  2. Header: `ImGui.TextDisabled("Working state (BTree)")` (only if ≥1 slot resolves).
  3. For each `StatefulSlotInfo s` with `s.WorkingStateType != null`: `if (!TryGetSlotOffset(memory, s.SlotKey, out int off)) continue;` (slot may belong to a different tier / not attached); `object boxed = Marshal.PtrToStructure((IntPtr)(memory + off), s.WorkingStateType);` render a tree node labelled `s.NodeLabel ?? $"slot 0x{s.SlotKey:X8}"` then `ImGuiPropertyTree.Render(boxed, contextType: s.WorkingStateType, out _)`. Read-only (the inspector session is read-only).
  4. Be robust: skip a slot on any projection failure (don't throw inside the renderer); a missing/zero-size slot just renders nothing.
- Each tier renderer calls the helper after its existing `BlueprintTierSummary` table, passing its `bb.Memory` pointer. Do NOT remove the existing summary table.
**Tests:** `Hrot/Engine/Hrot.Presentation.Tests/Behavior/` (mirror `BrainBlackboardRendererTests`): a headless test that builds a `BlueprintBlackboard1024`, `Initialize` + `TryAttach` a slot for a known `DemoCursorState`-shaped type, writes a known Cursor value into the payload, registers a `BehaviorDefinition` with a matching `StatefulWorkingSlots` entry (with `WorkingStateType`), and asserts the projection logic decodes the correct `Cursor` value (test the helper's decode path directly — e.g. a `TryProjectSlot(...) → object` seam returning the boxed struct — rather than asserting ImGui pixels). If `BrainBlackboardRendererTests` uses a fake `IInspectableSession`, mirror it.

## Global rules
- `dotnet build-server shutdown` before codegen verification (Task 1 touches the emitter).
- Byte-identity gate `Hrot.AiEditor.Persistence.Tests` 129/0 (no current asset uses the stateful shape except T20 which is not in the golden set; confirm). Behavior tests via `--filter Behavior` (full suite flaky — DEBT-AIB-030). Build `Hrot.Presentation` + `Hrot.Editor` + run `Hrot.Presentation.Tests` and `Hrot.AiEditor.Generators.Tests` green (2 MigrationEquivalence known).
- Editor app is NOT hot-reloaded — note in the report that a rebuild+restart is needed to see it live.
- Never weaken a test. Fail loud. Do NOT commit. Only stop on a genuine design contradiction (write at top of report).

## Success Criteria
- [ ] PREREQ: `StatefulSlotInfo` carries `WorkingStateType`+`NodeLabel`; emitter emits them; T20 still builds + proof tests pass; emitter test asserts the new args.
- [ ] Feature A: the 3 tier renderers show a typed "Working state" section; shared helper decode path unit-tested with a real value assertion.
- [ ] Clean rebuild 0 errors; byte-identity 129/0; touched-suite green.
- [ ] Report at `.dev/_DONE/btree-ai-action-binding/reports/BATCH-10-REPORT.md`.

## Report Requirements
Answer: how the manifest emit changed; the shared-helper seam you exposed for testing; how you handled a slot whose type fails to project; confirm T20 proof tests still green; rebuild+restart note for live viewing; any deviation; suggested commit message. Do NOT ask comprehension questions.
