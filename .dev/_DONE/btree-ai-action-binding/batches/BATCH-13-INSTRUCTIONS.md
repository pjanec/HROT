# BATCH-13 — S3-2: Scope-aware stateful slot key

**Task:** TASK-DETAIL.md → S3-2. **Slice 3 (§4.4 Behavior-scope shared working state MVP).**
**Design of record:** `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` §4.4 (see the **key-formula resolution note** 2026-07-12).
**Nature:** a pure function + unit tests. **No emitter/thunk change, no runtime, no authoring UI** — those are S3-3/S3-4/S3-1. This batch only generalizes the slot-key helper and proves it with tests.

## The resolved key formula (do not deviate)
The partitioned tier is a **per-entity ECS component**, so the slot key only disambiguates slots **within one entity** — `entityId` is NOT part of any key. The sharing unit is the *(scope, variable)* pair, so the key includes `variableId`. All inputs are compile-time constants.

| Scope | Key |
|---|---|
| `Node` | `FNV-1a(assetId, nodeVisualId)` — **unchanged from today** (do NOT add variableId — one state var per node today; changing it would perturb Slice-2 keys for no reason) |
| `Behavior` | `FNV-1a(assetId, variableId)` |
| `Entity` | `FNV-1a(variableId)` — no assetId (post-MVP; implement the branch, it's cheap) |

`variableId` is the bound variable's identity — use the variable **Name** string (the binding's `ExpressionTargetField`); hash its UTF-8/`ToString` bytes with the same FNV-1a loop the current key uses.

## Current code
`ComputeStatefulSlotKey(Guid assetId, Guid nodeVisualId)` at `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs:168` — an FNV-1a over `assetId.ToByteArray()` then `nodeVisualId.ToByteArray()`, returning `(int)(hash & 0x7FFFFFFF)`.

## Concrete changes
1. **`WorkingStateScope` enum (new, standalone file).** Create `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/WorkingStateScope.cs`:
   ```csharp
   namespace Hrot.AiEditor.Persistence;
   /// <summary>Scope of a stateful blackboard variable — determines its slot-key derivation (AIB-DD §4.4).</summary>
   public enum WorkingStateScope { Node, Behavior, Entity }
   ```
   > **COORDINATION:** a parallel batch (S3-1) also introduces `WorkingStateScope`. Put it in **this dedicated file** with this exact name/namespace/order so the two can be reconciled to one definition at integration. Do not inline it elsewhere.
2. **Scope-aware overload.** Add:
   ```csharp
   public static int ComputeStatefulSlotKey(Guid assetId, WorkingStateScope scope, Guid nodeVisualId, string variableId)
   ```
   - `Node`   → FNV-1a over `assetId` bytes then `nodeVisualId` bytes (must return the **exact same value** as the existing 2-arg method for the same assetId/nodeVisualId).
   - `Behavior`→ FNV-1a over `assetId` bytes then `variableId` UTF-8 bytes.
   - `Entity` → FNV-1a over `variableId` UTF-8 bytes only.
   - Mask `& 0x7FFFFFFF` as today.
   **Keep the existing 2-arg `ComputeStatefulSlotKey(Guid, Guid)`** unchanged (callers not yet migrated rely on it; it is definitionally the `Node` case — you may have it delegate to the new overload with `WorkingStateScope.Node` and an empty variableId, but ONLY if that yields the byte-identical result; otherwise leave it standalone).

## Success conditions (do not invent others)
Extend the existing key-key test file `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Bridge/StatefulSlotKeyTests.cs` (or the nearest existing slot-key test class):
- `SlotKey_Behavior_SameVar_TwoNodes_Equal` — `Behavior` key for the same (assetId, variableId) computed with two *different* nodeVisualIds ⇒ **equal** (nodeVisualId is not in the Behavior key → the two nodes share one slot).
- `SlotKey_Behavior_TwoVars_Differ` — two different `variableId`s under the same assetId ⇒ **distinct** keys.
- `SlotKey_Node_MatchesLegacy` — the new overload with `WorkingStateScope.Node` returns the **same** value as the existing 2-arg `ComputeStatefulSlotKey(assetId, nodeVisualId)` for the same inputs (no drift for Slice-2 assets).
- (optional but nice) `SlotKey_Entity_IndependentOfAsset` — `Entity` key for the same variableId under two different assetIds ⇒ **equal** (asset-independent).

## Constraints & guardrails
- Pure additive. Do NOT modify `EmitStatefulActionThunks`, the emitted thunk, `BehaviorIngressSystem`, `BlueprintBlackboardPartitions`, or any authoring UI.
- The `Node` path MUST stay byte-identical to today (the `SlotKey_Node_MatchesLegacy` test enforces this).
- Build/test: `dotnet test <proj>.csproj -c Debug --nologo`; if `NU1301 "local source './nugets'"`, `mkdir -p ./nugets` first. Do NOT run concurrent `dotnet` commands (CS2012 DLL lock).
- Touched projects: `Hrot.AiEditor.Persistence` + `Hrot.AiEditor.Generators.Tests` (wherever `StatefulSlotKeyTests` lives).

## Report back
Files changed; the exact key derivation per scope; confirmation `SlotKey_Node_MatchesLegacy` passes (byte-identical Node path); the new tests' results; before/after pass counts for the touched test project; note that `WorkingStateScope` is in its own file for S3-1 reconciliation. Verify edits persisted on disk (git diff) before reporting done.
