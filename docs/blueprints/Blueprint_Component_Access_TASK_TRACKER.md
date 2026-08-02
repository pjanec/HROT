# Blueprint Component Access — TASK TRACKER

Execution tracker for `Blueprint_Component_Access_Design.md` (approved Q#15 read + Q#16 write). Branch:
`claude/blueprint-component-read`.

**Execution policy:** Sonnet executes mechanical / mirror-an-existing-pattern batches; **Opus reviews every diff**
and personally does / reviews the novel IR-op + Stage5 lowering + emit. No Zoo.
**Gate (every batch):** clean-rebuild → run **`Hrot.AiEditor.Generators.Tests` SERIAL** (`xunit.runner.json`
`{parallelizeTestCollections:false, maxParallelThreads:1}` in `bin/Debug/net8.0/`, then `dotnet test --no-build`)
→ **184 byte-identical** must hold (nodes are additive), **plus** the batch's new tests green. `AI.Behaviors`
builds clean.
**Legend:** ⬜ not started · 🔧 in progress · ✅ done (gate passed) · 🔎 in Opus review.

## Batches

| # | Batch | Slice | Model | Status |
|---|-------|-------|-------|--------|
| CA-01 | Read compiler spine (unmanaged) | 1a | Sonnet + Opus(lowering) | ✅ |
| CA-02 | Read editor (unmanaged) | 1a | Sonnet | ✅ |
| CA-03 | Write compiler spine (unmanaged) | W1 | Sonnet + Opus(IR/lowering/emit/validator) | ✅ |
| CA-04 | Write editor (unmanaged) | W1 | Sonnet | ✅ |
| CA-05 | Managed read | 1b | Sonnet + Opus(flow rules) | ✅ |
| CA-06 | Managed write (ECB) | W2 | Opus + Sonnet(mirror) | ✅ |
| CA-07a | Collection pin type + GetComponent collection out-pin | 2 | Sonnet + Opus(review) | ✅ |
| CA-07b | Consumer nodes + IR + emit (ForEach/Get[i]/Length) | 2 | Sonnet + Opus(review/BP2050 fix) | ✅ |
| CA-07c | Editor wire-baking + palette + drawers + demo bp | 2 | Sonnet + Opus(review/wildcard fix) | ✅ |
| CA-07d-1 | Contains/Find nodes (unmanaged, extend CA-07b) | 2 | Opus(compiler 761f452d) + Sonnet(editor b9532ac3, Opus-reviewed) | ✅ |
| CA-07d-2 | Managed collections (List/IReadOnlyList/T[], native .Count/[i]) | 2 | later | ⬜ |

*(CA-07d split per Architect Q#18 fast-track: A3 EqualityComparer emit, B2 Find=Index+Found + ship both, C2 managed auto-resolve, D scope List/IReadOnlyList/T[].)*

---

### CA-01 — Read compiler spine (unmanaged) · Slice 1a
- [x] `ComponentFieldDecl { Name; TypeId }` (+ `GetComponentNode.Fields: List<ComponentFieldDecl>?`, additive JSON) — `Assets/Nodes.cs`
- [x] Stage0 `EnrichGetComponentPins` — optional `Target` (Entity) in, `Found` (bool) out, per-field `Value` outs when `Fields` baked; legacy single-field when not — `Compiler/Stages/Stage0_Rehydrate.cs`
- [x] Stage5 multi-field read lowering — one `IrOp_GetComponentRO` + N× `IrOp_FieldRead`; `Found` ← `IrOp_HasComponent` — `Compiler/Stages/Stage5_Schedule.cs` **(Opus reviews)**
- [x] Tests: pin projection (Target/Found/fields), lowering, emit golden (multi-field + Target + Found) — `Hrot.Blueprints.Tests/…`
- **Files:** Nodes.cs, Stage0_Rehydrate.cs, Stage5_Schedule.cs (+tests). **Reuse:** `EnrichGetSharedPins`, GetShared multi-pin read. **No new IR/emit.**
- **✅ Done — Opus-reviewed + gated** (legacy shape frozen; 7/7 GetComponent + 184/184 serial): see running log.

### CA-02 — Read editor (unmanaged) · Slice 1a
- [x] `ComponentFieldReflector` — public fields → `(Name, TypeId, IsManaged)`, **no offset**, keeps managed fields — `NodeDrawers/`
- [x] `ComponentTypeProvider` + `GetComponentPaletteEntries` (reflect all component types) — `NodeDrawers/`, registered in `BlueprintEditorBootstrap.CreatePaletteRegistry`
- [x] `NodePinSchema.GetComponentPins` — parity with Stage0 `EnrichGetComponentPins` — `Host/NodePinSchema.cs`
- [x] `ComponentNodeDrawers` (Get) — component picker (NO "Expand to field pins" toggle — always multi-pin, see file doc comment) — `NodeDrawers/`
- [x] `BlueprintNodeModel` — title `Get Component [T]` + `NodeState.Error` on unresolved component (reuse `IsUnresolvedClrCall` path) — `Host/BlueprintNodeModel.cs`
- [x] Editor tests: reflector, palette discovery, pin parity, title, stale-ref
- **Reuse:** `SharedStructFieldReflector` (pattern only, not code — CA-02 keeps managed fields), `SharedTypePickerLogic` (reused AS-IS, no duplicate), `ReflectionSharedStructTypeProvider` (pattern), `GetSharedPins` (pattern).
- **2026-08-01, CA-02, Sonnet:** built the read editor (unmanaged) — `ComponentFieldReflector` +
  `ReflectedComponentField` (`NodeDrawers/ComponentFieldReflector.cs`; managed/unmanaged determined
  via `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` invoked reflectively — the exact CLR test
  the `unmanaged` generic constraint uses, so it can never disagree with what `GetComponentRO<T>`
  accepts); `IComponentTypeProvider`/`ReflectionComponentTypeProvider` keyed on **`[ComponentId]`**
  presence (`Fdp.Core.ComponentIdAttribute`) — this IS the component marker (no separate
  interface/base class exists; `ComponentTypeRegistry.GetOrRegisterManaged` requires it on every
  component struct/class); `ComponentPaletteEntries.GetComponentEntries` (skips zero-field/tag
  components, mirrors `MakeBreakStructPaletteEntries`); `NodePinSchema.GetComponentPins` (byte-for-
  byte mirror of the frozen Stage0 `EnrichGetComponentPins`: multi-pin `Target`+fields+`Found`,
  legacy single `Value`); `GetComponentNodeDrawer`/`GetComponentNodeSession`
  (`NodeDrawers/ComponentNodeDrawers.cs`, reuses `SharedTypePickerLogic` directly — no duplicate
  filter/contains helper — deliberately has NO expand-toggle, since a component is always multi-pin);
  `BlueprintNodeModel` title `Get Component [T]` + `NodeState.Error` via a NEW
  `ComponentFieldReflector.ResolveType`-based check (kept separate from `TryReflect` so a resolvable
  zero-field/tag component is never misreported as unresolved). Registered both in
  `BlueprintEditorBootstrap` (drawer + palette). Added `Categories.Component` (distinct from
  `SharedState`/`Variables`).
  **Gate:** `Hrot.Blueprints.Editor` + `Hrot.AI.Behaviors` build clean; 66/66 new CA-02 editor tests
  green (reflector, discovery, palette, drawer/session, pin-parity-vs-Stage0, title, stale-ref);
  `Hrot.AiEditor.Generators.Tests` **184/184** byte-identical (serial). Full `Hrot.Blueprints.Tests`
  suite run in parallel shows 8 pre-existing reds (Stage4 BP1500, NodeCoverage Make/Break/SetMembers,
  2 perf tests, 4 ALC-not-reclaimed-under-parallel-load) — reproduces the same failure set CA-01
  already characterized as pre-existing/not-CA-introduced (the 4 ALC ones are a parallel-runner
  artifact of running the whole suite together, not serial-mode flakiness).
- **2026-08-01, CA-02, Opus review:** reviewed — `GetComponentPins` is an exact mirror of the frozen
  Stage0, and `GetComponentPinParityTests` genuinely cross-checks (runs real `Stage0_Rehydrate.Run` +
  pins the literal shape, so drift on either side fails). `[ComponentId]` discovery + `IsManaged` via
  `IsReferenceOrContainsReferences<T>` are correct; stale-ref (empty→Normal, unresolved→Error) correct.
  Re-gate: **68 Component editor tests + 184/184 serial**. **CA-02 ✅ done.**

### CA-03 — Write compiler spine (unmanaged) · Slice W1
- [x] `[BlueprintWritable]` attribute in **`Fdp.Core`** (next to `[ComponentId]`) + `SetComponentNode` (`ComponentTypeFqn`, `Fields`, `IsManaged`) + JsonDerivedType `"SetComponent"` — `Assets/Nodes.cs`
- [x] **`IrOp_WriteComponentFields`** (single guarded-block op — Opus-preferred over per-field for clean HasComponent-guard + shared RW ref) — `Compiler/Ir/IrOperation.cs`
- [x] Stage0 `EnrichSetComponentPins` — exec In/Out, per-field data-ins, `Written` out; **self-only, no Target** — `Stage0_Rehydrate.cs`
- [x] Stage2 `V_ComponentAccessRules` — **structural only** (BP2060 empty / BP2061 malformed FQN / BP2062 self-only reject Target); **no `[BlueprintWritable]` check** (editor-primary, option a) — `Stage2_Validate.cs`
- [x] Stage5 write lowering — wired-only field resolution (unwired preserved) + `IrOp_Self`; `Written` ← guard bool — `Stage5_Schedule.cs`
- [x] `StatementEmitter` — `var __t{i}=HasComponent<T>(self); if(__t{i}){ ref var __wc=ref GetComponentRW<T>(self); __wc.f=__t{v}; }` — `Compiler/Emit/StatementEmitter.cs`
- [x] Tests: write lowering/emit (write-if-present, wired-only), validator (empty/malformed/Target)
- **Reuse:** `ChannelCommandLowering` `GetComponentRW(self)` emit, `SetSharedNode` per-field lowering, `V_SharedStateRules`.
- **2026-08-01, CA-03, Sonnet + Opus review:** built + Opus-reviewed. Deviation from the doc's per-field `IrOp_WriteComponentField`: used a **single `IrOp_WriteComponentFields`** guarded-block op (Opus-directed — cleaner HasComponent guard + one shared RW ref, wired-only writes → unwired preserved). Self-only enforced at Stage0 (no Target pin) + validated (BP2062). `[BlueprintWritable]` in `Fdp.Core`; compiler does NOT reflect it (editor-primary). Gate: compiler/`Fdp.Core`/`AI.Behaviors` clean; **15 CA-03 tests + 184/184 serial**. **CA-03 ✅ done.**

### CA-04 — Write editor (unmanaged) · Slice W1
- [x] `SetComponentPaletteEntries` — reflect **`[BlueprintWritable]`** types only — `NodeDrawers/`
- [x] `NodePinSchema.SetComponentPins` — parity with Stage0 `EnrichSetComponentPins` — `Host/NodePinSchema.cs`
- [x] `ComponentNodeDrawers` (Set) — writable-set picker — `NodeDrawers/`
- [x] `BlueprintNodeModel` — title `Set Component [T]` + stale-ref error
- [x] Editor tests: writable-only discovery, pin parity, title
- **Reuse:** CA-02 infra + `SetSharedNodeDrawer`/`SetSharedPins`.
- **2026-08-01, CA-04, Sonnet:** built the write editor (unmanaged) — `ReflectionWritableComponentTypeProvider`
  (`NodeDrawers/ComponentTypeProvider.cs`), a writable-only `IComponentTypeProvider` filtered on
  **both** `[ComponentId]` and `Fdp.Core.BlueprintWritableAttribute`; factored the shared assembly
  scan into an internal `ComponentTypeScan.Compute(predicate)` helper so the read (`ReflectionComponentTypeProvider`,
  unchanged, all-components) and write providers share one reflection walk (DRY, per the batch
  spec). `ComponentPaletteEntries.SetComponentEntries` mirrors `GetComponentEntries` — one
  `"Component.Set.{fqn}"` entry per writable component with ≥1 reflectable field, `CreateInstance`
  bakes `ComponentTypeFqn` + a fresh `Fields` list (never shared across placements); `IsManaged` left
  at its default `false` (CA-06 territory). `NodePinSchema.SetComponentPins` is a byte-for-byte
  mirror of the frozen Stage0 `EnrichSetComponentPins`: exec In/Out + one data-IN per field +
  data-out `Written` (unconditional, even with zero fields baked) — no `Target`, ever (self-only).
  `SetComponentNodeDrawer`/`SetComponentNodeSession` (`NodeDrawers/ComponentNodeDrawers.cs`) mirror
  `GetComponentNodeDrawer`/`Session` exactly (reuses `SharedTypePickerLogic`, no expand toggle —
  always multi-pin, routes through `EditService.NotifyStructureChanged`); managed fields are still
  listed by the picker (not special-cased out) but flagged with a "write path not yet wired (CA-06)"
  caveat instead of CA-02's "read-only, never persisted" caveat. `BlueprintNodeModel`: generalized
  the CA-02 `IsUnresolvedComponent` check to take a `string?` FQN (was `GetComponentNode`-typed) so
  both `GetComponentNode.ComponentTypeFqn` and `SetComponentNode.ComponentTypeFqn` share the same
  stale-ref detector; title `Set Component [T]` (bracketed, mirrors Get); category
  `NodeCategory.VariableSet` (mirrors `SetSharedNode`). Registered both in
  `BlueprintEditorBootstrap` (new `writableComponentTypeProvider` param on
  `CreateNodeDrawerRegistry`, defaulting to `ReflectionWritableComponentTypeProvider`; new
  `SetComponentEntries` registration in `CreatePaletteRegistry`). No compiler changes (CA-03 stays
  frozen; `BuiltInNodeRegistry`'s `SetComponentNode => [ExecIn(), ExecOut()]` static skeleton
  already existed from CA-03).
  **Gate:** `Hrot.Blueprints.Editor` + `Hrot.AI.Behaviors` build clean; 104/104 `Hrot.Blueprints.Tests`
  Editor+Host-subset tests green (66 pre-existing CA-02 + new CA-04: writable-discovery,
  `SetComponentEntries`, `SetComponentPinParityTests` real-Stage0 cross-check, drawer/session,
  title/stale-ref); `Hrot.AiEditor.Generators.Tests` **184/184** byte-identical (serial). Full
  `Hrot.Blueprints.Tests` suite (parallel) reproduces the SAME 8 pre-existing reds CA-01/CA-02
  already characterized (4 ALC-not-reclaimed-under-parallel-load, 2 perf thresholds, 1 NodeCoverage
  Make/Break/SetMembers, 1 Stage4 BP1500) — no new regressions. Awaiting Opus review.

### CA-05 — Managed read · Slice 1b
- [x] `GetComponentNode.IsManaged` baked by editor reflector; `IrOp_GetManagedComponentRO` **(new)** → `view.GetManagedComponentRO<T>` — `IrOperation.cs`, `StatementEmitter.cs`, `Stage5` **(Opus reviews)**
- [x] Managed fields exposed in the read picker with the **persistence caveat** UI — `ComponentNodeDrawers`
- [x] Stage2 — reject **managed→unmanaged** wiring; uphold BP1503 (no persist) — `V_ComponentAccessRules`
- [x] Tests: managed read emit (`GetManagedComponentRO`), managed→unmanaged rejection, no-persist
- **Confirm:** view API `GetManagedComponentRO<T>`.
- **2026-08-01, CA-05, Sonnet:** built the managed read path. `GetComponentNode.IsManaged` (NEW — the
  doc's Context section said this already existed; it didn't, only `SetComponentNode.IsManaged` did)
  baked by `GetComponentNodeDrawer.ApplyComponentTypeFqn` via a NEW `ComponentFieldReflector.
  IsManagedComponent(fqn)` (component-LEVEL check: `ResolveType(fqn) is { IsClass: true }` — distinct
  from CA-02's per-FIELD `IsManaged`, which uses `IsReferenceOrContainsReferences` and also catches a
  managed field embedded in an otherwise-unmanaged struct). New `IrOp_GetManagedComponentRO(fqn,
  Entity, Type)` (exact signature per spec); `IrOp_HasComponent` gained an additive `IsManaged = false`
  param (reused, no new Found-op); `IrOp_FieldRead` gained an additive `SourceIsManaged = false` param.
  **Throw-safety finding (see running log below for full detail):** `ISimulationView.
  GetManagedComponentRO<T>` throws unconditionally when the component is absent (confirmed via
  `EntityRepository.View.cs` + the universal Has-then-Get calling convention at every real call site in
  the engine, e.g. `SmartEgressUtil`) — unlike unmanaged `GetComponentRO` (fail-safe outside
  `FDP_PARANOID_MODE`). To preserve the design's "fail-safe, never throw" reads invariant, the emit
  guards the call: `HasManagedComponent<T>(e) ? GetManagedComponentRO<T>(e) : default!`, and
  `IrOp_FieldRead`'s managed variant projects `source?.Field ?? default` (null-safe) instead of a bare
  member access — this is a DELIBERATE deviation from the design doc's literal (unguarded) emit
  snippet; flagged for Opus sign-off. `HasManagedComponent<T>` (public, direct, `T : class`) is used for
  BOTH the guard and "Found" (not the unconstrained `HasComponent<T>`, which also happens to dispatch
  correctly for managed types via reflection, but is slower/less direct — see running log). New
  `EmissionContext.SimulationViewVar` resolves an `ISimulationView`-typed receiver (`view` for Instance;
  `((ISimulationView)world)` for AiPrimitive) since `GetManagedComponentRO` is only reachable through
  the interface (explicit impl), never through the concrete `EntityRepository` (which would need
  `InternalsVisibleTo`, not granted to generated blueprint code). Stage2 `V_ComponentAccessRules` gained
  BP2063: rejects a managed `GetComponentNode` field-out-pin wired into `SetVariableNode`/
  `SetSharedNode` (closes a genuine gap — `V_SharedStateRules` never checks managed-ness at all; BP1503
  only covers `Variables`/`WorkingState`'s own DECLARED type, not link-level wiring) without touching
  `FunctionCallNode` destinations (legitimate managed→managed pass-through stays allowed).
  **Gate:** `Hrot.Blueprints.Compiler` + `Hrot.Blueprints.Editor` + `Hrot.AI.Behaviors` build clean; 138
  Component-prefixed tests green (15 new CA-05 lowering + 6 new BP2063 validator + 9 new editor
  reflector/drawer/palette); clean-rebuilt `Hrot.AiEditor.Generators.Tests` **184/184** byte-identical
  (serial). Full `Hrot.Blueprints.Tests` suite (parallel) reproduces the SAME 9-ish pre-existing reds
  CA-01..CA-04 already characterized (1 Stage4 BP1500, 1 NodeCoverage Make/Break/SetMembers, 2
  perf/allocation thresholds, and a handful of ALC/dynamic-compile tests that fail only under parallel
  contention and pass in isolation) — no new regressions. **Awaiting Opus review of the throw-safety
  guard + BP2063 scope.**

### CA-06 — Managed write (ECB whole-replace) · Slice W2
- [ ] `SetComponentNode` managed path: single whole-value in-pin; `IrOp_SetManagedComponent(fqn, self, val)` **(new)** → `ecb.SetManagedComponent(self, __t{v})` — `IrOperation.cs`, `StatementEmitter.cs`, `Stage5` **(Opus)**
- [ ] Stage2 — **reject per-field managed write** (must whole-replace) — `V_ComponentAccessRules`
- [ ] Editor: managed write drawer = whole-value pin (no field expand)
- [ ] Tests: managed write emit (ECB), per-field-managed rejection
- **Confirm:** ECB API `SetManagedComponent`; AiPrimitive tick has ECB in scope.

### CA-07 — Collections · Slice 2 — **A2 UX + R1 curated-accessor mechanism** (Q17-A decided 2026-08-01; reality-check pivot to R1)

**Mechanism (R1 — curated accessors, after the ⚠ reality-check in the Q#17 doc):** this ECS has NO
`FixedList`/`DynamicBuffer` — only `[InlineArray]`/`fixed` buffers whose logical count is a sibling field and
whose raw access is `unsafe` + architect-ruled off-graph (Q#5-C). So collections are exposed via **curated
accessor pairs**, NOT auto-reflected off the raw buffer:
- A component declares a virtual collection with `[BlueprintCollection]` (`int Count(in T)`) +
  `[BlueprintCollectionItem]` (`TElement Item(in T,int)`) static helpers (`Fdp.Core`), grouped by `(component,name)`.
- `GetComponent` projects **one collection out-pin** per collection — `BlueprintTypeRef { TypeId = elementFqn,
  IsArray = true }` (reuse existing `IsArray`; no new pin kind). **A2 UX:** generic consumers wire off it.
- The collection pin **carries only the entity** at runtime; consumers re-read `GetComponentRO<Comp>(e)` and call
  the **baked accessor FQNs** — identical to `FlowForEach`'s emit, so collection *kind* never reaches the
  compiler. New IR ops; `IrOp_ForEach` untouched (FlowForEach goldens byte-identical).

**CA-07a — collection pin type + GetComponent collection out-pin** *(Sonnet build, Opus review + gate)* ✅
- [x] `[BlueprintCollection]`/`[BlueprintCollectionItem]` marker attrs — `FDP/Engine/Fdp.Core/BlueprintCollectionAttribute.cs`
- [x] Extend `ComponentFieldDecl`: `IsCollection` + `ElementTypeId`/`CountAccessorFqn`/`ItemAccessorFqn` (byte-stable, `JsonIgnore` when default/null) — `Assets/Nodes.cs`
- [x] `ComponentFieldReflector.TryReflectCollections` — scan loaded asms, validate `Count`/`Item` signatures, pair by name, ordinal-sort — `NodeDrawers/`
- [x] `EnrichGetComponentPins` (Stage0) + `GetComponentPins` (editor) — collection decl → ONE `IsArray` element-typed out-pin; `MakePin`/`MakeData` gain `isArray`; strict parity
- [x] Bake collection decls at picker time (`ComponentNodeDrawers.ApplyComponentTypeFqn`; `Fields` non-null even if collections-only)
- [x] Stage5 GetComponent loop **skips** collection decls (`if (f.IsCollection) continue;`)
- [x] Demo: `BpCollectionDemo` (`[ComponentId(189)]`, `int Count` + `fixed int Values[4]`) + `BpCollectionDemoOps` accessor pair
- [x] Tests: parity (incl. `IsArray`), reflector discovery + lone/malformed-accessor, byte-stable JSON. **Gate: 184 serial ✅ + 160 Component ✅**

**CA-07b — consumer nodes + IR + emit** *(Sonnet build, Opus review + hands-on BP2050 fix)* ✅ **DONE** (all items below built; see running log)
- [ ] Nodes (collection in-pin + baked `ComponentTypeFqn`/`CountAccessorFqn`/`ItemAccessorFqn`/`ElementTypeFqn`): `ComponentForEachNode` (exec: In→Body/Completed, `CurrentItem`+`CurrentIndex` outs), `ComponentItemGetNode` (data: `Index` in → `Element` out), `ComponentItemCountNode` (data: `Count` out) — `Assets/Nodes.cs` + `[JsonDerivedType]`
- [ ] New IR ops: `IrOp_ComponentCollectionForEach`, `IrOp_ComponentItemGet`, `IrOp_ComponentItemCount` — `Compiler/Ir/IrOperation.cs`
- [ ] Stage5 lowering (ForEach body inline, mirror `ScheduleFlowForEachNode`; collection in-pin resolves to the entity) — `Stage5_Schedule.cs`
- [ ] StatementEmitter: read `GetComponentRO<Comp>(e)` once, call `global::{CountFqn}(in c)` / `global::{ItemFqn}(in c,i)` (mirror `IrOp_ForEach` emit) — guard nested-ops-class `+` in FQN — `Emit/StatementEmitter.cs`
- [ ] Stage2 validators (collection-pin in-pin required; element-type flow) + BP206x codes
- [ ] `ArrayGetNode` silent-default stub: leave as-is + document (superseded by `ComponentItemGet`)
- [ ] Tests: iterate + index + length lowering + emit goldens. **Gate 184 + new goldens.**

**CA-07c — editor wire-baking + palette + drawers** *(Sonnet mechanical mirror; Opus reviews wire-bake hook)*
- [ ] `BlueprintCommandSink.ApplyAddLink` hook: collection out-pin → consumer in-pin bakes `(ComponentFqn, Count/Item FQNs, ElementFqn)` from the source GetComponent collection decl onto the consumer, then `RebuildAndNotify` (mirror `ApplyComponentTypeFqn`) **(Opus reviews)**
- [ ] Palette entries + drawers for the 3 consumer nodes
- [ ] Demo blueprint(s) using GetComponent<BpCollectionDemo> "Values" → ForEach/Get/Length (visual check)
- [ ] Tests: wire-bake round-trip. **Gate 184.**

**CA-07d — deferred sub-slice** *(after visual check)*
- [ ] Managed collections via direct C# `foreach`/indexer under the managed read-and-pass rules
- [ ] `Contains` / `Find`

- **Note:** no unmanaged maps/sets (Q#15-E1). Element = value copy → struct element decomposed with `Break` (shipped).

---

## Running log
*(append per batch: date, who, gate result, notes)*
- _pending kickoff_
- **2026-08-01, CA-01, Sonnet:** built the read compiler spine (unmanaged) — `ComponentFieldDecl`
  (Nodes.cs), `Stage0_Rehydrate.EnrichGetComponentPins` (mirrors `EnrichGetSharedPins`; legacy
  "Value" pin's TypeId is used VERBATIM from `FieldTypeFqn`, deliberately NOT "global::"-stamped —
  stamping would misroute well-known primitives like `System.Single` into `StaticTypeRegistry`'s
  AN2 enum/project-type path), and the `Stage5_Schedule` `GetComponentNode` multi-field branch
  (one `IrOp_GetComponentRO` + N×`IrOp_FieldRead` + `IrOp_HasComponent` for `Found`; legacy
  single-field path left byte-identical, reached only via early-`break` skip). Also removed the
  now-stale `GetComponentNode` entry from `NodeCoverageTests.PinlessRoundTripExceptions` (Guard 4)
  since pin-less rehydration now genuinely reconstructs its pins. Gate: `Hrot.Blueprints.Compiler` +
  `Hrot.AI.Behaviors` build clean; 9 new/updated CA-01 tests green; `Hrot.AiEditor.Generators.Tests`
  184/184 byte-identical (serial). **Known gap flagged for Opus review:** the NEW `Found` out-pin
  Stage0 now projects on LEGACY (`Fields == null`) `GetComponentNode`s has no Stage5 computation in
  that branch (kept byte-identical per the batch spec) — if a legacy single-field node's `Found` pin
  is ever wired, `ResolveNodeOutput` will return the Value field's result instead of a real
  `HasComponent` check. Not exercised by any existing asset (the pin didn't exist before this batch),
  but worth a deliberate call before CA-02 exposes it in the editor.
- **2026-08-01, CA-01, Opus review:** reviewed the diff; **ruled on the flagged gap — froze the legacy
  single-field shape.** `EnrichGetComponentPins` now projects `Target`/`Found` **only in multi-pin mode
  (`Fields` baked)**; the legacy (`Fields == null`) branch projects a single `Value` out — self-only, no
  `Target`/`Found` — matching the untouched Stage5 legacy lowering, so no projected pin is ever left
  uncomputed. New capability (self+Target, Found) lives entirely in multi-pin mode; CA-02's editor
  always creates Fields-baked nodes. Updated the legacy-shape test accordingly. Verified the 4
  full-suite reds (Stage4 BP1500, NodeCoverage Make/Break/SetMembers, 2 perf) reproduce on the clean
  base → pre-existing, not CA-01. Re-gate: 7/7 GetComponent tests + **184/184 serial** byte-identical.
  **CA-01 ✅ done.**
- **2026-08-02, CA-07a, Sonnet build + Opus review:** R1 curated-accessor collection reads. Added
  `[BlueprintCollection]`/`[BlueprintCollectionItem]` (Fdp.Core), extended `ComponentFieldDecl`
  (`IsCollection`/`ElementTypeId`/`CountAccessorFqn`/`ItemAccessorFqn`, byte-stable), reflector
  `TryReflectCollections` (signature-validated pair discovery, ordinal-sorted), picker bake,
  `GetComponent` one `IsArray` element-typed out-pin per collection (Stage0 ⇄ NodePinSchema parity,
  `MakePin`/`MakeData` gain `isArray`), Stage5 skips collection decls, demo `BpCollectionDemo`
  (ComponentId 189) + `BpCollectionDemoOps`. **Opus review:** parity exact, byte-stability correct,
  Stage5 skip right, reflector validation sound; noted `AccessorFqn` uses `DeclaringType.FullName`
  (guard nested-ops-class `+` at CA-07b emit). Re-ran gate MYSELF: **184/184 serial byte-identical**
  + **160/160 Component**, both builds clean. **CA-07a ✅ done.**
- **2026-08-02, CA-07b, Sonnet build + Opus review/fix:** the three collection CONSUMER nodes
  (`ComponentForEachNode`/`ComponentItemGetNode`/`ComponentItemCountNode`) + IR + emit. Design
  collapsed nicely: `ComponentForEach` REUSES `IrOp_ForEach` + `IrOp_GetComponentRO` unchanged
  (only the entity source differs — resolved from the "Collection" in-pin, not `IrOp_Self`), and
  Get[i]/Length need just ONE new tiny op `IrOp_ComponentAccessorCall` → `global::{Fqn}(comp[,i])`
  (same call shape `IrOp_ForEach` already emits; component binds `in T` via the `ref readonly`
  local). Changed CA-07a's Stage5 skip so the GetComponent collection out-pin caches → `entityValue`
  (that's what lets consumers re-read the component off the source entity). Stage0 enrichers +
  `NodePinSchema` twins (element-typed pins, exact parity) + `BuiltInNodeRegistry` fallbacks. BP2066
  (wired Collection + empty baked FQNs). **Opus hands-on fix:** the agent flagged that
  `V_FlowForEachRules` (BP2050, latent-free body) only matched `FlowForEachNode` — a REAL hole,
  since `ScheduleComponentForEachNode` uses the same inline for-body scheduling; generalized the
  validator to walk `ComponentForEach` bodies + added a test. Safe defaults: unwired/unbaked →
  `default` (Get/Count) or empty loop (ForEach). **Opus review:** lowerings faithful to
  `ScheduleFlowForEachNode`; entity-cache change correct; parity exact. Re-ran gate MYSELF after the
  BP2050 fix: **184/184 serial byte-identical** + **177/177 Component+FlowForEach**. **CA-07b ✅.**
- **2026-08-02, CA-07c, Sonnet build + Opus review/fixes:** editor wire-baking + palette + titles +
  demo. `BlueprintCommandSink.TryBakeCollectionConsumer` (in `ApplyAddLink`, outside history like
  `ApplyComponentTypeFqn`): wiring a GetComponent collection out-pin → a consumer's "Collection" pin
  bakes `ComponentTypeFqn` + the decl's accessor FQNs + `ElementTypeFqn` onto the consumer (per-kind
  switch); `RebuildAndNotify` re-projects the now-element-typed pins. Palette entries
  (`ComponentPaletteEntries.ConsumerEntries`), titles (`For Each [T]`/`Get Item [T]`/`Item Count [T]`),
  NodeCategory, stale-ref Error + BP2066-mirroring "wired-but-unbaked" Error (`collectionPinWired`
  threaded from `BlueprintGraphModel`). Demo `ComponentCollectionDemo.bp.json` (GetComponent<BpCollectionDemo>
  "Values" → all three consumers; compiles clean via the generator).
  **Companion fixes (both reviewed by Opus):** (1) a REAL latent COMPILER bug — `Stage4_TypeResolve.
  VerifyLinkTypes`'s `System.Object` wildcard never stripped the `[]` array suffix, so `Int32[] →
  Object[]` (ItemCount's `System.Object[]` Collection pin) wrongly failed BP1501; fixed with
  `WildcardFullName` (narrow — `Int32[]→String[]` still fails, +tests). (2) editor
  `BlueprintTypeSystem.AreCompatible` mirror of the same wildcard so the first wire is accepted.
  **Opus-caught bug (broad re-gate):** the agent's `~Component` filter never ran `BlueprintTypeSystemTests`,
  which red-flagged its own too-broad `AreCompatible` (exec-vs-`System.Object` wrongly compatible) —
  Opus tightened the wildcard to require the other side be a real data type (non-empty Id). Re-gate
  MYSELF: **184/184 serial** + **399/400** broad (Component/Stage4/TypeSystem/FlowForEach/CommandSink/
  Palette/Title), sole red = pre-existing `TypeResolve_UnknownFieldType_EmitsBP1500` (BP1500, ignore-list).
  **CA-07c ✅ — the feature is now wireable in the editor; ready for the user's visual check.**
- **2026-08-02, CA-07c follow-up (visual feedback), Opus:** user visual check — GetComponent/
  SetComponent/ComponentForEach look good (picker, titles, diamond array-pin, ForEach title-on-connect
  all work). Bug on `ComponentItemGet`: its unconnected `Collection` (array) input pin rendered a
  spurious scalar inline value-box (drawn AFTER pin glyphs → on top), which occluded the `Element`
  output pin and appeared as a widget on the right/outside. Root cause: `BlueprintPinModel` builds the
  canvas `TypeKey` from the element TypeId only (drops `IsArray`), so `GetDefaultEditor` handed an
  array input pin the element's scalar editor. Fix: `BlueprintPinModel` no longer synthesizes a
  `Default` for array pins (`!pin.TypeRef.IsArray`) — array pins are wire-only. +regression test
  (`BlueprintPinDefaultZeroTests.PinModel_WithRegistry_ArrayInputPin_DefaultIsNull`). Editor builds
  clean; 17 pin-model + 293 pin/typesystem/component tests green. **Awaiting user re-check of the
  ItemGet node layout.**
- **2026-08-02, CA-07c follow-up #2 (visual feedback), Opus:** user's precise repro — dragging FROM
  GetItem's "Collection" input pin and dropping on GetComponent's "Values" output pin connected the
  link but the Collection pin "moved to the right" (worked fine dragging the other direction). Root
  cause (PRE-EXISTING, GENERAL — in the shared NodeEdit lib): `CanvasInput`'s pending-wire drop-on-PIN
  path called `cb.AddLink(SourcePin, CandidateTarget)` WITHOUT normalizing by pin direction, so
  dragging from an input pin stored a backwards link (From=input, To=output); the slow-path pin-GUID
  binding (fresh node, empty Pins) then mis-assigned that outgoing link's FromPinId to an OUTPUT pin
  → the input pin rendered on the right. The drop-on-NODE path already normalized; the drop-on-PIN
  path did not. Fix: normalize orientation to output(From)->input(To) in the drop-on-pin path
  (`NodeEditor.UI/Canvas/CanvasInput.cs`), mirroring the drop-on-node path. Also makes the CA-07c
  wire-bake fire regardless of drag direction (bake keys on toPin=="Collection"). Affects ALL node
  editors (Blueprint/BTree/HSM), fixing a latent drag-from-input bug on any fresh node. Gate:
  NodeEditor.UI.Tests 90/90; Blueprint wire/command-sink/component 237/237. **Awaiting user re-check.**
  (The earlier array-pin inline-editor fix `98463ac9` stands — a real but DIFFERENT bug.)
- **2026-08-02, CA-07c follow-up #3 (compile error), Opus:** user hit `BP1601 duplicate pin Id
  ...022` on the GetComponent node when compiling. Cause: the committed `ComponentCollectionDemo.bp.json`
  was an EDITOR-SAVED corruption (my earlier `git add -A` swept up the user's in-editor changes) — all
  node `Pins` stripped to `[]`, a stray extra `ComponentItemGet` node added, and a BACKWARDS link whose
  `ToPinId` was GetComponent's `Values` OUTPUT pin (...022). That put ...022 in both the outgoing and
  incoming buckets, so `Stage0.AssignLinkGuids` minted two pins with id ...022 → BP1601. (The backwards
  link is exactly the pre-fix drag-from-input bug now fixed by fc1222e5.) Fix: rewrote the demo to the
  correct self-consistent EXPLICIT-PINS form (Stage0 skips authored-pin nodes → compiles reliably),
  dropping the stray node + backwards link. Verified: `Hrot.AI.Behaviors` builds — **0 errors, no BP1601**.
  Lesson: use targeted `git add <path>` (not `-A`) while the user has the editor open, so editor-saved
  assets aren't swept into commits.
- **2026-08-02, CA-07c follow-up #4 (compile error) + demo removal, Opus:** user hit `CS8716` (untyped
  `default`) then, after a partial fix, `CS0266` (object->int) in the demo's generated code. Root: the
  editor REWRITES the asset on open (lossy DTO round-trip) — strips all `Pins` to `[]`, adds
  `FieldName`/`FieldTypeFqn`, adds a stray unwired `ComponentItemGet`. With pins stripped, the demo's
  sequential-GUID links dangle → scrambled wiring → an unwired consumer hit my CA-07b safe-default
  (`IrOp_Const("default", pinType)`) which emitted a BARE `default` (CS8716); typing it surfaced the
  real object->int mismatch from the scrambled graph (CS0266). Two actions: (1) **emit fix** — the
  `IrOp_Const` "default" literal now emits a TYPED `default(global::T)` (unknown type -> `object`); no
  valid graph should ever emit bare `default`. Serial **184/184** byte-identical (the "default" literal
  is exclusively CA-07b's safe-default, so existing goldens unchanged). (2) **removed
  ComponentCollectionDemo.bp.json** — the editor's save-on-open kept corrupting it into an
  un-compilable mis-wired state, and its visual-check job is DONE (user confirmed the wire-orientation
  fix works). The three consumer nodes stay covered by in-code fixtures (`ComponentCollectionConsumerLoweringTests`).
  `Hrot.AI.Behaviors` builds clean. **REAL underlying bug flagged: the editor rewrites blueprint assets
  on open (project_blueprint_editor_writes_on_open) — strips pins + adds DTO artifacts — which corrupts
  ANY opened blueprint, not just this demo. Needs its own investigation.**
