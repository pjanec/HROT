<!--STATUS
state: LIVE — report from the backend lane to the behaviours lane
updated: 2026-10-01
current-answer: §1 (what you can use now) and §4 (what to watch for when you migrate). §2 maps the handoff item by item.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_EQS_Unification.md — the handoff this answers (behaviours lane → backend lane).
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — the owning design. §16 (measured state), §16.2a (the node split),
    §17 (UML, parity, migration recipe, as-built, the shared Brain part). Everything below cites it; it is the durable record.
  - docs/designs/hill-attack/DESIGN.md — owns the doctrine that consumes the area query (the invariant).
  - docs/blueprints/Architect_Question_78_Hill_Attack_The_Blueprint_Node_Way.md — the blueprint hill attack waiting on this.
-->

# REPORT — EQS unification: the area query inside EQS 1.3

**From:** the backend lane (`backend`) · **To:** the behaviours lane (`behaviors`) · **Answers:**
[`HANDOFF_EQS_Unification.md`](HANDOFF_EQS_Unification.md) · **Head:** `backend` @ `9a8d7edea`
*(merges `behaviors` cleanly: last merged at `8f8c75f13`, nothing new on `behaviors` since)*.

> 🔒 **The user narrowed the scope during the work** *(2026-09-30)*: *"no need to update callers, just integrate area
> query into eqs and leave old area query as it is please, the behavior lane still works with it and i need this work
> to merge back to behaviors easily."* ⇒ handoff items ④ (retire AreaQuery) and ⑤ (move the callers) were **not done
> here**. ⑤ is yours, with the recipe in §3; ④ follows it.

## 1. What you can use now

| | where | design |
|---|---|---|
| ⭐ **EQS is live in production** — every host installs the template registry and the solver through ONE startup, `EqsSolverStartup.Register(context)` *(SimHost, Stride, editor)* | `Hrot.SimHost/EqsInfrastructureCapability.cs` | [§17.3](../../designs/eqs-2/EQS_Design_v1.3_final.md) |
| ⭐ **`EntitiesOfForceInArea`** — the area query as an EQS template. Generator `EntitiesInAreaGenerator` reuses `AreaQuerySolverSystem.PointInPolygon` (parity by construction); filters `FactionFilterTest` + `AliveFilterTest` | `Hrot.SimHost/Systems/EntitiesInAreaGenerator.cs` · AssetId `3e5a7c91-2b4d-4f86-a0c3-5d7e9f1b2a64` | [§17.1](../../designs/eqs-2/EQS_Design_v1.3_final.md), [§17.5](../../designs/eqs-2/EQS_Design_v1.3_final.md) |
| ⭐ **The node split is the same as AreaQuery's** — the sensor is authored on the Brain (CGF), computed on the Muscle (SimHost), the answer read on the Brain as **Brain-local** entities | the four EQS translators in `Hrot.Network.NED` | [§16.2a](../../designs/eqs-2/EQS_Design_v1.3_final.md), [§17.2](../../designs/eqs-2/EQS_Design_v1.3_final.md) |
| ⭐ **Blueprints** — `SpawnEqsSensor` now has `ContextSlot0/1/2` (`Entity`) pins and survives save + reload with its links; `ReadEqsResult` has its full pin set; the template picker lists every runtime template | `BuiltInNodeRegistry.cs` | [§17.4](../../designs/eqs-2/EQS_Design_v1.3_final.md) |
| ⭐ **The Brain-side authoring is ONE implementation on the editor AND CGF** — node drawers, the template picker, the Details node view, and the canvas pills (the template name on a `SpawnEqsSensor` node) | `Hrot.Editor.AiComposition/AiBlueprintNodeAuthoringBinder.cs` | [§17.8](../../designs/eqs-2/EQS_Design_v1.3_final.md) |

**Authoring the area query in a blueprint:** `SpawnEqsSensor` with template **EntitiesOfForceInArea** ·
`ContextSlot1` ← the area entity · `FactionFilter` ← a **bitmask**, `1 << ForceId`: Neutral `1`, Friend `2`,
**Hostile `4`** ⚠ *(unwired it is `0`, which matches no force — you get an empty answer, not an error)* ·
then `ReadEqsResult` on its `Handle`: `IsReady`, `ResultCount`, `Entity` per `ResultIndex`.

## 2. The handoff, item by item

| # | item | state | evidence |
|---|---|---|---|
| ① | make EQS live (`CE-465`) | ✅ | `EqsModuleTests` (production install, canonical id); T-DIS4 runs on the **production** registry. ⚠ `EqsModule.cs`'s "hot reload picks templates up" was false — comment corrected, no reload path added |
| ② | `EntitiesInArea` generator + the parity template | ✅ | §1 above |
| ③ | parity rail | ✅ | the matrix in §4 — 10/10 across hosts, stable on a second run |
| ④ | retire AreaQuery | ⛔ not done — user ruling | untouched; [§16.3](../../designs/eqs-2/EQS_Design_v1.3_final.md) lists its full footprint for when you retire it |
| ⑤ | move the callers | ⛔ **yours** | recipe in §3 |
| D1–D4 | facade? pool? order? network? | D1 no facade (the recipe moves callers to the sensor model) · D2 EQS keeps its own buffer, `EqsTargetPool` stays with AreaQuery · D4 the solver runs on the Muscle, exactly where AreaQuery's does | [§17.2](../../designs/eqs-2/EQS_Design_v1.3_final.md) |

## 3. Migrating your callers — the recipe

⭐ The full table is [EQS design §17.6](../../designs/eqs-2/EQS_Design_v1.3_final.md) — ⛔ not restated here. In one line:
**one persistent child sensor per question** instead of request / poll / free; the sensor re-answers at 10 Hz.
Callers named in your handoff: `HillAttackCommanderNodes.cs` (5 methods), `TargetPoolOps`, `AreaQueryBatchOps`.

## 4. ⚠ What to watch for — the parity matrix verdict

`EqsDistributedTests` (`Hrot.ClusterRunner.Integration.Tests/Eqs/`) compares the two on a **real CGF + SimHost over
DDS**; every step waits for EQS to settle, then asks the old AreaQuery the same question. Full table:
[§17.5](../../designs/eqs-2/EQS_Design_v1.3_final.md).

✅ **They agree** on: inside / outside / wrong force / wrecked · a target enters, dies, turns hostile, turns friendly,
is deleted, leaves · **the area moves** · a concave L (target in the notch) · a triangle · a target on an edge ·
three sensors at once across two commanders and both forces.

⛔ **They differ in three places** — each one matters to a migrating caller:

| | old AreaQuery | EQS 1.3 | ⭐ what it means for you |
|---|---|---|---|
| **more than 16 targets** (T-DIS8) | all of them (≤ 64) | exactly **16**, all from the old set | your waves round-robin at most 16 tanks (`HillAttackCommanderNodes.cs:374`), so this costs the hill attack nothing ([§16 H8](../../designs/eqs-2/EQS_Design_v1.3_final.md)). ⚠ A future caller that needs **every** target needs a per-template limit — designed (§4.3 `TopK`), never built |
| **area with no polygon** (T-DIS9) | answers **READY, 0 targets** — your `Condition_IsAreaQueryResolved` reads that as **"area clear"** | publishes **nothing**; the reader keeps waiting | ⚠ **keep your 5 s timeout** when you migrate — it becomes the only way that case ends. ⭐ No more false "area clear" |
| 🔴 **targets outside 0..1000 m** (T-DIS10) | **blind** — its broad phase is the perception grid, 200 × 200 cells of 5 m anchored at the **world origin**; anything at x or y `< 0` or `≥ 1000` is never in it | sees them | 🔴 **a live risk for you TODAY, before any migration:** a scenario whose area lies outside that square — e.g. a centred origin putting half the map at negative coordinates — gets **no targets** from the old query, so the hill attack reads "area clear". Documented as an engine limit in the [programmers' guide](../../HROT-PROGRAMMERS-GUIDE.md) (now stating the origin anchor); moving to EQS removes it |

## 5. Defects fixed on the way — they change behaviour you rely on

| | before | now | design |
|---|---|---|---|
| 🔴 `EntityInfoIngressTranslator` | the owner applied its **own stale loopback sample** ⇒ **no runtime force change on an owning node ever replicated** (the old AreaQuery was hit equally) | the owner ignores incoming `EntityInfo` where authority is tracked | [§17.7](../../designs/eqs-2/EQS_Design_v1.3_final.md) |
| 🔴 `EqsSensorConfigIngressTranslator` | cached an ECB **placeholder** as the Muscle carrier ⇒ a sensor's parameters never changed after its first sample, and a disposed sensor's carrier was never destroyed | carriers resolved from the world | §17.7 |
| `EqsSensorConfigEgressTranslator` | published a sensor's config **once** | publishes on any change; waits for slot entities to have a network id; disposes on removal | §17.7 |
| `EqsResult*` translators | network ids where Brain-local entities were expected | mapped to Brain-local; unmapped dropped | §17.7 |
| `PreviewTestPos` (test) | `[ComponentId(210)]` — production's `IEqsTemplateRegistry` id ⇒ 9 SimHost test classes red once a registry existed | `507` | §17.7 |

## 6. Gates

| gate | result |
|---|---|
| `EqsDistributedTests` (ClusterRunner, real CGF + SimHost) | **10 / 10**, twice |
| `HillAttack*` (`Hrot.SimHost.Tests`) — the handoff's invariant, SimHost half | **65 / 65**, including all 6 `HillAttackIntegrationTests` (`SC_HA015_1` full end-to-end … `SC_HA015_6` the solver's polygon test) |
| ⚠ the invariant's **live `ClusterRunner --mode all` half** | ⛔ **not run** — nothing in these commits changes the AreaQuery path the hill attack uses, but the `EntityInfo` fix touches every replicated force. Worth one live run on your side |
| Blueprints (`Hrot.Blueprints.Tests`) | 4023 passed / 0 failed / 17 skipped |
| `EqsAuthoringOnBothHostsTests` · `TheEqsBrainStartupIsSharedTests` · `AiDocumentViewStateBinderTests` | green |
| NED translators · MiniExCon + attribute round-trip · conformance 13/13 · Stride compiles | green |
| ⚠ pre-existing reds, proven on base `196c7f7c9` | 31 EQS rails on `EditorHarness` (e.g. *"LocomotionChannel is not registered"*) · IG `EntityInfoTranslatorTests` ×4 · `CgfSubsystemHeadlessTests` ×3 · SimHost `FullBranchPipelineTests`, `MapPresentationParityRails`, `NodeRolePersistenceRails` |

**Ids allocated:** none. `CE-465` (yours) is closed by ①.
