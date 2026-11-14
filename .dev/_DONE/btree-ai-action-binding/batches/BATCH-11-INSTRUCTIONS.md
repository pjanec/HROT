# BATCH-11: Live value column in the Blackboard variable window (Feature B — selected-entity MVP)
**Tasks:** Feature B (MVP)   **Phase:** Slice 2 polish   **Est:** ~10h
**Dependencies:** Feature A (BATCH-10) committed + seen live by user.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/_DONE/btree-ai-action-binding/LIVE-VALUE-DISPLAY-DESIGN.md` — Feature B section + the LOCKED decisions (selected-entity scope; do NOT build the "any entity" / canvas-overlay tier).
3. Codebase-memory MCP first.

## Scope (read carefully — what "value" means here)
The Blackboard variable window lists the asset's **authored blackboard variables** (the per-variable Params DTOs projected over `BrainBlackboard`, e.g. T20's `cursorA`/`cursorB` of type `DemoCursorParams`, `counter` of type `DemoCounterParams`). The new **"Value" column shows each variable's LIVE value** read from the selected entity's `BrainBlackboard`, **only when the selected entity is currently running this asset's behavior**. 
- **NOT in scope:** the per-node WorkingState slots (those are not authored variables — they're shown in the Entity Inspector via BATCH-10). This feature reads only `BrainBlackboard` params (the `ManagedBlackboardVariable` offsets).
- **Selected-entity scope only** (user-confirmed). No "all entities running this asset", no canvas overlay, no new AssetId→BehaviorId map, no IInspectableSession entity-iteration.

## Verified current-code facts (dev-lead-grounded — exact paths)
- **Window:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`. Ctor (~lines 115-125) takes `EditorSelectionStore store`, `IRefactorService`, optional comparison services, `IActionSchemaExporter?`. `DrawClientArea()` (~lines 353-474) reads `_store.ActiveAsset` (an `IEditableAsset`, has `.Name` + `.AssetId`), builds the VM via `BuildViewModel(...)` (pure — keep it pure), then instantiates `VariablesPanelControl` (~line 462) and calls `DrawSingle(section, rowDec)` (~line 474). Registered in `Hrot/Editor/Hrot.Editor.AiShared/SharedAiEditorServiceCollectionExtensions.cs` (~lines 78-84).
- **View-model:** `VariableViewModel` record (BlackboardAuthoringWindow.cs ~lines 18-27): `(string Name, string TypeName, int ByteSize, Type FieldType, string? Comment, …)`. **Do NOT add a runtime field to this record** — keep it design-time. Pass live values as a separate map.
- **Control:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`. `DrawTable` (~lines 249-407): `ImGui.BeginTable(section.TableId, 4, …)` with columns Name/Type/Bytes/##rmv; per-row renders `row.TypeName` then `row.ByteSize` (~lines 373-375). This is where a 5th "Value" column goes.
- **Selection:** `Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs` — `Entity? SelectedEntity` (~lines 61-70) + `event Action? OnSelectionChanged`. Already injected into the window via `store`.
- **Live session + registry (both live in `Hrot.Editor`, NOT directly injectable today):** `EditorSubsystem.cs` owns `_world` (`EntityRepository`, ~line 184), wraps it as `_fdpRepoAdapter` (an `IInspectableSession`, ~line 1387), and owns `_behaviorRegistry` (`BehaviorRegistry`, ~lines 616-617). The Entity Inspector gets its session via a per-frame `Func<IInspectableSession?>` factory (`FdpEntityInspectorWindow.cs:37-43`, wired in `EditorSubsystem` ~line 3388-3393). Reuse that same `_fdpRepoAdapter` source.
- **Read idiom (proven):** `Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs` — `session.GetComponent(entity, typeof(BrainBlackboard))`; for each `ManagedBlackboardVariable (Name, Type, ByteOffset)` from `BehaviorDefinition.ManagedBlackboardVariables`, `Marshal.PtrToStructure((IntPtr)(ptr+ByteOffset), Type)`. `BehaviorRegistry.TryGetId(name, out id)` + `TryGetDefinition(id, out def)`.

## Task 1: `ILiveBlackboardValueProvider` seam + impl
**Files:**
- NEW interface `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ILiveBlackboardValueProvider.cs`
- NEW impl `Hrot/Editor/Hrot.Editor/.../LiveBlackboardValueProvider.cs` (in `Hrot.Editor`, where the live world + registry live)
- DI registration (`Hrot.Editor` startup / where `_fdpRepoAdapter` + `_behaviorRegistry` are available) + `SharedAiEditorServiceCollectionExtensions.cs` (window ctor gets the optional dependency)

**Interface:**
```csharp
public interface ILiveBlackboardValueProvider
{
    // Returns variableName -> formatted live value, for the asset's authored variables,
    // ONLY when an entity is selected AND it is currently running this asset's behavior.
    // Empty map otherwise (no selection / behavior mismatch / no live world).
    IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset);
}
```

**Impl logic (`LiveBlackboardValueProvider`):** ctor takes the live-session source (a `Func<IInspectableSession?>` over `_fdpRepoAdapter`), a `Func<BehaviorRegistry?>` (or the instance), and `EditorSelectionStore`.
1. `var entity = _store.SelectedEntity; if (entity == null) return Empty;`
2. `session = _sessionFactory(); if (session == null || !session.IsAlive(entity) ) return Empty;`
3. `if (!session.HasComponent(entity, typeof(BehaviorState))) return Empty;` read `BehaviorState.ActiveBehaviorHash`.
4. **Name-match gate:** `if (!registry.TryGetId(asset.Name, out int id) || id != activeBehaviorHash) return Empty;` (selected entity is NOT running this asset → no live values).
5. `registry.TryGetDefinition(id, out def)`; read `bb = (BrainBlackboard)session.GetComponent(entity, typeof(BrainBlackboard))`.
6. For each `ManagedBlackboardVariable v` in `def.ManagedBlackboardVariables`: project `Marshal.PtrToStructure((IntPtr)(bbPtr + v.ByteOffset), v.Type)` and format to a compact one-line string (e.g. for a multi-field struct: `"Counter=7, Threshold=1000"` — reflect public fields/props; for a primitive: its `ToString()`). Key the result by `v.Name`. Wrap each projection in try/catch — skip a variable on failure (never throw).
7. Return the map.
**Robustness:** all failures → omit that entry / return Empty; the provider must never throw into the UI.

**Tests required** (`Hrot.Editor.AiShared.Tests` or `Hrot.Editor.Tests` — whichever can reference the impl; use a FAKE `IInspectableSession` returning a known `BrainBlackboard` + `BehaviorState`):
- `LiveValues_SelectedEntityRunningAsset_ReturnsFormattedValues` — fake session: selected entity has `BehaviorState{ActiveBehaviorHash=id}` + a `BrainBlackboard` with a known DTO at a known offset; a registry with that id → def carrying `ManagedBlackboardVariables`; assert the map contains the variable name → the correctly formatted live value (real value assertion, e.g. counter shows `Counter=7`).
- `LiveValues_NoSelection_ReturnsEmpty`.
- `LiveValues_SelectedEntityRunningDifferentBehavior_ReturnsEmpty` (name-match gate: `ActiveBehaviorHash != TryGetId(asset.Name)`).
- `LiveValues_ProjectionFailure_OmitsVariable_DoesNotThrow`.

## Task 2: wire into the window + Value column
**Files:** `BlackboardAuthoringWindow.cs`, `VariablesPanelControl.cs`, `SharedAiEditorServiceCollectionExtensions.cs`.
**Scope:**
- Add an **optional** `ILiveBlackboardValueProvider? liveValueProvider = null` to the window ctor (default null → keeps existing registration/tests compiling; null means no Value column / all "—"). Register the impl in DI and pass it where the window is constructed.
- In `DrawClientArea`, when `_store.ActiveAsset` is set and the provider is non-null, call `liveValueProvider.GetLiveVariableValues(_store.ActiveAsset)` ONCE per frame; pass the resulting `IReadOnlyDictionary<string,string>?` into `VariablesPanelControl` (new optional param on the draw method, default null).
- `VariablesPanelControl.DrawTable`: bump the table to **5 columns**, add `ImGui.TableSetupColumn("Value", WidthStretch)` after "Bytes"; in the row loop render `liveValues != null && liveValues.TryGetValue(row.Name, out var val) ? val : "—"` (TextDisabled for "—"). Keep all existing columns/behavior. When `liveValues` is null (no provider / design-time), the column still renders but shows "—" for every row (or omit the column entirely if null — your choice; document it).

**Tests:** if `VariablesPanelControl`/window are ImGui-only (not headlessly testable), the provider unit tests (Task 1) carry the logic coverage — state that in the report. Do not write hollow string-presence tests for the ImGui layer.

## Global rules
- Editor-only; no codegen, no runtime, no byte-identity impact. Do NOT touch persistence/generator/runtime assemblies (the BATCH-10 manifest already provides everything needed; this batch only READS via `ManagedBlackboardVariables`).
- Build `Hrot.Editor.AiShared` + `Hrot.Editor` + their test projects; run the touched test suites green (0 net-new failures). Note any pre-existing unrelated failures, don't fix them.
- Editor not hot-reloaded — note rebuild+restart to view live.
- Never weaken a test. Provider must never throw into the UI. Do NOT commit. Work autonomously; only stop on a genuine design contradiction (write atop the report).

## Success Criteria
- [ ] `ILiveBlackboardValueProvider` + impl with the name-match gate + robust formatting; 4 provider unit tests pass (incl. the real-value assertion + the mismatch/empty/throw-safety cases).
- [ ] Window shows a "Value" column populated for the selected entity when it runs the asset, "—" otherwise; optional injection keeps existing ctor usages compiling.
- [ ] Touched suites green; clean build 0 errors.
- [ ] Report at `.dev/_DONE/btree-ai-action-binding/reports/BATCH-11-REPORT.md`.

## Report Requirements
Answer: the exact DI wiring you used to get the live session + registry into the impl; how you formatted multi-field DTO values; the null-provider column behavior you chose; whether the ImGui layer is headlessly testable (and what the provider tests cover instead); rebuild+restart note; any deviation; suggested commit message. Do NOT ask comprehension questions.
