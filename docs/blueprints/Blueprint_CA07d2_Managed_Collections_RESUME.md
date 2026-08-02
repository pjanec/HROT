# RESUME / HANDOFF — CA-07d-2 managed collections (2026-08-02)

Self-contained handoff written before a preventive compaction. **CA-07d-1 (Contains/Find) is DONE +
committed; CA-07d-2 (managed collections) is DESIGN-APPROVED, NOT yet built — build next.**

**Branch:** `claude/blueprint-ca07d` (off `origin/main`). **Working tree:** clean.
**Commits on branch (vs main):**
| Commit | What |
|--------|------|
| `761f452d` | CA-07d-1 compiler — Contains/Find search nodes + `IrOp_ComponentCollectionSearch` |
| `b9532ac3` | CA-07d-1 editor — pins/titles/wire-bake/palette (Sonnet, Opus-reviewed) |
| `bc1a5a11` | tracker: CA-07d-1 done |

**PR not yet opened for this branch.** (CA-01..CA-07c already merged to main via PR #2 = commit `f3ebb947`.)

---

## STATUS: CA-07d overall
- **CA-07d-1 (Contains/Find, unmanaged/curated collections): ✅ DONE.** Two pure-data search nodes
  (`ComponentContainsNode` → Result:bool; `ComponentFindNode` → Index:int + Found:bool) consuming the
  CA-07b collection pin. `IrOp_ComponentCollectionSearch` emits a bounded loop over the SAME baked
  `Count`/`Item` accessors as `ComponentForEach`, comparing with `EqualityComparer<TElem>.Default.Equals`
  (Q#18-A), short-circuit on first match; `Find` fills both result temps in ONE loop. Gates: Generators
  **184/184** byte-identical, 121 filtered editor/host/lowering tests green.
- **CA-07d-2 (managed collections): ⬜ build next — design below.**

Architect Q#18 (fast-tracked, user-approved leans) in
`docs/blueprints/Architect_Question_18_Collection_ContainsFind_Managed.md`:
- Q18-A: `EqualityComparer<T>.Default.Equals` (one reflection-free path). *(done in 07d-1)*
- Q18-B: `Contains → bool`; `Find → (Index, Found)`. *(done in 07d-1)*
- **Q18-C: managed collections auto-resolve native `.Count`/`[i]`** (no curated helpers). *(07d-2)*
- **Q18-D: scope = `List<T>` / `IReadOnlyList<T>` / `T[]`**; defer `IEnumerable<T>`; no maps/sets. *(07d-2)*

---

## CA-07d-2 APPROVED DESIGN (user-confirmed 2026-08-02)

**Goal:** a MANAGED component (a `class`) with a `List<T>` / `IReadOnlyList<T>` / `T[]` field projects a
collection out-pin feeding all FIVE consumers (ForEach / ItemGet / ItemCount / Contains / Find) — but the
compiler emits NATIVE member access (`comp.Field.Count`, `comp.Field[i]`) instead of curated
`[BlueprintCollection]` static accessors. Everything else (pins, titles, wire-bake, loop scheduling,
EqualityComparer search) is REUSED unchanged.

### Running example
```csharp
public sealed class SquadRoster {         // managed component (reference type)
    public List<int> MemberIds = new();   // managed collection field — NO attributes, NO helpers
}
```

### The ONE new axis: accessor rendering mode (discriminator)
Add `enum CollectionKind { CuratedStatic, ManagedMember }` + a `CollectionFieldName` string.

| Baked on node | Curated (today) | Managed (new) |
|---|---|---|
| `CollectionKind` | `CuratedStatic` | `ManagedMember` |
| `ComponentTypeFqn` | `…BpCollectionDemo` | `…SquadRoster` |
| `CollectionFieldName` | (unused) | `"MemberIds"` |
| `CountAccessorFqn` | `…Ops.Count` | (empty) |
| `ItemAccessorFqn` | `…Ops.Item` | (empty) |
| `ElementTypeFqn` | `System.Int32` | `System.Int32` |

### Generated C# — curated vs managed (the entire delta)
Re-read component (managed is null-safe — GetManagedComponentRO can return null):
```csharp
// Curated: ref readonly var __comp = ref world.GetComponentRO<global::…BpCollectionDemo>(__ent);
// Managed: var __comp = world.HasManagedComponent<global::…SquadRoster>(__ent)
//                     ? simView.GetManagedComponentRO<global::…SquadRoster>(__ent) : default!;
//          var __ml   = __comp?.MemberIds;   // resolve list ONCE; null-safety in one place
```
Per-consumer (curated `global::{Fqn}(comp[,i])`  →  managed `__ml…`):
```csharp
// ItemCount : global::…Ops.Count(__comp)              →  (__ml?.Count ?? 0)
// ItemGet[i]: global::…Ops.Item(__comp, __i)          →  (__ml != null && (uint)__i < (uint)__ml.Count) ? __ml[__i] : default
// ForEach   : for(int __fe=0,__n=global::…Ops.Count(__comp); …) { var __item=global::…Ops.Item(__comp,__fe); … }
//           → for(int __fe=0,__n=__ml?.Count ?? 0;          …) { var __item=__ml![__fe];                 … }
// Contains/Find loop bound __csN = __ml?.Count ?? 0; element __ml![__csI]; EqualityComparer unchanged.
```
Pattern: **curated = `global::{Fqn}(comp[,i])`; managed = `__ml.Count` / `__ml![i]`, guarded by `__ml != null`.**
Managed ItemGet/loop-element must be null+bounds safe (mirrors the "managed read never throws" convention
already in `IrOp_GetManagedComponentRO` / `IrOp_FieldRead` SourceIsManaged).

### Blast radius (files to touch)
1. **Nodes.cs** (`Hrot.Blueprints.Compiler/Assets/`): add `enum CollectionKind`; add
   `CollectionKind CollectionKind` + `string CollectionFieldName` to the 5 consumer nodes
   (`ComponentForEachNode`/`ComponentItemGetNode`/`ComponentItemCountNode`/`ComponentContainsNode`/
   `ComponentFindNode`) AND to `ComponentFieldDecl` (so GetComponent's collection out-pin remembers the
   managed field name + kind). Default `CuratedStatic`/`""` (byte-stable; `[JsonIgnore]` when default so
   existing curated `.bp.json` fixtures don't gain a field).
2. **IrOperation.cs** (`Compiler/Ir/`): add `CollectionKind Kind = CuratedStatic` + `string ManagedFieldName = ""`
   to `IrOp_ForEach`, `IrOp_ComponentAccessorCall`, `IrOp_ComponentCollectionSearch`. (Positional-record
   trailing optional params → existing constructions unaffected.)
3. **StatementEmitter.cs** (`Compiler/Emit/`): add a small `RenderCount(...)`/`RenderItem(..., i)` helper
   that returns either `global::{Fqn}(comp[,i])` (CuratedStatic) or `__ml…` member access (ManagedMember).
   Every op's emit calls the helper instead of hardcoding `global::{Fqn}(…)`. For ManagedMember, emit the
   `var __ml{n} = __comp?.{Field};` local first (thread the managed-list local index via the op or a
   per-block map). **FlowForEach + every curated node stays CuratedStatic → goldens byte-identical.**
4. **Stage5_Schedule.cs**: in the 5 consumer data/loop cases, when the consumer's `CollectionKind ==
   ManagedMember`, re-read via `IrOp_GetManagedComponentRO` (not `IrOp_GetComponentRO`) and pass
   `Kind=ManagedMember` + `ManagedFieldName` into the emitted op. Curated path unchanged.
5. **Stage2_Validate.cs**: BP2066 currently requires non-empty accessor FQNs when Collection wired. For
   `ManagedMember`, the required-non-empty set becomes `ComponentTypeFqn` + `CollectionFieldName` (NOT the
   accessor FQNs, which are legitimately empty for managed). Split the check by kind.
6. **Editor** (mirror — delegate to Sonnet, Opus-review):
   - `ComponentFieldReflector.cs` (`…Editor/NodeDrawers/`): flag a field as a collection when its type is
     `List<T>` / `IReadOnlyList<T>` / `T[]` (element = generic arg / array element) on a managed component —
     no `[BlueprintCollection]` needed. Add `CollectionKind`/`FieldName` to `ReflectedComponentCollection`.
   - GetComponent projects the collection out-pin for such fields (diamond, element-typed) exactly like
     curated; `GetComponentNode.Fields` decl carries `CollectionKind=ManagedMember` + `CollectionFieldName`.
   - `BlueprintCommandSink.TryBakeCollectionConsumer`: when the source field is a managed collection, stamp
     `CollectionKind=ManagedMember` + `CollectionFieldName` onto the consumer, accessor FQNs left empty.
   - NodePinSchema/Stage0 pin shapes: UNCHANGED (element-typed Collection/Item pins already work — managed
     just fills ElementTypeFqn from the generic arg). Verify parity still holds.

### Tests to add
- Compiler lowering (mirror `ComponentSearchLoweringTests`/`ComponentCollectionConsumerLoweringTests`): a
  managed component fixture (`List<int>` field) → assert emitted `__ml?.Count ?? 0` / `__ml![i]` /
  null-safe ItemGet for each of the 5 consumers; assert NO `global::…Ops.` accessor call. Needs a managed
  demo component in `Hrot.AI.Behaviors` (mirror `BpCollectionDemo`, but a `class` with a `List<int>`).
- NodeCoverage: the 5 consumer kinds are already covered by curated fixtures — managed adds no new kinds, so
  no new coverage rows REQUIRED, but add managed fixtures for real evidence.
- Editor: `ComponentFieldReflector` managed-collection detection test; wire-bake stamps ManagedMember.
- **GATE: Generators 184/184 byte-identical (proves curated path untouched)** + full Blueprints.Tests
  (ignore the pre-existing reds: `TypeResolve_UnknownFieldType_EmitsBP1500`, the `NodeCoverage`
  BreakStruct/MakeStruct/SetMembers red, and env-flaky perf/ALC/MoveToAndFire — all predate this work).

### Slicing (user approved SINGLE slice)
One slice, all 5 ops incl. the shared `IrOp_ForEach` change — safe because the `CollectionKind` discriminator
defaults `CuratedStatic`, so `FlowForEach` goldens can't move. Gate on 184 identical to prove it.

---

## Workflow reminders (this workstream)
- Opus does the NOVEL compiler work hands-on (IR/emit/schedule/discriminator); Sonnet does the mechanical
  editor MIRROR (reflector/bake/palette) via a plain Agent-tool subagent; **Opus reviews the real diff +
  re-runs gates + commits** (never trust the subagent's self-report — verify NodePinSchema↔Stage0 parity in
  the diff). NO Zoo, NO worker-orchestrator.
- **NodePinSchema.GetCanonicalPins MUST stay byte-identical to Stage0_Rehydrate enrichers** or wires render
  "unused". (07d-2 doesn't change pin shapes, but re-verify.)
- SERIAL-184 gate: `Hrot.AiEditor.Generators.Tests` with `xunit.runner.json {parallelizeTestCollections:false,
  maxParallelThreads:1}`, run `--no-build` after a clean build.
- Use targeted `git add <path>`, NOT `git add -A` (editor open → -A sweeps editor-saved files).
- codebase-memory MCP tools NOT connected this session → use Grep/Read directly.

## Key reference points in code (as of `bc1a5a11`)
- Curated search op emit: `StatementEmitter.cs` `case IrOp_ComponentCollectionSearch` (search loop) +
  `case IrOp_ComponentAccessorCall` (single call) + `case IrOp_ForEach` (loop).
- Managed read emit to mirror: `StatementEmitter.cs` `case IrOp_GetManagedComponentRO`
  (`Has… ? Get… : default!`) and `IrOp_FieldRead` `SourceIsManaged` (`?.Field ?? default`).
- Stage5 consumer cases: `Stage5_Schedule.cs` `case ComponentItemGetNode/ItemCount/Contains/Find` +
  `ScheduleComponentForEachNode`.
- Editor managed detection: `ComponentFieldReflector.cs` `TryReflectCollections` / `IsManagedComponent`
  / `IsManagedFieldType`.
