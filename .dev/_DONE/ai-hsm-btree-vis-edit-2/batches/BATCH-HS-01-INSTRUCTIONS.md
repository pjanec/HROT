# BATCH-HS-01 — Command sink: create state (+ asset state-registration API)

**Task:** TASK-HS-01. **One objective only.** Make dragging a state kind from the palette create a real `StateNode` on the HSM canvas. This requires first adding a small state/transition registration API to `HsmAsset` (the foundation HS-02/03/04 will reuse).

Design ref: TASK-DETAIL.md §TASK-HS-01; DECISIONS.md §D-HS-01 (read it — the model-API shape and FlatIndex policy are decided there, do not re-litigate).

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch only the files named below. Do NOT change move/reparent/region handlers, BTree code, or other workstreams' files.
2. **No cheating to pass build/tests.** Never exclude assets from compilation, comment out code, suppress diagnostics, weaken assertions, or stub to dodge an error. If blocked, STOP and write the blocker in the report.
3. **Finish without asking.** Build + run the named test project, diagnose, fix, repeat until `Failed: 0`, then write the report. No permission-asking.
4. **Headless only.** Verify via build + unit tests. No editor/pixel work.
5. **Tests verify behavior, not strings.** Assert actual values/enums/flags/counts, not generated text.
6. **Litter-free.** No scratch files, no `Console.WriteLine`, no debug `File.WriteAllText`. Leave the tree clean.
7. **Report = truth.** The report must match the diffs.

## Files
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — add registration API.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs` — implement `ApplyAddNode` (currently `{ /* TODO */ }` at ~line 139).
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmKinds.cs` — kind constants (read; mapping helper may live on `StateNode.Kind`).
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/` (add a new test file, e.g. `HsmCommandSinkCreateStateTests.cs`; reuse the `BuildTestAsset()` pattern from `HsmCommandSinkRegionTests.cs`).

## Part 1 — HsmAsset state/transition registration API

**Current reality:** the ctor receives `List<StateNode> allStates` / `List<TransitionNode> allTransitions`, exposes them as `AllStates`/`AllTransitions` via `.AsReadOnly()` (which wraps the SAME list — so mutating the original list is visible through the read-only view), but does NOT retain the list references. Only `_allRegionsList`/`_allGlobalTransitionsList` are kept. Identity maps `_stableIdToState`, `_visualIdToTransition`, `_flatIndexToState`, `_flatIndexToTransition` are private.

**Add:**
1. Two private fields capturing the backing lists, assigned in the ctor:
   ```csharp
   private readonly List<StateNode>      _allStatesList;
   private readonly List<TransitionNode> _allTransitionsList;
   ```
   In the ctor set `_allStatesList = allStates; _allTransitionsList = allTransitions;` (alongside the existing `_allRegionsList = allRegions;`). Because `AllStates`/`AllTransitions` already wrap these exact lists, mutating them updates the public views automatically.

2. Internal mutators (place them near `RegisterRegion`/`UnregisterRegion`):
   ```csharp
   // Registers a newly-created (editor-authored) state under `parent`. Updates the
   // backing list + identity maps. FlatIndex is assigned to the next free value so the
   // flat-index map stays collision-free in-session; it is authoritatively re-derived
   // from the blob on save->reload.
   internal void RegisterState(StateNode state, StateNode parent)
   {
       state.Parent = parent;
       if (!parent.Children.Contains(state)) parent.Children.Add(state);
       if (state.FlatIndex == 0 || _flatIndexToState.ContainsKey(state.FlatIndex))
           state.FlatIndex = NextFreeStateFlatIndex();
       _allStatesList.Add(state);
       _stableIdToState[state.StableId] = state;
       _flatIndexToState[state.FlatIndex] = state;
   }

   internal void UnregisterState(StateNode state)
   {
       state.Parent?.Children.Remove(state);
       _allStatesList.Remove(state);
       _stableIdToState.Remove(state.StableId);
       _flatIndexToState.Remove(state.FlatIndex);
   }

   internal void RegisterTransition(TransitionNode t)
   {
       if (t.FlatIndex == 0 || _flatIndexToTransition.ContainsKey(t.FlatIndex))
           t.FlatIndex = NextFreeTransitionFlatIndex();
       if (!t.Source.OutgoingTransitions.Contains(t)) t.Source.OutgoingTransitions.Add(t);
       _allTransitionsList.Add(t);
       _visualIdToTransition[t.VisualId] = t;
       _flatIndexToTransition[t.FlatIndex] = t;
   }

   internal void UnregisterTransition(Guid visualId)
   {
       if (!_visualIdToTransition.TryGetValue(visualId, out var t)) return;
       t.Source?.OutgoingTransitions.Remove(t);
       _allTransitionsList.Remove(t);
       _visualIdToTransition.Remove(visualId);
       _flatIndexToTransition.Remove(t.FlatIndex);
   }
   ```
   Plus private helpers `NextFreeStateFlatIndex()` / `NextFreeTransitionFlatIndex()` returning `(ushort)((max existing FlatIndex across the map) + 1)`, starting at 1 if empty. (FlatIndex 0 is treated as "unassigned" for editor-new nodes.)

   > NOTE: `RegisterTransition`/`UnregisterTransition` are added now because they are trivial siblings and HS-03/HS-04 need them; **do not** call them from any sink handler in THIS batch — that is HS-03/04. Only `ApplyAddNode` (using `RegisterState`) is wired here.

## Part 2 — HsmCommandSink.ApplyAddNode

Replace the `{ /* TODO */ }` body. Behavior:
- Map `cmd.Kind.Id` (a `NodeKindKey`) to state flags using `HsmKinds` constants:
  - `Simple` / `Composite` → no pseudo/parallel flags (a freshly-created Composite has no children yet; `StateNode.Kind` will report it as Simple until a child is reparented in — that is correct and intended per D-HS-01).
  - `Parallel` → `IsParallel = true`
  - `Final` → `IsFinal = true`
  - `History` → `IsHistory = true`
  - `DeepHistory` → `IsDeepHistory = true`
  - Unknown kind id → create a Simple state (no flags).
- Create `new StateNode(name)` where `name` is a readable default derived from the kind (e.g. `"State"`, `"Parallel"`, `"Final"`, `"History"`, `"DeepHistory"`); set `StableId = cmd.AssignedId.Value`, `Position = cmd.Position`.
- Register under root: `_asset.RegisterState(state, _asset.RootState);`
- (No explicit promote-to-composite code — that is automatic via `StateNode.Kind`/`IsContainer` + the existing reparent handler, per D-HS-01.)
- The trailing `_asset.MarkDirty()` in `Apply(...)` already fires; do not add another.

Confirm exact field names by reading the `GraphCommand.AddNode` record (`AssignedId`, `Kind`, `Position`, `InitialProperties`). Ignore `InitialProperties` for this task.

## Tests (`Hrot.Hsm.Editor.Tests`, new file)
Reuse a `BuildTestAsset`-style helper (root + maybe one state) — but build an asset you can add to. Assert real values:
1. `AddNode(Simple)` → `asset.AllStates` count +1; `FindStateByStableId(assignedId)` resolves; its `Parent == RootState`; `Kind.Id == HsmKinds.Simple`.
2. `AddNode(Parallel)` → created state `IsParallel == true`; `Kind.Id == HsmKinds.Parallel`.
3. `AddNode(Final)` → `IsFinal == true`.
4. `AddNode(History)` → `IsHistory == true`; `AddNode(DeepHistory)` → `IsDeepHistory == true`.
5. **Implicit promotion:** create a Simple state S1, create another state S2, reparent S2 under S1 via `GraphCommand.ChangeParent` (or `ChangeParentMultiple`), then assert `S1.IsContainer == true` and `S1.Kind.Id == HsmKinds.Composite`.
6. FlatIndex uniqueness: create two states → their `FlatIndex` values differ and both resolve via `FindStateByFlatIndex`.

## Verification (run WITHOUT any regenerate env var)
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. If there are pre-existing failures, list them and confirm your change adds none.

## Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-01-REPORT.md`
State: the exact API added to HsmAsset; the ApplyAddNode mapping; test names + what each asserts; build/test counts (before/after); any pre-existing failures; anything you could NOT do. Do not commit.
