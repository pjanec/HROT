# Blueprint Component Access — RESUME / HANDOFF (2026-08-01)

Self-contained resume point for the **ECS component read/write** workstream. Read this + the tracker
(`Blueprint_Component_Access_TASK_TRACKER.md`) + design (`Blueprint_Component_Access_Design.md`) to continue.

**Branch:** `claude/blueprint-component-read` (NOT yet PR'd to `main` — the whole workstream is here).
**Working tree:** clean (everything committed).

## Where we are

Building designer-facing `GetComponent` / `SetComponent` blueprint nodes (real ECS components, distinct from
blackboard Variables/Shared). Approved by the architect in **Q#15 (read)** + **Q#16 (write)** (both in
`docs/blueprints/Architect_Question_15/16_*.md`). Built as **7 batches CA-01..CA-07** (tracker has full detail).

| Batch | What | Commit |
|-------|------|--------|
| CA-01 | GetComponent multi-field read — compiler spine | `628c249a` |
| CA-02 | GetComponent read — editor (palette/picker/drawer) | `d64b7d36` |
| CA-03 | SetComponent self-write — compiler spine | `4d6f746d` |
| CA-04 | SetComponent write — editor (`[BlueprintWritable]` palette) | `a91aa734` |
| CA-05 | Managed read (`GetManagedComponentRO`, guarded) | `d016d91c` |
| CA-06 | Managed write (ECB whole-replace, Instance-only) | `95ef4a43` |
| **demos** | 3 demo blueprints + `BpComponentDemo` component (visual test) | `f9dc9b94` |
| CA-07 | Collections (read: iterate+index+length) — **DESIGN DRAFTED, NOT BUILT** | doc `deddca1e` |

**CA-01–06 are DONE**: full Get/Set Component, unmanaged + managed, compiler + editor, each gated at
**184/184 serial** (see gate below). CA-07 is the only remaining batch.

## IMMEDIATE NEXT STEP

1. **User is doing a visual check** of CA-01–06 via the demos (open `GetComponentDemo` / `GetComponentTargetDemo`
   / `SetComponentDemo` in `Hrot.AI.Behaviors/Assets/Blueprints/`). Wait for their findings; fix anything they hit.
2. **CA-07 (collections) — Q17-A DECIDED: A2 (full Unreal collection-pin UX, committed up front)** (user call,
   2026-08-01). Q#17 doc: `docs/blueprints/Architect_Question_17_Component_Collection_Read.md` (answers recorded).
   A collection component field projects a **collection out-pin**; generic `ForEach` / `Get[i]` / `Length` nodes
   consume a **collection in-pin**; the pin carries only the entity at runtime and the editor **bakes
   `(ComponentFqn, field, CollectionKind, Count/Item accessor)` onto the consumer on wire** (author-time, stays
   reflection-free). First slice = iterate + `Length` + `Get[i]` (unmanaged: `FixedList<T>`, `[InlineArray]`,
   `DynamicBuffer<T>`); managed collections + `Contains`/`Find` are later sub-slices. **This is novel compiler +
   editor work (new pin kind, new IR ops, author-time wire-baking) — build it hands-on (Opus), not delegated
   wholesale to Sonnet; delegate only the mechanical mirror bits (palette/drawer).** Batch plan lives in the
   design doc + tracker once the seam map is in.

## How to continue (the workflow — user-approved)

- **Batched execution:** Sonnet (via the Agent tool, `model: sonnet`, general-purpose) builds each mechanical /
  mirror-an-existing-pattern batch; **Opus (me) reviews EVERY diff** (not just the report — pull `git diff`,
  verify the novel bits), personally does/reviews the novel IR-op + Stage5 lowering + emit + validators, runs the
  gate, then commits. **No Zoo.** No worker-orchestrator for this workstream — plain Agent-tool Sonnet subagents.
- Agents leave work **uncommitted** for my review; I gate + commit. Update the tracker (check boxes, flip status
  ✅, running-log line) as part of each commit.
- Keep going through batches until a real decision point, then surface (the user said "keep going until all clear
  which way to go then i will check visually").

## THE GATE (run before every commit)

Clean-rebuild → **`Hrot.AiEditor.Generators.Tests` SERIAL must stay 184 byte-identical** (nodes are additive) +
the batch's new tests green + `AI.Behaviors` builds clean. Serial run:
```
# ensure bin/Debug/net8.0/xunit.runner.json = {"parallelizeTestCollections":false,"maxParallelThreads":1}
dotnet build Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -c Debug
echo '{"parallelizeTestCollections": false, "maxParallelThreads": 1}' > Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/bin/Debug/net8.0/xunit.runner.json
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -c Debug --no-build
```
Component tests: `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/... --filter "FullyQualifiedName~Component"`.

## PRE-EXISTING test reds (DO NOT chase — verified on the base, not ours)

Full `Hrot.Blueprints.Tests` (parallel) shows ~8–9 reds that are pre-existing/flaky and unrelated to this
workstream: `Stage4_TypeResolveTests` BP1500 (UnknownFieldType), `NodeCoverageTests.AllNodeKinds` re
`Make/Break/SetMembersStruct`, 2 perf/allocation thresholds, and several ALC/dynamic-compile tests that pass in
isolation but fail under parallel contention. **These fail on `main` too.** The GATE is the serial 184 Generators
suite (green), plus filtered Component tests (green).

## KEY FACTS / DECISIONS (so you don't re-derive)

- **Component marker:** `[Fdp.Core.ComponentId(n)]` (NOT an interface/base). `ComponentTypeProvider` discovers via
  `IsDefined(ComponentIdAttribute)`. `[BlueprintWritable]` lives in **`Fdp.Core`** (next to `[ComponentId]`).
- **`IsManaged`:** managed component/field = reference type. Reflector uses `IsReferenceOrContainsReferences<T>`
  (per-field) + `ResolveType(fqn) is {IsClass:true}` (component-level).
- **`[BlueprintWritable]` enforcement = EDITOR-PRIMARY (option a):** the netstd2.0 compiler CAN'T reflect it, so
  the Set palette only offers writable types; the compiler validator (`V_ComponentAccessRules`) does STRUCTURAL
  checks only (no writable-catalog).
- **Managed read** emits a GUARDED read: `HasManagedComponent<T>(e) ? GetManagedComponentRO<T>(e) : default!`
  because `GetManagedComponentRO<T>(class)` THROWS on absent (unlike the FDP_PARANOID-gated unmanaged RO).
  `IrOp_FieldRead.SourceIsManaged` → `src?.Field ?? default`. `HasManagedComponent` is public on `EntityRepository`
  (`wv`); `GetManagedComponentRO` needs the `ISimulationView` cast → `EmissionContext.SimulationViewVar`.
- **Managed write** = ECB whole-replace `ecb.SetManagedComponent(self, value)`, guarded, **Instance-only**
  (`EmissionContext.EcbVar` throws for AiPrimitive — `TickCore` has no ECB; gated by BP2065 at Stage2).
- **Unmanaged write** = direct `GetComponentRW<T>(self)`, **write-if-present** (HasComponent guard, no implicit
  add), **wired-only fields** (unwired preserved). Self-only (no Target on Set).
- **Frozen legacy GetComponent:** `Fields==null` single-field shape projects ONLY `Value` (no Target/Found); all
  new capability (Target/Found/per-field) is multi-pin mode. Stage0 `EnrichGetComponentPins` ⇄
  `NodePinSchema.GetComponentPins` MUST stay in parity (enforced by `GetComponentPinParityTests` /
  `SetComponentPinParityTests` — they run real `Stage0_Rehydrate.Run`).
- **Diagnostics:** BP2060 empty-FQN, BP2061 malformed-FQN, BP2062 SetComponent Target (self-only), BP2063 managed
  read persisted into SetVariable/SetShared, BP2064 per-field managed write, BP2065 managed write in AiPrimitive.
- **New IR ops:** `IrOp_WriteComponentFields` (guarded unmanaged write), `IrOp_GetManagedComponentRO`,
  `IrOp_SetManagedComponent`. Reused: `IrOp_GetComponentRO`, `IrOp_FieldRead`, `IrOp_HasComponent(IsManaged)`,
  `IrOp_Self`, `IrOp_GetComponent`(RW).
- **Demo:** `BpComponentDemo` = `[ComponentId(188)][BlueprintWritable] struct { int Health; int Ammo; float Speed; }`
  in `Hrot.AI.Behaviors/Components/`. ComponentId 188 = Hrot app block (160–199), verified collision-free.
- **`.bp.json` demos** use EXPLICIT pins (edit-safe) matching the new pin shapes.

## After CA-07: open the PR

When CA-07 lands (or if the user wants to ship CA-01–06 without collections), open a PR
`claude/blueprint-component-read` → `main` (the workstream is currently unmerged). Content decision still pending:
**which real game components to mark `[BlueprintWritable]`** (only the demo + a test component are marked so far).
