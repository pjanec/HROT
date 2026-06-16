# Live blackboard/WorkingState value display — design note (2026-06-16)

Two related asks after Slice 2:
- **Feature A** — show the decoded **WorkingState** (Slice-2 per-node stateful slots, e.g. `DemoCursorState.Cursor`) in the **Entity Inspector**, where today the `BlueprintBlackboard*` component only shows a slot *summary*.
- **Feature B** — show each blackboard variable's **current value** in the **Blackboard variable (authoring) window** — the most intuitive place to look.

## Shared prerequisite (PREREQ) — the runtime-Type gap
Decoding any slot into typed fields needs the managed `Type`. `BrainBlackboardRenderer` already does this for `BrainBlackboard` because `ManagedBlackboardVariable(Name, Type, ByteOffset)` carries `Type`. But the Slice-2 stateful manifest does **not**:
```
record StatefulSlotInfo(int SlotKey, int PayloadSize, uint StructureHash);   // no Type!
```
The WorkingState `Type` is known at codegen (`WorkingStateTypeId` FQN) but dropped after emission.

**PREREQ fix (additive, back-compatible):** add `Type? WorkingStateType` (and optional `string? NodeLabel` for a friendly row name) to `StatefulSlotInfo`; have `BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray` emit `typeof(global::…WorkingState)` (and the node's DisplayLabel). Defaulted to `null` so all existing 3-arg constructions (BATCH-06/08 tests) keep compiling. This unblocks both features.

## Reusable read idiom (proven — `BrainBlackboardRenderer`)
For an entity + its active `BehaviorDefinition`:
- **Params (stateless / Slice-1):** for each `ManagedBlackboardVariable`, `Marshal.PtrToStructure((IntPtr)(bb.BehaviorParameters + ByteOffset), v.Type)` → `ImGuiPropertyTree.Render`.
- **WorkingState (Slice-2):** for each `StatefulSlotInfo`, `TryGetSlotOffset(memory, SlotKey, out off)` on whichever `BlueprintBlackboard*` tier the entity carries, then `Marshal.PtrToStructure((IntPtr)(memory + off), WorkingStateType)` → `ImGuiPropertyTree.Render`. Active behavior via `BehaviorState.ActiveBehaviorHash` + `BehaviorRegistry`.

---

## Feature A — typed WorkingState in the Entity Inspector  (LOW effort → BATCH-10)
Extend the `BlueprintBlackboard{1024,4096,16384}Renderer` (entity-aware ImGui renderers, registered via `[ImGuiRenderer(typeof(T))]`) so that, after the existing slot-summary table, they add a **"Working state (BTree)"** section: resolve the entity's active behavior, and for each `StatefulWorkingSlots` entry whose `SlotKey` is attached in this tier, project the typed `WorkingState` and render it read-only. Needs a `BehaviorRegistryAccessor` static (same wiring as `BrainBlackboardRenderer`, set in `EditorSubsystem`). Self-contained, mirrors an existing precedent. **This is what's spec'd in BATCH-10 below (with the PREREQ).**

## Feature B — live value column in the Blackboard variable window  (recommendation + decision)
The intuitive place, but the window is design-time and the asset ≠ the running entity. Honest tiers:

- **MVP (recommended) — "live value for the selected entity, when it runs this asset."** Inject a runtime-read seam into `BlackboardAuthoringWindow`; read `EditorSelectionStore.SelectedEntity` (already global, fires `OnSelectionChanged`); gate on **name match** — `BehaviorRegistry.TryGetId(asset.Name, out id)` and `selectedEntity.BehaviorState.ActiveBehaviorHash == id` (sidesteps the missing `AssetId→BehaviorId` map). Add an optional `LiveValue` field to `VariableViewModel` + a 5th "Value" column in `VariablesPanelControl` (shows "—" when no matching selected entity). Reads via the idiom above (Params from `BrainBlackboard`; WorkingState from the partition slot using PREREQ's Type). **Feasible, ~1–1.5 days, additive.** Seams: (1) DI a read capability (an `IInspectableSession` provider or a small `ILiveBlackboardReader`), (2) the new VM field + column.
- **Deferred (HARD) — "all entities running this asset" / live overlay on the node canvas.** Blocked by missing infra: `BehaviorRegistry` has no `AssetId` field and `IInspectableSession` exposes no entity iteration. Would need an `AssetId→BehaviorId` registry + a queryable session. Not worth it for v1.

**DECISIONS LOCKED (user, 2026-06-16):**
- Feature B is a **separate batch (BATCH-11), authored only after Feature A has been seen live** — not folded into BATCH-10.
- Feature B scope = **selected-entity MVP, confirmed** ("selected entity is exactly what I expect"). The harder "any entity running this behavior" / canvas-overlay tier is **dropped** for now (do not build the AssetId→BehaviorId map / session iteration).
- **Do not build anything yet** — BATCH-10 (Feature A + PREREQ) is spec'd and ready but ON HOLD until the user explicitly says go.

## Sequencing — BOTH DONE
- PREREQ + Feature A: **DONE (BATCH-10, commit f817e809)** — typed WorkingState in the Entity Inspector; user confirmed live (cursors incrementing).
- Feature B MVP (selected-entity): **DONE (BATCH-11)** — live "Value" column in the Blackboard variable window via `ILiveBlackboardValueProvider` (name-match gate). Deferred "any entity"/canvas-overlay tier remains out of scope.
