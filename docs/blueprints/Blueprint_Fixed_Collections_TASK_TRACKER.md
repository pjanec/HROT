# Blueprint Fixed Collections — TASK TRACKER

Execution tracker for the Fixed Collections capability (umbrella: `Blueprint_Fixed_Collections_Design.md`;
component-write rulings: `Architect_Question_20_Component_Collection_Write.md` ✅ APPROVED; blueprint-variable
home: `Blueprint_List_Variables_Design.md` + Q#19). Branch: `claude/reset-from-blueprint-1t6cq8`.

**Execution policy:** mirror the CA tracker — mechanical/mirror-pattern batches delegable; the novel IR-op +
Stage5 lowering + emit + validator work done and reviewed hands-on. **Gate (every batch):** clean rebuild →
`Hrot.AiEditor.Generators.Tests` SERIAL **184/184 byte-identical** → the batch's new tests green.
**Legend:** ⬜ not started · 🔧 in progress · ✅ done (gate passed).

## Batches

| # | Batch | Scope | Status |
|---|-------|-------|--------|
| FC-0 | Runtime foundation: write-accessor convention + reference impls + probe hook + gates | component home | ✅ |
| FC-1·C | Write compiler spine: node + Stage0 pins + **IR op family** + Stage5 + emit + Stage2 gates (G3/G4/managed/BP-writable-structural) | component home | ✅ |
| FC-1·E | Write editor: reflector write-accessor discovery, palette (two-gate filter), drawer, wire-bake, demo bp | component home | ✅ |
| FC-1·G2 | Tick-order: fix the `bpTick` splice (preferred) or pin 1-tick lag + composition-order test | composition | ✅ |
| FC-1b | `[BlueprintCollectionField]` Roslyn source generator emitting the ops class from the FC-0 template | tooling | ✅ |
| FC-2 | Blueprint-variable collection (List Variables LV-1…LV-5; independent of FC-1) | blackboard home | ⬜ |
| FC-3 | Action-DTO recognition pipeline + F2 safety + JSON converter + inspector marshal | action home | ⬜ |

> **Resequencing note (2026-08-04):** the design docs placed "the mutation-op IR family" in FC-0. It is
> tracked under **FC-1·C** instead: the codebase's only idiomatic test path for IR ops runs Stage3→7 through a
> real node (the CA-03 precedent bundles node+IR+lowering+emit+tests in one compiler-spine batch), and an IR op
> with a bespoke hand-rolled emit harness would add risk without coverage value. FC-0 stayed the pure-runtime
> foundation.

---

### FC-0 — Runtime foundation · ✅ (2026-08-04)

- [x] `BlueprintCollectionOp` enum + `[BlueprintCollectionWrite]` attribute (pinned `ref C` signatures,
  presence-is-the-per-field-gate) — `FDP/Engine/Fdp.Core/BlueprintCollectionWriteAttribute.cs`
- [x] `BpFixedListDemo` — the `[InlineArray]`-backed `[BlueprintWritable]` demo component (G7: the `fixed`-buffer
  demo cannot exercise the InlineArray write path) — `Hrot.AI.Behaviors/Components/BpFixedListDemo.cs`,
  `[ComponentId(191)]`
- [x] `BpFixedListDemoOps` — the REFERENCE ops class (read pair + all 6 write ops; `Span<T>` write-through;
  G6 tail-always-default; F2 defensive Count clamp) = the FC-1b generator template —
  `Hrot.AI.Behaviors/Brains/BpFixedListDemoOps.cs`
- [x] `BpCollectionDemoOps` write set — the raw-`fixed`-buffer idiom reference (same contract; also the
  gate-1-vs-gate-2 discovery case: write accessors present, component NOT `[BlueprintWritable]`)
- [x] DebugProbe overflow hook — `IBlueprintProbeSink.OnCollectionWriteFailed` (default-implemented, no
  implementer breakage) + `DebugProbe.CollectionWriteFailed` (`nodeId`/`op`/`reason`; reasons:
  `component-absent` · `op-rejected`)
- [x] Tests (14, `Runtime/FixedCollectionOpsTests.cs`): round-trip gate through `GetComponentRW` ref ·
  G6 invariant after RemoveAt/Clear/Resize-shrink + grow-needs-no-fill · overflow/bounds contract ·
  F2 garbage-Count clamp · both buffer idioms · probe routing/no-op ·
  **compiler-behavior pins** (see finding below)
- **Gate:** clean build · 14/14 new tests green · Generators 184/184 serial byte-identical.

**⚠ FC-0 empirical finding — the documented InlineArray "silent mutation loss" does NOT reproduce.**
Measured on .NET SDK 8.0.4xx (test-pinned): `ref var c = ref GetComponentRW<T>(e); c.Items[0] = x;` **lands**
— through a ref local and through a ref-returning receiver alike. The only reproducible loss mode is the
missing-`ref` **value copy** (`var c = GetComponentRW<T>(e); …`), which loses scalar writes identically and is
not InlineArray-specific. `EntityRepository.GetComponentRW`'s "ldobj → temp → lost" warning appears stale (or
described an older-toolchain bug). Consequences: the accessor + `Span<T>` convention **stays mandated** (Q#5-C
off-graph rule, readonly-read defensive copies, value-copy hazard buried inside a `ref`-receiver method,
generator uniformity) — but its justification is curation + the value-copy hazard, not the indexer trap. Two
tests pin the measured behavior so any future compiler change fails loudly
(`NaiveRefLocalWrite_CurrentToolchain_Lands` / `ValueCopyWrite_IsLost`).

### FC-1·C — Write compiler spine · ✅ (2026-08-04)
- [x] `CollectionWriteNode` (Assets, discriminator "CollectionWrite") — baked `ComponentTypeFqn` ·
  `Op: CollectionWriteOp` (asset-side mirror of `Fdp.Core.BlueprintCollectionOp`) · `WriteAccessorFqn` ·
  `ElementTypeFqn` · `CollectionKind` (CuratedStatic only); collection in-pin (G4 UX; never the write entity) +
  per-op operand pins ("Index"/"Length"/"Value") + unconditional `Ok` out (= present AND applied)
- [x] Stage0 `EnrichCollectionWritePins` + registry exec-skeleton entry
- [x] `IrOp_CollectionWrite` + Stage5 case (always-allocated Ok; wired-only operand resolution; unwired/unbaked/
  managed/missing-operand degrade to const `Ok=false`; entity = unconditional `IrOp_Self`) + StatementEmitter
  case (guarded `HasComponent` → `GetComponentRW` ref → `global::{WriteAccessorFqn}(ref __wc[, i][, v])`;
  Clear keeps the guard bool; `DebugProbe.CollectionWriteFailed` on op-rejected/component-absent, gated
  non-Release + self-in-scope exactly like the other probe ops — satisfying Q#19's "diagnostic in Debug/Trace,
  no-op in Release" contract)
- [x] Stage2 (`V_ComponentAccessRules` extension): **BP2067** wired-but-unbaked/malformed · **BP2068**
  ManagedMember write forbidden (Q#20-C) · **BP2069** "Target" pin (self-only) · **BP2070** producer
  GetComponent `Target` wired (G4 cross-entity) · **BP2071 warning** write-inside-ForEach-body over the same
  collection (G3; "same collection" = ComponentTypeFqn + accessor owner class; exec-BFS from the Body wire)
- [x] Tests (20): `CollectionWriteLoweringTests` (guarded accessor shape, per-op arity, Clear void shape,
  **self-bound even with a cross-entity producer** (Stage3-7, skipping Stage2), Release no-probes, three
  degrade paths) + `V_CollectionWriteValidatorTests` (BP2067-71 fire + negative cases, `[CoversDiagnosticCode]`
  for the coverage gate)
- **Gate:** clean build · 20/20 new tests · full Blueprints suite green · Generators 184/184 serial byte-identical.

### FC-1·E — Write editor · ✅ (2026-08-04)
- [x] `ComponentFieldReflector`: `IsWritableComponent` (gate 1 — `[BlueprintWritable]`) +
  `TryReflectWriteAccessors(componentFqn, name)` (gate 2 — `[BlueprintCollectionWrite]` statics keyed by the
  asset-side `CollectionWriteOp`, per-op signature validation incl. writable-`ref`-receiver; partial sets legal)
- [x] `ComponentPaletteEntries.CollectionWriteEntries` — six static no-picker entries
  ("Add/Set At/Insert At/Remove At/Clear/Resize (Collection)") + bootstrap registration
- [x] `NodePinSchema.CollectionWritePins` — exact Stage0 parity (per-op operand pins, System.Object fallback)
- [x] `BlueprintCommandSink.TryBakeCollectionConsumer` extended: bakes
  ComponentTypeFqn/WriteAccessorFqn/ElementTypeFqn only when gate 0 (not ManagedMember, Q#20-C) + gate 1 +
  gate 2 (op accessor exists) all pass; refused wire stays unbaked → canvas error + BP2067
- [x] `BlueprintNodeModel`: verb titles ("Set At [BpFixedListDemo]" / "Set At (Collection)" unbaked),
  bake-incomplete + stale-component `NodeState.Error`, `VariableSet` category; `BlueprintGraphModel`
  wired-flag extended
- [x] Tests (18): per-op pin parity vs Stage0 + fallback · write-accessor discovery on BOTH FC-0 reference ops
  classes · gate-1 check (BpCollectionDemo: accessors present, not writable) · sink bake + two refusal cases
  (non-writable, ManagedMember) · title/state model cases
- **Not done (deliberate):** no Details-panel drawer (consumers precedent — nothing to pick, wire-bake only);
  demo `.bp.json` deferred to the FC-1 wrap-up alongside a runtime end-to-end proof.
- **Gate:** clean build · 38/38 CollectionWrite tests · full suite failures unchanged from base · 184/184 goldens.

### FC-1·G2 — Tick-order splice fix · ✅ (2026-08-04)
**Chose FIX over document-the-lag** (the lag was an accident of composition, not design intent; Q#16's shipped
scalar writes were approved on the same-tick fact too — this restores their contract retroactively).
- [x] `BlueprintRuntimeWiring.SpliceIntoSimulation(sims, bpTick)` — inserts the tick immediately BEFORE the
  first system named by its own `[UpdateBefore]` attributes (attribute-driven, not hardcoded); appends when no
  target present (degenerate compositions keep old behavior). Single shared splice.
- [x] Both composition sites converted: `EditorSubsystem` (was `.Append(bpTick)` after the dispatchers) +
  `EditorHarness` (was list-append) — comments updated.
- [x] Tests (3, `BlueprintTickSpliceTests`): insert-before-first-dispatcher with order preservation ·
  no-dispatcher append fallback · the `[UpdateBefore]` target set pinned to exactly the three dispatchers.
- **Verified not-a-regression:** the ClusterRunner integration suite's failures are PRE-EXISTING on the branch
  base (stash-baseline: identical 8/8 failure set incl. `BlueprintKernelRunTests` before the splice change —
  environmental in this container: DDS transport / raycast dependencies). No new failures introduced.
- **Contract now TRUE by construction:** blueprint intent writes are dispatched the SAME tick (Q#16-B as the
  architect approved it); the "write-visible-next-tick" reality documented in G2 is retired for these two
  compositions.

### FC-1b — Collection ops source generator · ✅ (2026-08-04)
- [x] `[BlueprintCollectionField(nameof(Count))]` + `CollectionAccess` + `CollectionOps` flags (Fdp.Core) —
  the dev-facing surface is ONE attribute; opt-in per field (never auto-triggered), `Access=ReadOnly` /
  `Ops=` subset are the declarative curation knobs
- [x] `CollectionOpsGenerator` (in `Fdp.Toolkits.Analyzers` — the repo's existing generator home, already
  wired as an analyzer into `Hrot.AI.Behaviors`): emits `{Component}{Field}Ops` in the component's namespace
  from the FC-0 template (Span write-through · G6 tail-always-default · F2 clamp); **per-field class naming**
  so adding a second collection later never renames an already-baked FQN
- [x] Diagnostics FCOL001-004 (count field missing/not-int · not `[InlineArray]` · managed element ·
  class/managed component per Q#20-C); **hand-written wins** — any `[BlueprintCollection*]` accessor for the
  same (component, name) in the compilation silently suppresses generation (bespoke escape hatch)
- [x] `BpGenListDemo` (`[ComponentId(192)]`, `[BlueprintWritable]`) — the generated-ops demo; its ONLY
  authoring surface is the attribute
- [x] Tests: 9 generator-driver tests (`Fdp.Toolkits.Tests/FixedCollections/`: emit shape incl. the three
  template rules, compiles-against-real-BCL, ReadOnly/Ops knobs, all four diagnostics, hand-written-wins both
  ways) + 4 end-to-end (`GeneratedCollectionOpsTests`: the FC-0 round-trip/G6/overflow/F2 gates against the
  REAL generated `BpGenListDemoItemsOps`, and editor discovery finding read pair + all 6 write ops +
  gate 1 — generated is indistinguishable from hand-written)
- **Note:** the doc's provisional `{Component}CollectionOps` name became `{Component}{Field}Ops` — baked FQNs
  must never change when a second collection field is added later (assets persist accessor FQNs).
- **Gate:** clean builds (analyzers + AI.Behaviors with generator active) · 13/13 new tests · full suite
  failure set unchanged · goldens 184/184.

### FC-2 — Blueprint-variable collection (List Variables LV-1…LV-6) · 🔧 (started 2026-08-04)

Design: `Blueprint_List_Variables_Design.md` (+ its Review deltas F1–F8 and "Revised sequencing"). Pre-existing
suite failures were fixed first (suite fully green at FC-2 start: 2483/0).

**LV-1a — Instance-side foundation · ✅ (2026-08-04).** Shipped: `BlueprintTypeRef.Capacity/InitialLength`
(JSON-additive) · `IrTypeRef.Capacity/InitialLength` · `StaticTypeRegistry` list branch (unmanaged element only,
computed size, `SizeReliable=false`, `__List_{Elem}_{N}` FullName) · `InstanceEmitter.EmitListWrappers`
(per-class nested `[InlineArray]` buffer + wrapper, deduped per (elem,N)) + State field + `InitDefault`
`Count = L` seeding · BP1504 (InitialLength ∉ [0,Capacity]) · **plus a latent-hazard fix found while testing:**
`CSharpEmitter.layoutFromRuntime` only scanned descriptor-VISIBLE fields, but a synthesized `__List_…` field is
descriptor-excluded while still occupying unreliable bytes — a scalar declared AFTER a list kept its wrong baked
offset; the scan now covers ALL variables. 7 tests (`ListVariableFoundationTests`): resolve sizes incl.
non-8-aligned element · managed-element rejection · BP1504 · emitted wrapper/seed/runtime-layout assertions ·
full Roslyn+ALC `Marshal.OffsetOf`/`SizeOf` round-trip + `InitDefault` seed via Span delegate (the F3 gate).
Suite 2490/0 · goldens 184/184.

**LV-1 remaining notes.** Scouted facts (verified in code, 2026-08-04):
- `BlueprintTypeRef` (`Assets/Declarations.cs:40`): gains `Capacity` + `InitialLength` (int, JSON-ignored when 0;
  discriminator is `Capacity`, NOT `IsArray` — F7).
- `IrTypeRef`: gains `Capacity` (+ carry `InitialLength`); element rides the existing `ElementType`.
- `StaticTypeRegistry.TryResolve` (`:106`): new `Capacity > 0` branch BEFORE the IsArray branch — resolve element
  as scalar, require unmanaged + element.Capacity==0 (nested lists forbidden), size =
  `alignUp(alignUp(4, elemAlign) + N*elemSize, max(4, elemAlign))`, `SizeReliable=false` (F3),
  `FullName = "__List_{San(elemFullName)}_{N}"` ('.'/'+'→'_').
- `TypeRefToCSharp` (`StatementEmitter.cs:1430`) already routes `_`-prefixed FullNames as local generated types
  (bare name) — the per-class nested wrapper (F4) needs NO emit change for State-internal use. ⚠ PITFALL: the
  `StateFields` descriptor emission (`CSharpEmitter.cs:356-369`) writes `typeof({csharpType})` in the REGISTRATION
  context — a nested wrapper must be qualified `{className}.__List_…` there (check whether registration is inside
  the class; `typeof({className}.State)` at `:346` suggests qualification IS needed).
- Wrapper emit site: `InstanceEmitter.EmitStateStruct` (`:102`) — emit deduped per-(elem,N) nested
  `[InlineArray(N)] __Buf_{San}_{N}` + `[StructLayout(Sequential)] __List_{San}_{N} { public int Count; public __Buf… Items; }`
  before `struct State`; State field emission (`:110`) works unchanged via `CSharpType`.
- InitialLength seeding: `InstanceEmitter.EmitInitDefault` (`:126`) — after `s = default;`, emit
  `s.{Name}.Count = L;` for list vars with `InitialLength > 0`.
- Layout: `FieldLayout` (`Lowering/FieldLayout.cs`) keys alignment off SizeBytes (heuristic, over-pads) — fine
  because `SizeReliable=false` already flips `CSharpEmitter.cs:356` `layoutFromRuntime` → `Marshal.OffsetOf` +
  `Unsafe.SizeOf` (the F3 safety net; NO new mechanism needed).
- Diagnostics: new `BP1504` (InitialLength out of `[0, Capacity]` / Capacity < 0) in Stage4's
  `ResolveFieldTypes(VariableDecl)`; element-managed simply fails resolve → existing BP1500. The
  forbid-list-on-generic-pins / cross-boundary / whole-list-clone gates need the LV-2/3 pin surfaces — deferred
  there (recorded, not dropped).
- **Scope split:** LV-1a = Instance `State` path (InitDefault always runs — F2-safe). LV-1b = AiPrimitive
  `WorkingState` (AiPrimitiveEmitter) + the F2 init-on-all-attach-paths engine fix
  (`BehaviorIngressSystem.AttachSlotsToMemory` + `InlineActionLowering`) — do NOT ship WorkingState lists before
  that fix (garbage-Count OOB hazard; the defensive clamp lands with the LV-2/3 accessors).
- Gate: registry unit tests (size/align incl. non-8-aligned element, e.g. short×3 → 12B) + full Roslyn
  compile+load of a list-var asset + emitted-source assertions (wrapper, State field, `Count = L` seed,
  `Marshal.OffsetOf` fallback in StateFields) + 184/184 goldens.

**LV-1b — WorkingState + F2 fix · ✅ (2026-08-04).** `TryAttach` zero-on-attach (the allocator choke point —
free-list reuse handed stale bytes; poison-byte tests pin fresh + reused paths) · AiPrimitiveEmitter WS list
wrappers + `InitDefaultWorkingState` Count seeding (BlueprintCall inline-zero path documented Count=0-safe).

**LV-2 — read path · ✅ (2026-08-04).** The A1 UX delivered: `CollectionKind.BlackboardFixedList` ·
`IrOp_StateFieldRef` (writable `ref` onto `s.{field}`; no entity, no component re-read) · Capacity threaded
through `IrOp_ForEach`/`IrOp_ComponentAccessorCall`/`IrOp_ComponentCollectionSearch` · emit renders the F2
clamp `Math.Min(ref.Count, N)` everywhere + guarded never-throw ItemGet + ForEach bound ALWAYS snapshotted at
entry (decided read binding) · Stage5 producer-driven list-source branches in all 5 consumers (wire is source
of truth; bake serves gates) · Stage0 + editor `GetVariablePins` collection out-pin projection (parity-tested) ·
wire-bake GetVariable branch (Kind + variable name; scalar vars refuse) · BP2066 + `BlueprintNodeModel`
bake-incomplete made Kind-aware. 8 tests incl. a full Roslyn+ALC ForEach compile. Suite 2498/1-known-flake ·
goldens 184/184.

**LV-3…LV-6 remaining** (write nodes via `IrOp_ListWrite` + DebugProbe overflow (reuse FC-0's
`CollectionWriteFailed`) · declare-UX · debugger visibility · demo/docs).

### FC-3 — see the umbrella §Sequencing (details filled in when the batch starts)
