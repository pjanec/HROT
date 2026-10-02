# BATCH-15 — S3-3: Scope-aware baked const (thunk + topology key)

**Task:** TASK-DETAIL.md → S3-3. **Slice 3 (§4.4 Behavior-scope shared working state MVP).**
**Design of record:** `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` §4.4 (key-formula resolution 2026-07-12: keys are compile-time constants — NO runtime hashing).
**Depends on:** S3-1 ✓, S3-2 ✓, **S3-4 (BATCH-14) ✓ — must already be integrated on this branch.** BATCH-14 added `ResolveStatefulSlotKey(BehaviorTreeAssetDto dto, string? targetField, Guid nodeVisualId)` to `BTreeBridgeEmitCore` and wired it into the manifest emit. This batch reuses that helper for the two *other* places the slot key is baked.
**Nature:** make the baked slot-key constant **scope-aware** so co-bound `Behavior` nodes resolve to one shared slot. The thunk shape is unchanged — only the value's derivation changes. Node scope stays byte-identical.

---

## Why this is TWO call sites, not one (verified, read before editing)

The stateful slot key is baked as a compile-time literal in **three** places, and all three MUST agree or the runtime lookup misses:
1. **Manifest** — `BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray` — **already scope-aware (done in BATCH-14).**
2. **Thunk registration + baked `const`** — `BTreeBridgeEmitCore.EmitStatefulActionThunks` (~line 534): `int slotKey = ComputeStatefulSlotKey(dto.AssetId, actNode.VisualId);` → registered under `{MethodFqn}@{paramOffset}@{slotKey}`, and emitted as `const int __slotKey = {slotKey};`. **STILL Node-scope — you change this.**
3. **Topology blob key** — `BTreeEmitCore.EmitAction` (~line 690): `int slotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, node.VisualId);` → `blobKey = "{MethodFqn}@{paramOffset}@{slotKey}"`, stored in the FastBTree blob so each topology node dispatches to the registered thunk by this exact string. **STILL Node-scope — you change this too.**

TASK-DETAIL's "Touches" for S3-3 names only `EmitStatefulActionThunks`, but the named success test `BehaviorScoped_TwoNodes_ShareOneSlot` **cannot pass** unless the topology key (#3) is also scope-aware: if the thunk registers under the shared Behavior key while the topology node still references the per-node Node key, the interpreter's lookup misses and the node fails. So #2 and #3 must move together, driven by the identical `ResolveStatefulSlotKey` derivation. This is not new scope — it is the code required to satisfy the given test.

**Effect once both change:** two nodes binding the same `Behavior` variable produce the **same** `slotKey` →
- their topology blob keys are identical → both dispatch to one thunk;
- the thunk-registration `seen.Add(key)` dedups → one thunk registered;
- the baked `const __slotKey` + the one provisioned Behavior slot (BATCH-14) line up → both nodes read/write the same `DemoCursorState`.

---

## Concrete changes

### 1. Promote the shared helper to `internal`
In `BTreeBridgeEmitCore.cs`, change `ResolveStatefulSlotKey` (added in BATCH-14) from `private static` to **`internal static`** so `BTreeEmitCore` (same assembly, `Hrot.AiEditor.Persistence`) can call it. Single source of truth for the key derivation — do not duplicate the scope-resolution logic.

### 2. Thunk — `BTreeBridgeEmitCore.EmitStatefulActionThunks` (~line 534)
```csharp
// BEFORE
int slotKey = ComputeStatefulSlotKey(dto.AssetId, actNode.VisualId);
// AFTER
int slotKey = ResolveStatefulSlotKey(dto, p.ExpressionTargetField, actNode.VisualId);
```
Everything downstream (the `key` string, the baked `const int __slotKey = {slotKey};`, the tier-dispatch body) is unchanged — it just consumes the new value.

### 3. Topology — `BTreeEmitCore.EmitAction` (~line 690)
`EmitAction` currently receives only `assetId` (line ~646), not the DTO or the variables' scopes. Thread the scope in and use the shared helper:
- Change `EmitAction`'s signature to receive the `BehaviorTreeAssetDto dto` (the caller `EmitLeafNode`/`EmitNode` at ~line 613 already has `dto` in scope — it passes `dto.AssetId` today). Prefer passing `dto` over just `assetId` so the helper can read `dto.Blackboard.Variables`. Update the call site(s) accordingly.
- Replace the key computation:
```csharp
// BEFORE
int slotKey  = BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, node.VisualId);
// AFTER
int slotKey  = BTreeBridgeEmitCore.ResolveStatefulSlotKey(dto, actionTargetField, node.VisualId);
```
Keep `blobKey = $"{p.MethodFqn}@{statefulParamOffset}@{slotKey}"` unchanged in shape.

> If threading `dto` through `EmitAction` ripples awkwardly (e.g. other callers), the acceptable alternative is to pass a precomputed `IReadOnlyDictionary<string, WorkingStateScope>` scope map alongside the existing `variableOffsets` parameter and have `ResolveStatefulSlotKey` accept scope directly — but the DTO-threaded form is preferred for a single source of truth. Do NOT re-implement FNV-1a anywhere.

### Do NOT touch
`EmitStatefulWorkingSlotsArray` (BATCH-14 already did it), `BehaviorIngressSystem`, `BlueprintBlackboardPartitions`, `StatefulSlotInfo`, authoring UI, `ComputeStatefulSlotKey` (both overloads stay as-is).

---

## Success conditions (implement EXACTLY these — do not invent others)

Add tests to `Hrot.AiEditor.Generators.Tests` (same project/pattern as `Demos/T20_MultiStateful_ProofTests.cs` and BATCH-14's `Demos/S3_SharedSlotProvisioningTests.cs`). Build assets in-memory, serialize with `BTreeJsonServices.Serialize(dto)`, run the generate→compile→load→register→provision→tick pipeline. Reuse `DemoCounterNodes.Action_AdvanceCursor` / `DemoCursorParams` / `DemoCursorState`.

### Compile gate — `BehaviorScoped_Asset_CleanRebuild` (or fold into Test 1's setup)
Generate a Behavior-scoped stateful asset and assert the generator produces **no diagnostics** and the sources compile with **0 errors** (mirrors the T20 `result.Diagnostics.Should().BeEmpty()` + `CompileMultiAndLoad` success gate that closed DEBT-AIB-026).

### Test 1 — `BehaviorScoped_TwoNodes_ShareOneSlot`
- Asset: managed blackboard with one variable `shared` (type `DemoCursorParams`, `Role = State`, `Scope = Behavior`, and author a default so `Limit` is seeded — e.g. `Limit = 3`). Topology: Root → Sequence → **two** Action nodes (distinct `VisualId`s), both binding `Action_AdvanceCursor` to `ExpressionTargetField = "shared"`, `ThreeParamReusableStateful`, `WorkingStateTypeId = DemoCursorState`.
- Generate → compile → register → provision via `BehaviorIngressSystem` (T20 setup).
- Assert exactly **one** slot is provisioned: `def.StatefulWorkingSlots.Count == 1` and `BlueprintBlackboardPartitions.GetSlotCount(mem) == 1` at the Behavior key.
- Tick with fresh `BehaviorTreeState` per tick (T20 pattern) enough times for the Sequence to complete (both nodes execute). Read the single shared slot's `DemoCursorState.Cursor`.
- **The sharing proof:** the cursor value reflects the *cumulative* increments of BOTH nodes on ONE slot (node B continues from where node A left off — it sees A's write the same run), which is impossible with independent per-node slots. With `Limit = 3`: node A drives the shared cursor 0→3 across ticks (Success at 3), then in that same tick node B runs and increments the SAME cursor 3→4. Assert the final cursor is `4` (a value only reachable if both nodes share the slot; independent slots would give A=3, B=1). Derive the exact tick count from the `Running`/`Success` semantics (`Action_AdvanceCursor`: `ws.Cursor++; return ws.Cursor < p.Limit ? Running : Success;`) and document it in the test like T20 does. Also assert `TryGetSlotOffset(mem, behaviorKey, out _)` is true and that the key equals `ComputeStatefulSlotKey(assetId, WorkingStateScope.Behavior, Guid.Empty, "shared")`.

### Test 2 (regression) — `NodeScoped_StillBakedConst`
- Assert Slice-2 Node-scoped emission is unchanged:
  - Build a Node-scoped 2-node asset (two variables `cursorA`/`cursorB`, each `Role = State`, `Scope = Node`, each bound at its own node with `Action_AdvanceCursor`). Emit the bridge via `BTreeBridgeEmitCore.EmitBridge(dto, sizeResolver)` and the topology via the generator; assert the two baked slot keys are **distinct** and each equals the legacy `ComputeStatefulSlotKey(assetId, nodeVisualId)` (no drift → independent slots preserved).
- AND confirm the existing `Demos/T20_MultiStateful_ProofTests` (`TwoStatefulInstances_MaintainIndependentState`, `MixedStatelessAndStateful_Coexist`) **still pass unchanged** — that IS the `SameStatefulPrimitive_TwoNodes_IndependentSlots` regression guard referenced in TASK-DETAIL. Run them; do not modify them.

---

## Byte-identity / regression gate (mandatory)
- Node scope stays byte-for-byte identical (the entire committed corpus is Node/Input). Re-run and confirm green:
  - `Hrot.AiEditor.Persistence.Tests` — **byte-identity gate** (CombatShowcase/SampleScout).
  - `Hrot.AiEditor.Generators.Tests` — T20 proof tests + `StatefulSlotKeyTests` (S3-2) + BATCH-14's `S3_SharedSlotProvisioningTests` all still pass; plus your two new tests.
- `dotnet build-server shutdown` before the codegen verification run.
- Pre-existing non-regressions to ignore (do NOT "fix"): the 2 `MigrationEquivalenceTests` byte-stability cases (DEBT-TRACKER).

## Constraints & guardrails
- Production changes: `BTreeBridgeEmitCore.cs` (visibility bump + one call-site swap) and `BTreeEmitCore.cs` (thread `dto` into `EmitAction` + one call-site swap). No other production files.
- Do NOT commit any `.btree.json` asset — tests build DTOs in-memory.
- Do NOT re-implement FNV-1a; always go through `ComputeStatefulSlotKey` / `ResolveStatefulSlotKey`.
- Do NOT touch the parked-red items (D-8 Presentation, D-13 DistributedTank).

## Environment
- `dotnet test <proj>.csproj -c Debug --nologo`; `--filter "FullyQualifiedName~BehaviorScoped"` for a fast loop.
- `NU1301 "local source './nugets'"` → `mkdir -p ./nugets` first. Never run two `dotnet` build/test concurrently in one tree (CS2012). Serial only.

## Report back
1. Full `git diff` of `BTreeBridgeEmitCore.cs` and `BTreeEmitCore.cs`.
2. New tests' file + results; the exact tick arithmetic for `BehaviorScoped_TwoNodes_ShareOneSlot` and the observed final cursor value.
3. Before/after pass counts for `Hrot.AiEditor.Generators.Tests` + `Hrot.AiEditor.Persistence.Tests`; explicit confirmation T20 proof tests still pass and byte-identity is green.
4. Confirmation you touched only the two named production files.
5. `git status --short` + `git diff --stat`. Verify edits persisted on disk before reporting done.
</content>
