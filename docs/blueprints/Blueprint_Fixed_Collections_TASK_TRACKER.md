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
| FC-1·C | Write compiler spine: node + Stage0 pins + **IR op family** + Stage5 + emit + Stage2 gates (G3/G4/managed/BP-writable-structural) | component home | ⬜ |
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

### FC-1·C — Write compiler spine · ⬜
- [ ] `CollectionWriteNode` (Assets) — baked: `ComponentTypeFqn`, collection name, `CollectionWriteOp`,
  `WriteAccessorFqn`, `CollectionKind` (CuratedStatic only v1); collection in-pin (G4) + per-op operand pins +
  `Ok` out-pin
- [ ] Stage0 pin enrichment mirror
- [ ] `IrOp_CollectionWrite` + Stage5 lowering (self-bound `IrOp_Self`, guarded shape) + StatementEmitter case
  (`HasComponent` guard → `GetComponentRW` ref → accessor call → `Ok`; `DebugProbe.CollectionWriteFailed` on
  both failure paths)
- [ ] Stage2 `V_CollectionWriteRules`: producer-self check (G4 — source GetComponent `Target` unwired),
  ManagedMember rejection (new BPxxxx), G3 iterate-while-writing warning, structural FQN/accessor checks
- [ ] Lowering + emit tests through Stage3→7 (CA-03 pattern); goldens 184/184

### FC-1·E / FC-1·G2 / FC-1b / FC-2 / FC-3 — see the umbrella §Sequencing (details filled in when the batch starts)
