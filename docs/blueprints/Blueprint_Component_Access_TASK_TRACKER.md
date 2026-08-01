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
| CA-03 | Write compiler spine (unmanaged) | W1 | Sonnet + Opus(IR/lowering/emit/validator) | ⬜ |
| CA-04 | Write editor (unmanaged) | W1 | Sonnet | ⬜ |
| CA-05 | Managed read | 1b | Sonnet + Opus(flow rules) | ⬜ |
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
- [ ] `[BlueprintWritable]` attribute (confirm assembly — co-locate w/ component contracts) + `SetComponentNode` (`ComponentTypeFqn`, `Fields`, `IsManaged`) + JsonDerivedType `"SetComponent"` — `Assets/Nodes.cs`
- [ ] `IrOp_WriteComponentField(target, name, val)` **(new)** + reuse `IrOp_GetComponent`(RW)/`IrOp_HasComponent` — `Compiler/Ir/IrOperation.cs` **(Opus)**
- [ ] Stage0 `EnrichSetComponentPins` — exec In/Out, per-field data-ins (unmanaged), `Written` out — `Stage0_Rehydrate.cs`
- [ ] Stage2 `V_ComponentAccessRules` — writable-set (`[BlueprintWritable]`), self-only (reject `Target`), well-formed FQN (BP206x) — `Stage2_Validate.cs` **(Opus reviews)**
- [ ] Stage5 write lowering — `HasComponent` guard + `IrOp_GetComponent`(RW) + N× `IrOp_WriteComponentField` (wired-only) — `Stage5_Schedule.cs` **(Opus)**
- [ ] `StatementEmitter` — `IrOp_WriteComponentField` → `__c.{name} = __t{v};` — `Compiler/Emit/StatementEmitter.cs` **(Opus reviews)**
- [ ] Tests: write lowering/emit (write-if-present, wired-only), validator (non-writable + Target rejected)
- **Reuse:** `IrOp_WriteSharedField` shape, `ChannelCommandLowering` `GetComponentRW(self)` emit, `V_SharedStateRules`.

### CA-04 — Write editor (unmanaged) · Slice W1
- [ ] `SetComponentPaletteEntries` — reflect **`[BlueprintWritable]`** types only — `NodeDrawers/`
- [ ] `NodePinSchema.SetComponentPins` — parity with Stage0 `EnrichSetComponentPins` — `Host/NodePinSchema.cs`
- [ ] `ComponentNodeDrawers` (Set) — writable-set picker + field-expand — `NodeDrawers/`
- [ ] `BlueprintNodeModel` — title `Set Component [T]` + stale-ref error
- [ ] Editor tests: writable-only discovery, pin parity, title
- **Reuse:** CA-02 infra + `SetSharedNodeDrawer`/`SetSharedPins`.

### CA-05 — Managed read · Slice 1b
- [ ] `GetComponentNode.IsManaged` baked by editor reflector; `IrOp_GetManagedComponentRO` **(new)** → `view.GetManagedComponentRO<T>` — `IrOperation.cs`, `StatementEmitter.cs`, `Stage5` **(Opus reviews)**
- [ ] Managed fields exposed in the read picker with the **persistence caveat** UI — `ComponentNodeDrawers`
- [ ] Stage2 — reject **managed→unmanaged** wiring; uphold BP1503 (no persist) — `V_ComponentAccessRules`
- [ ] Tests: managed read emit (`GetManagedComponentRO`), managed→unmanaged rejection, no-persist
- **Confirm:** view API `GetManagedComponentRO<T>`.

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
