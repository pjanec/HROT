# FIX1-BATCH-02 — Phase 1: Shared Infrastructure Foundation

## Tasks Covered
- **TASK-S1-05** — `ReferenceCatalog` must subscribe to `IAssetCatalog.Changed` and trigger automatic multi-index rebuild.
- **TASK-S1-08** — `IGSelectionBridge` / `CallbackSelectionBridge` must be wired in the editor to read `SelectionChangedEvent` from `FdpEventBus` and sync selected entity.
- **TASK-S1-11, TASK-S1-12** — `DebugSessionRegistry.TryAcquireSession` must enforce exclusivity: only one active control session at a time.

> **Note:** TASK-S1-03 (`EditorSelectionStore` per-asset sub-selection) is already correctly implemented per design review and does NOT require changes.

## Onboarding

You are working on the `IOS-IG-SimHost-FDP-2` project. This batch fixes Phase 1 shared infrastructure gaps.
The shared infrastructure layer (`Hrot.Editor.AiShared`) is the unified substrate for the BTree, HSM, and Blueprint editors.

**Mandatory read before coding:**
- `.dev/blueprints-2/FIX1-TASK-DETAIL.md` — "ACTION PACKET 2: Phase 1 — Shared Infrastructure Foundation" section.
- `.dev/blueprints-2/FIX1-TASK-EXTRA-DETAILS.md` — "ACTION PACKET: Phase 1 — Shared Infrastructure (Remaining Fixes)" section for detailed step-by-step instructions.
- `.dev/blueprints-2/AI_Editor_Shared_Infrastructure.md` — §4.3 (ReferenceCatalog), §11.1 (debug session cardinality).
- `.dev/blueprints-2/ACCEPTANCE-CRITERIA.md` — Criteria F1-05, F1-09, F1-10.
- `Hrot/Editor/Hrot.Editor.AiShared/` — Existing shared infrastructure code.

## Developer Insights Section

After implementing, answer the following in your report:
1. What issues were encountered during implementation (unexpected dependencies, missing interfaces, etc.)?
2. What weak points did you spot in the existing codebase?
3. What design decisions were made beyond the spec?

---

## Tasks

### TASK-S1-05: `ReferenceCatalog` Rebuild Trigger

**Target files:**
- `Hrot/Editor/Hrot.Editor.AiShared/References/ReferenceCatalog.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-S1-05: `ReferenceCatalog` Rebuild Trigger".

**Summary:**
1. Update the `ReferenceCatalog` constructor to accept both `IAssetCatalog catalog` and `IEnumerable<IReferenceCatalogContributor> contributors`. Save to private fields.
2. Subscribe to `catalog.Changed` in the constructor.
3. Implement `OnCatalogChanged()` to clear `_elements` and `_references`, then enumerate all `_catalog.All` assets, call `contributor.EnumerateElements(asset)` and `contributor.EnumerateReferences(asset)` for each contributor, and fire `Changed?.Invoke()` at the end.
4. If `IReferenceCatalogContributor` does not yet exist as an interface, define it with:
   ```csharp
   public interface IReferenceCatalogContributor
   {
       IEnumerable<KeyValuePair<AssetElementKey, IAssetSubElement>> EnumerateElements(IEditableAsset asset);
       IEnumerable<IAssetReference> EnumerateReferences(IEditableAsset asset);
   }
   ```
   Adjust to match existing types in the codebase.

**Acceptance criteria:** F1-09.

**Tests required:**
- Add a test that:
  1. Creates a mock `IAssetCatalog` with a `Changed` event.
  2. Registers a `MockReferenceCatalogContributor` that returns 2 elements and 1 reference for a mock asset.
  3. Triggers `Changed` on the catalog.
  4. Asserts `referenceCatalog.Elements.Count == 2` (or however elements are queried).
  5. Triggers the catalog changed event again with an empty mock asset set.
  6. Asserts elements are cleared and count == 0.

---

### TASK-S1-08: Engine-to-Editor Selection Sync (`IGSelectionBridge`)

**Target files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Selection/IGSelectionBridge.cs` (or `CallbackSelectionBridge.cs`)
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (or your DI composition root / editor init)

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-S1-08: Engine-to-Editor Selection Sync (FdpEventBus)".

**Summary:**
1. Find where the editor is initialized (the editor subsystem or DI root).
2. Instantiate the `CallbackSelectionBridge` with a factory lambda that subscribes to `SelectionChangedEventDto` (or `SelectionChangedEvent`) on `_world.Bus` (the `FdpEventBus`).
3. Inside the subscription callback:
   - Extract the first entity ID from `evt.SelectedEntityIds`.
   - Map via `_entityMap.TryGetEntity(netId, out var entity)` (or equivalent).
   - Call `onEditorSelectionSet(entity)` or `onEditorSelectionSet(null)` if empty/not found.
4. Call `selectionBridge.Connect(_selectionStore)` to wire it.
5. Ensure the subscription is properly disposed when the editor subsystem shuts down.

**Note:** If the exact event type is `SelectionChangedEvent` rather than `SelectionChangedEventDto`, use whatever type matches the actual codebase. Adjust the entity ID extraction accordingly.

**Acceptance criteria:** F1-10.

**Tests required:**
- Add a test (integration or unit with mocks) that:
  1. Sets up a `CallbackSelectionBridge` with a mock bus that produces a `SelectionChangedEventDto` with entity ID = 42.
  2. Connects the bridge to a mock `EditorSelectionStore`.
  3. Fires the event.
  4. Asserts `selectionStore.SelectedEntity` equals entity 42 (or the mapped entity).
  5. Fires an event with empty IDs; asserts `SelectedEntity` is null/default.

---

### TASK-S1-11 & TASK-S1-12: `DebugSessionRegistry` Session Exclusivity

**Target files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Debug/DebugSessionRegistry.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-S1-11 & TASK-S1-12: `DebugSessionRegistry` Exclusivity".

**Summary:**
1. Add a private field: `private IAiDebugSession? _activeControlSession;`
2. Add a lock object: `private readonly object _lock = new();`
3. Implement `TryAcquireSession<T>(out T? session) where T : class, IAiDebugSession`:
   ```csharp
   lock (_lock)
   {
       if (_activeControlSession != null) { session = null; return false; }
       session = _serviceProvider.GetRequiredService<T>();
       _activeControlSession = session;
       return true;
   }
   ```
4. Implement `ReleaseSession(IAiDebugSession session)`:
   ```csharp
   lock (_lock) { if (_activeControlSession == session) _activeControlSession = null; }
   ```
5. Verify that observer sessions (obtained via a separate method, e.g., `GetObserver<T>()`) are NOT blocked by the exclusive lock — the exclusivity applies only to control sessions.

**Acceptance criteria:** F1-05.

**Tests required:**
- Add tests that:
  1. `TryAcquireSession<T>` returns `true` when no control session is active.
  2. A second `TryAcquireSession<T>` call while the first is active returns `false`.
  3. After `ReleaseSession(firstSession)`, a new `TryAcquireSession<T>` succeeds again.
  4. Observer sessions (via `GetObserver<T>()` or equivalent) are not blocked regardless of active control session state.

---

## Mandatory Workflow: Test-Driven Task Progression

For every task:
1. **Read** the spec and acceptance criteria first.
2. **Write or update the test** before or alongside the implementation.
3. **Implement** the feature/fix.
4. **Run the tests** and confirm they pass.
5. **Do not mark a task complete** unless its tests pass.

Do not swallow exceptions silently. Let failures surface loudly.

---

## Build & Test Commands

```powershell
# Build and test the shared editor project
cd Hrot/Editor/Hrot.Editor.AiShared
dotnet build
dotnet test

# Or run from solution root
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build Hrot/
dotnet test Hrot/
```

---

## Report Format

Write your report to: `.dev/blueprints-2/reports/FIX1-BATCH-02-REPORT.md`

**Required sections:**
1. **Summary** — What was implemented.
2. **Task Status** — Per-task: Implemented / Partial / Blocked.
3. **Tests** — List of test methods added, and pass/fail status.
4. **Developer Insights**:
   - Issues encountered
   - Weak points spotted
   - Design decisions beyond spec
5. **Build Output** — Paste relevant `dotnet build` / `dotnet test` output (last 30 lines minimum).
