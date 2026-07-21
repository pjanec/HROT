# Tree-integration build plan (turnkey) — assembling the Hill-attack blueprint twins

> **Status: ✅ DONE (2026-07-17).** Built per this plan — `PlatoonHillAttack2.btree.json` + 6 `HillAssault2I_*` integrated blueprints + `PlatoonHillAttack2_Integration_ProofTests` (3/3). Kept below as the record of the approach. Originally: future track, architect-approved (Q9). The per-node twins are complete + proven in isolation
> (`HillAssault2_*` 52/52). This is the step-by-step recipe to assemble them into the running commander
> behavior, using ONLY the architect-sanctioned pattern (Q9-A/B). It is deliberately mechanical — every
> step mirrors an existing, shipped proof. No new compiler capability is expected.

## The two sanctioned patterns (mirror these exactly)
| Need | Mirror this shipped asset | Key detail |
|---|---|---|
| Blueprints share behavior state | `Assets/Blueprints/SharedStateRallyDemo.bp.json` + `T37_SharedStateManifestProvisioning.btree.json` | Empty `WorkingState`; `GetShared`/`SetShared` with `VariableId="state"`, `SharedTypeId="Hrot.AI.Behaviors.Brains.<SharedStruct>"` → `BlueprintSharedState.TryGetShared/TrySetShared` (Role=State, Scope=Entity) |
| Compose generated blueprints into a BTree | `Assets/BTrees/Authoring/T32_ComposedGeneratedBlueprint.btree.json` | Bind the generated `<Name>_*_Bp.TickCore` thunk via `"DelegateShape": "AiPrimitiveTickCore"` |
| Overall tree shape | `Assets/BTrees/Authoring/PlatoonHillAttack.btree.json` | `Sequence(CalculateSegments, DispatchAllToBaseline, AreAllAtBaseline, Repeater(Sequence(RequestAreaQuery, IsAreaQueryResolved, DispatchWaveWithTargets, IsWaveCompleted)))` |

## Step 1 — the shared Category-1 struct
Create `Hrot.AI.Behaviors.Brains.HillAttackSharedState` — a Category-1 blittable struct bundling **all**
fields the commander nodes share (the oracle's `HillAttackMutableState`):
`TotalSlots(int)`, `BurnedSlotsMask`/`WaveUsedSlotsMask`/`BaselineReservedMask(ushort)`,
`ActiveRunners(MemberSlotList)`, `CachedEqsRequestId(long)`, `CachedTargetGroupHandle(int)`,
`EqsRequestTime(float)`, `CurrentWave(byte)`. Register it in `StaticTypeRegistry.TypeTable` (like
`MemberSlotList`/`WaveState`), with its `Marshal.SizeOf`. The curated `*Ops` helpers already operate on
these sub-structs by value, so add thin `HillAttackSharedStateOps` field accessors/mutators
(`GetTotalSlots`/`WithTotalSlots`/`GetRunners`/`WithRunners`/…) — same by-value pattern as
`MemberSlotListOps`, so `GetShared → field ops → SetShared` round-trips one struct.

## Step 2 — re-author the stateful twins as `*_Integrated` blueprints (do NOT edit the proven twins)
For each of the 6 stateful nodes (`CalculateSegments`, `DispatchAllToBaseline`, `RequestAreaQuery`,
`IsAreaQueryResolved`, `DispatchWaveWithTargets`, `IsWaveCompleted`), create a `HillAssault2I_<Node>.bp.json`:
- **empty `WorkingState`**;
- at graph entry, `GetShared(VariableId="state", SharedTypeId="…HillAttackSharedState")` → the struct;
- replace every `GetVariable(field)` with a `HillAttackSharedStateOps.Get<Field>(state)` read, and every
  `SetVariable(field)` with `state = HillAttackSharedStateOps.With<Field>(state, …)` then a single
  `SetShared(state)` before the node returns (mirror `SharedStateRallyDemo`).
- Params stay Params (read via `GetParameter`); only the *shared mutable* fields move to `GetShared`/`SetShared`.
Keep the isolated `HillAssault2_*` twins untouched (they remain the per-node oracle-parity proofs).

## Step 3 — the tree
Author `Assets/BTrees/Authoring/PlatoonHillAttack2.btree.json` structurally identical to
`PlatoonHillAttack.btree.json`, but every action/condition node binds the corresponding
`HillAssault2I_<Node>_*_Bp.TickCore` via `"DelegateShape": "AiPrimitiveTickCore"` (mirror T32). Provision
the shared `state` var once at the tree/entity scope (mirror T37's manifest provisioning).

## Step 4 — end-to-end proof
`PlatoonHillAttack2_Integration_ProofTests.cs`: build a world + commander with a `UnitRoster` + the EQS
singleton, drive the composed tree across several ticks, and assert the observable behavior matches the
oracle's tree — e.g. baseline dispatch happens, then wave dispatch publishes `HullDownAttack` intents,
`IsWaveCompleted` drains the tracker, `CurrentWave` alternates across waves. Reuse the per-node proofs'
world setup. Determinism holds (Q8-C sim-seeded RNG), so exact assertions are possible.

## Gates
`dotnet build Hrot.AI.Behaviors` (0 err) · the new integration proof green · **no regression** in
`HillAssault2_*` (52/52) or full `Blueprints.Tests` (2042). The isolated twins and the integrated tree
coexist.

## Effort / risk
~6 re-authored blueprints (mechanical, mirror `SharedStateRallyDemo`) + 1 shared struct + accessors + 1
`.btree.json` + 1 proof. Highly delegatable per-node once Step 1's struct + accessors exist. No expected
new compiler work (GetShared/SetShared, struct Working-state/shared types, and AiPrimitive composition are
all already shipped + proven).
