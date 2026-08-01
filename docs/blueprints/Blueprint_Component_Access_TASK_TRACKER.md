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
| CA-06 | Managed write (ECB) | W2 | Opus + Sonnet(mirror) | ⬜ |
| CA-07 | Collections (iterate + random) | 2 | Opus-led | ⬜ |

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

### CA-07 — Collections (iterate + random-access) · Slice 2
- [ ] Generalize `FlowForEach` baked `Count`/`Item[i]` accessors across `FixedList<T>` / `[InlineArray]` / `DynamicBuffer<T>` — iteration
- [ ] Random-access read: `component.array[i]` + `Length` (build on baked accessors; retire/replace the `ArrayGet`/`ArrayMake` stubs) **(Opus-led)**
- [ ] Managed collections via direct C# `foreach`/indexer under the managed read-and-pass rules
- [ ] Tests: per-kind iterate + index + length
- **Note:** no unmanaged maps/sets (E1). Largest batch — may split when scoped.

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
