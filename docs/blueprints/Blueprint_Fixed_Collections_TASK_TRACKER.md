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
| FC-1·E | Write editor: reflector write-accessor discovery, palette (two-gate filter), drawer, wire-bake, demo bp | component home | ⬜ |
| FC-1·G2 | Tick-order: fix the `bpTick` splice (preferred) or pin 1-tick lag + composition-order test | composition | ⬜ |
| FC-1b | `[BlueprintCollectionField]` Roslyn source generator emitting `{Component}CollectionOps` from the FC-0 template | tooling | ⬜ |
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

### FC-1·E / FC-1·G2 / FC-1b / FC-2 / FC-3 — see the umbrella §Sequencing (details filled in when the batch starts)
