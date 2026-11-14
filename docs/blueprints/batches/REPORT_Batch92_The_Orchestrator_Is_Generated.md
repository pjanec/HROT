<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: the whole file — it is Batch 92's report
stale-below: nothing
known-rot: nothing
known-conflict: §2 CONTRADICTS the dispatched handoff's §3 premise that the DTOs carry both Approach-A
  and Approach-B inputs. The handoff is wrong on the Approach-B half; the measurement is in §2.
-->

# REPORT — Batch 92: **the orchestrator is GENERATED** *(`Q45`, `BP-340`)*

> **Dispatched at** `27c83f5e0` · **started at** `9503de9` · scope frozen at the dispatch sha.
> **Base for every red:** `27c83f5e0`.

---

## 1. ⭐ WHAT LANDED

| item | verdict |
|---|---|
| **`92a`** — the emit body moves to `Hrot.AiEditor.Persistence/Emit/` | ⭐ **DONE for the alias arm on both hosts.** ⛔ **Approach B STOPPED — `BP-342`, §2** |
| **`92b`** — the fourth / third `AddSource` | ⭐ **DONE, both generators**, omitted when the core returns `null` |
| **`92c`** — route the editor emitters | ⭐ **DONE.** `WriteOrchestratorFile` **kept**, still wired to nothing new |
| **`92d`** — pass `subAssetResolver` | ⭐ **DONE**, railed on the **constructed object** |
| **`92e`** — three misleading comments | ⭐ **DONE** *(doc only)* |
| ⚠ **an unplanned finding** | **`BP-343`** — the golden parts drift-guard reddened on correct behaviour; fixed. §5 |

**IDs allocated (rule 5): `BP-342`, `BP-343`.** `BP-340` closed.
**`DEBT-AIB` partitions touched: none.**

---

## 2. ⛔⛔ THE STOP — **the DTO cannot express Approach B, and the handoff's premise is false**

📄 **The handoff, verbatim** *(§3)*: *"the core must emit from `BehaviorTreeAssetDto` / `HsmAssetDto` —
which now carry **both** inputs: `SubtreeSyncBindings` *(since PU)* and **`Aliases`** *(since `91b`)*"*.

📐 **Measured: true for the BINDINGS, false for everything else Approach B needs.** ⭐⭐ **Two
independent gaps, either of which alone blocks it.**

### ① The sub-tree IDENTITY is **session-local**, not merely unpersisted

| what | where |
|---|---|
| the group is skipped without metadata | `BehaviorTreeAsset:719` — `if (!_syncNodeMeta.TryGetValue(nodeId, out var meta)) continue;` |
| ⭐⭐ its **only** writer in the repo | `InspectorWindow:590`, inside `DrawSyncBindingsTable` — **a UI draw** |
| ⛔ the DTO names it excluded | `BehaviorTreeAssetDto.cs:10` |
| ⛔⛔ **and a RAIL enforces that** | `BTreeDtoRuntimeFieldExclusionTests:29` lists `"_syncNodeMeta"`, `"SyncNodeMeta"` |

⇒ ⚠⚠ **A consequence nobody had written down: even in the EDITOR, Approach B emits nothing after a
reload** until a designer re-opens that panel per node.

⭐ Two of the four group fields **are** recoverable — `NodeVisualId` is the `SubtreeSyncBindings` key,
`SubtreeName` is `BTreeSubtreePayloadDto.SubtreeName` *(mapper `:317`)*. ⛔ `SubtreeDtoTypeName`/`Ns` is
the **sub-asset's** `BlackboardTypeName` and is nowhere in the master DTO.

### ② ⛔⛔ WORSE — **the destination FIELD does not exist**

The body emits `ref var subDto = ref master.{SubtreeName}_{SubtreeDtoTypeName};`. That slice comes from
`GetAutoAllocatedVariables()` *(`BehaviorTreeAsset:768`)*, whose **only** consumer is
`BlackboardAuthoringWindow:529` — which merely **displays** it greyed as *"(size unknown until build)"*.

⇒ it never enters `_blackboardVariables`, never reaches `Blackboard.Variables`, and **no blackboard
emitter declares it.** ⇒ ⭐⭐⭐ **widening the DTO would not be sufficient while ② stands.**

### ⭐ What landed instead of a guess

⛔ **The DTO was not widened** *(the handoff forbids it, and a rail forbids it, and it would not be
enough)*. ⛔ **No sibling-asset catalog was invented on my own judgement.**

⭐⭐ The Approach-B groups became an **explicit, non-optional parameter** of
`BTreeOrchestratorEmitCore.Emit` — 📌 the silent-default rule: *"a production caller that HAS a
dependency must PASS it."* The editor passes its `_syncNodeMeta`-derived groups; the generator passes
`Array.Empty<OrchestratorSyncGroup>()` **and states at the call site why it provably has none.**

⇒ ⭐ **one body** *(ruling 9)*, **fully railed** — ⛔ even though the generated arm cannot reach it yet.

⚠ **A precedent for closing ① exists inside `BTreeJsonGenerator` already:** *"Option A"* collects
sibling `*.bp.json` `AdditionalTexts` into `GeneratedBlueprintSchemaCatalog` for exactly the
*"a sibling asset's shape is not in my DTO"* problem. A `*.btree.json` catalog keyed
`AssetId → Blackboard.TypeName` would answer `SubtreeDtoTypeName` **with no schema change**.
⛔ **Not built** — it fixes only ①.

⇒ ⭐⭐ **The question for the owning batch:** *does the master blackboard DECLARE the auto-allocated
sub-tree slice, and if so who sizes it?* *(`GetAutoAllocatedVariables` carries its own `DEBT`: "real
type resolution requires catalog integration".)*

---

## 3. ⭐ THE CORRECTION THE HANDOFF ASKED ME TO CARRY

📌 `Q45-F`'s line *"the HSM arm would emit nothing"* is **wrong**, and the handoff's §2 already said so.
Confirmed by construction here: **HSM emits from Approach-A aliases**, which `91b` made persistent ⇒
⭐⭐ **`91b` is what makes the HSM arm meaningful at all.** And the HSM orchestrator **IS the hosting
mechanism** — without it an HSM state cannot host a sub-tree — whereas BTree's is optional sugar around
a tick the kernel already performs.

⛔⛔ **Not "sub-asset sharing is complete."** ⭐ Precisely: **an HSM state can host a sub-tree at all,
for the first time** — ⛔ but HSM still has **no authoring gesture** creating the alias and **no
blackboard aggregation** behind it *(handoff §9's scope fence; not built)*.

---

## 4. 🛠 WHAT WAS BUILT

| file | what |
|---|---|
| **`Emit/OrchestratorAliasCollector.cs`** *(new)* | ⭐ the Approach-A loop, **owned once** — it was character-for-character duplicated across the two hosts. `SplitTypeId` splits `Type.FullName`; a nested `+` becomes `.` *(⛔ not reduced to `Type.Name`, which is unreferable; character-identical for every non-nested type)* |
| **`Emit/HsmOrchestratorEmitCore.cs`** *(new)* | emits from `HsmAssetDto`; `null` when no alias |
| **`Emit/BTreeOrchestratorEmitCore.cs`** *(new)* | same, plus the Approach-B arm behind the explicit parameter; `+ OrchestratorSyncGroup` / `OrchestratorSyncBinding` *(⛔ deliberately not `Hrot.Editor.AiShared`'s `ApproachBSyncGroup` — netstandard2.0 cannot see the net8/ImGui assembly)* |
| `BTreeJsonGenerator.cs` · `HsmJsonGenerator.cs` | the new `AddSource`, omitted on `null` |
| `BTreeOrchestratorEmitter.cs` · `HsmOrchestratorEmitter.cs` | thin callers, **through the same `ToDto` the save path uses** — ⛔ not a second projection |
| `PerspectiveWorkspaceRegistrar.cs` | `subAssetResolver: id => catalog.FindByAssetId(id) as IBlackboardManagedAsset` |
| `InspectorWindow.cs` | `HasSubAssetResolver` + `ResolveSubAssetForRail` |
| `HsmValidator.cs` · `HsmAssetValidator.cs` · `CompanionFileDiscovery.cs` | `92e` |

⭐ **`92d` is the silent-default pattern in textbook form**: the registrar **held** `IAssetCatalog` as a
required argument and did not pass it, so `InspectorWindow:449` rendered *"Sub-asset resolver not
configured."* everywhere and **no designer could author a sync binding at all**.

---

## 5. ⚠ `BP-343` — **the gate found a defect in itself**

`HsmGoldenCorpusTests.TheHarnessEmitsTheSamePartsTheGeneratorDoes` scraped the generator's `AddSource`
hints and compared them to **one asset's emitted parts** — which silently assumes **every part is
unconditional.** ⇒ the moment a conditional part exists it fails on correct behaviour.

⚠⚠ **BTree's `{Name}.Blackboard.g.cs` has been conditional since S1-2**, so the guard was only
accidentally right; it simply had no BTree counterpart.

🛠 Split into the two properties it conflated: `AiAssetKind.AllHintNames` declares every part the
generator **can** emit; the scrape is compared to that **at full strength**; a second rail requires the
harness's emitted parts to be an **ordered subset**. ⇒ ⭐ the harness can neither miss a gained part nor
invent one.

---

## 6. ⭐⭐ GATES — **the seven-row contract**

| # | gate | command | result | Δ vs baseline | `--no-build`? |
|---|---|---|---|---|---|
| 1 | AiShared | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --no-build` | **1485 / 0 / 0** | **+6** *(`92d` rails)* | ✅ yes |
| 2 | BTree.Editor | `… Hrot.BTree.Editor.Tests --no-build` | **622 / 0 / 0** | **0** | ✅ yes |
| 3 | Hsm.Editor | `… Hrot.Hsm.Editor.Tests --no-build` | **554 / 0 / 0** | **0** | ✅ yes |
| 4 | AiEditor.Generators | `… Hrot.AiEditor.Generators.Tests` | **277 / 0 / 0** | **+7** *(6 new + 1 `BP-343` split)* | ✅ yes |
| 5 | AiEditor.Persistence | `… Hrot.AiEditor.Persistence.Tests --no-build` | **143 / 0 / 0** | **+7** *(copy·tick·copy)* | ✅ yes |
| 6 | Blueprints | `… Hrot.Blueprints.Tests --no-build` | **3773 / 0 / 10 skip** | **0** | ✅ yes |
| 7 | Hrot.Editor | `… Hrot.Editor.Tests --no-build` | **201 / 0 / 0** | **0** | ✅ yes |
| 8 | Breakpoints | `… Hrot.Diagnostics.Breakpoints.Tests --no-build` | **143 / 0 / 0** | **0** | ✅ yes |
| 9 | NodeEditor.Core | `… NodeEditor.Core.Tests` | **211 / 0 / 0** | **0** | ⛔ **NO — out of solution, stale bin** |
| 10 | NodeEditor.UI | `… NodeEditor.UI.Tests` | **135 / 0 / 0** | **0** | ⛔ **NO — same** |
| 11 | Fhsm | `… Fhsm.Tests` | **300 / 0 / 0** | **0** | ⛔ **NO — same** |
| 12 | Fdp.Presentation *(`BP-337`)* | `… --filter "FullyQualifiedName~Fdp.Presentation.Tests.WindowManager"` | **146 / 0 / 0** | **0** | ✅ yes |

⛔ **`Fdp.Toolkits.Tests` NOT RUN** — 📌 `DEBT-AIB-030`, rotating identity.
⭐ **No RED anywhere ⇒ nothing to confirm pre-existing against `27c83f5e0`.**
⭐ **Working tree CLEAN after every suite run** — verified with `git status --short`; ⛔ no golden was
regenerated and no `*.Orchestrators.g.cs` was written to disk *(`find` over the tree: zero)*.
⭐ **Quarantine counts unchanged**: Blueprints **10 skipped**, everything else **0**. ⛔ **No new skip.**

### ⭐ 7b — the scripts, UNFILTERED, with `EXIT`

```
$ python3 scripts/rulings-check.py
66/66 rulings verified against their sources
WARN 1 cited source(s) changed after the ledger was last updated: docs/blueprints/PLAN_Remaining_Work.md
EXIT=0                     ⭐ the WARN is PRE-EXISTING — RULINGS.md's own header records it

$ python3 scripts/tracker-counts.py --check
TRACKER COUNTS DISAGREE WITH THE ROWS: … Total: table says open=68 done=208, rows say open=69 done=209
EXIT=1                     ⭐ EXPECTED — the summary table is DERIVED; this is the script working

$ python3 scripts/tracker-counts.py --check      # after applying the corrected table
tracker counts OK — open 69 / done 209 (+1 refuted)
EXIT=0
```

📌 **My own Batch 90 root cause, not repeated:** every script above ran **unfiltered**, with its exit
code shown — ⛔ never piped through `tail`, which is what once turned the script's *remedy* table into
a false green.

---

## 7. ⭐⭐ GATE 8 — **GOLDEN**

| | |
|---|---|
| ⭐⭐⭐ **It did not move**, exactly as §8 predicted | no corpus asset has an alias or a sync binding ⇒ the core returns `null` ⇒ the file is **omitted entirely** |
| `MigrationEquivalenceTests` | **12 / 12** |
| HSM snapshots | all **4** `TheEmittedSourceOfEveryCorpusAssetIsUnchanged` green |
| BTree golden | green |
| **diff shape** | ⭐ **ZERO golden files touched** — the only changed files under `Golden/` are the two **harness** sources of `BP-343`, and `git status` shows **no `.txt` snapshot modified at all** |

---

## 8. ⭐⭐ GATE 9 — THE ENUMERATION *(`R-74`)*

| | before | after |
|---|---|---|
| `BTreeJsonGenerator` `AddSource` | **3** — `:225` topology · `:244` blackboard · `:278` registrar | **4** — `+ .Orchestrators.g.cs` |
| `HsmJsonGenerator` `AddSource` | **2** — `:104` · `:119` | **3** — `+ .Orchestrators.g.cs` |
| production callers of `BTreeOrchestratorEmitter` | **0** | **0** ⭐ *(unchanged — `Q45` ruled the generator owns it; `WriteOrchestratorFile` stays for Category-1 and is still wired to nothing)* |
| production callers of `HsmOrchestratorEmitter` | **0** | **0** |
| production constructions of `InspectorWindow` | **1** *(`PerspectiveWorkspaceRegistrar:241`)*, **without** a resolver | **1**, **with** one |

---

## 9. ⭐⭐⭐ GATE 10 — **WHAT EACH RAIL ASKS**

⛔⛔ **No rail asserts "the core returned non-null."** Every one asserts **emitted TEXT** or a
constructed object.

| rail | it asks |
|---|---|
| `AnHsmAliasEmitsAnHsmActionOrchestrator` | the text contains `[HsmAction(Name = "Orchestrate_GuardSubTree")]`, `ref master.{var}`, and `GetInterpreter().Tick(ref subBb, ref state, ref ctx);` |
| `ABTreeAliasEmitsABTreeActionOrchestrator` | `[BTreeAction(…)]` **and ⛔ NOT `[HsmAction`** — the hosts must not cross-emit |
| `TheDtoTypeIdIsSplitIntoNameAndNamespaceWithoutResolvingAType` | ⭐⭐⭐ a fixture type in **no assembly** *(`Made.Up.Behaviors.PatrolParams`)* still yields `Unsafe.As<PatrolParams, PatrolParams>` **and** `using Made.Up.Behaviors;` — ⛔ a `Type`-resolving core would fail |
| `AnHsm…` / `ABTree…AssetWithNoAliasEmitsNoOrchestratorFileAtAll` | ⭐ **the null case**: `GeneratedTrees` **still 2**, no orchestrator tree |
| `EachUniqueVariableSubTreePairEmitsExactlyOneMethod` | the dedupe key |
| `SyncInCopiesPrecedeTheTickAndSyncOutCopiesFollowIt` | ⭐⭐⭐ **the ORDER, by INDEX** — copy-in < tick < copy-out |
| `TheSubDtoIsTakenByRefFromTheAutoAllocatedSliceField` | `ref var subDto = ref master.PatrolSubTree_PatrolParams;` + `return result;` |
| `CopiesAreOrderedByFieldNameSoTheOutputIsDeterministic` | determinism of the emitted text |
| `AnAliasOnTheSameSubTreeSuppressesTheApproachBMethod` | ⛔ two methods under one `[BTreeAction]` name would collide |
| 3 × null-contract rails | no group · no active direction · no master variable ⇒ **nothing at all** |
| `TheResolverAnswersWithTheCatalogsAsset` | ⭐⭐ the **constructed** Inspector resolves a real asset — ⛔ not that a delegate is non-null |

---

## 10. 🔴 GATE 11 — **REVERT-GOES-RED** *(inverse edit only; ⛔ never `git checkout --`)*

| # | probe | red | ⭐ what it proves |
|---|---|---:|---|
| **P1** | HSM `AddSource` neutralised **only** | **3** | ⭐⭐ **the BTree arm stays green** — two arms, independently gated |
| **P2** | BTree `AddSource` neutralised **only** | **1** | ⭐⭐ **the HSM arm stays green** — the mirror of P1 |
| **P3** | `SplitTypeId` stops splitting | **1** | the string-split contract is load-bearing |
| **P4** | sync-out loop moved **before** the tick | **1** | ⭐ the **order** is asserted, not merely the statements |
| **P5** | `HsmOrchestratorEmitCore.Emit` forced to `null` | **2** | ⭐⭐⭐ **`92c` is real routing, not a cosmetic reroute** — the reds are **pre-existing editor-emitter tests** whose own code did not change |
| **P6** | resolver stubbed to `_ => null` | **1** | ⭐⭐ **`HasSubAssetResolver` stays GREEN** — proof the weaker rail alone would have been fooled, and the resolve rail earns its keep |
| **P7** | `"Orchestrators.g.cs"` dropped from `AllHintNames` | **1** | the `BP-343` drift guard still guards |

⭐ **Every probe un-applied by its inverse edit**; `git diff --name-only` after the run listed only the
two intended `BP-343` harness files.

---

## 10b. ⚠ RULE 4 — **what changed on the coordinator branch during this run** *(FYI only; scope frozen at `27c83f5e0`)*

`7e78727..da005a3` — `fc1d234` · `e9eacd8` · `da005a3`, all about **watch pinning** *(the next batch)*
plus **`R-102`** and **`M-25`**. ⛔ **Nothing invalidates an item of this batch**, so nothing was adapted
*(📌 the "scope is frozen at the dispatch sha" rule)*.

⭐⭐ **Two `RULINGS.md` §M rows are now STALE because of this batch** — 📌 §M is *"measure, don't
memorise"*, so these are reported rather than edited by me:

| row | it says | ⭐ after Batch 92 |
|---|---|---|
| 🔴 **`M-23`** | *"NO. The generator makes **three** `.g.cs` files and not this one; the two editor emitters still have zero production callers"* | ⭐ **the generator now makes FOUR (BTree) / THREE (HSM)** and emits `{Name}.Orchestrators.g.cs`. ⛔ **The "zero production callers" half is still TRUE and deliberate** — `Q45` gave the sidecar to the generator, not to an editor caller. ⛔ **Approach B still does not execute — `BP-342`** |
| 🔴🔴 **`M-19`** | *"NO, on both … `PerspectiveWorkspaceRegistrar:226` omits `subAssetResolver` ⇒ the panel renders `"Sub-asset resolver not configured."`"* | ⭐ **the resolver half is CLOSED by `92d`** *(now at `:241`, and railed on the constructed object)*. ⚠ the emitter half is answered by `Q45` as above |

⭐ **`M-24` corroborates `92e` exactly** — it names the same rotted `HsmValidator:395` comment and notes
*"`rulings-check.py` cannot see a code comment"*. ⭐ **`92e` repairs it in the code.**

---

## 11. ⭐ WHAT THIS UNLOCKS — **stated precisely**

✅ **BTree**: an Approach-A alias authored in the editor now reaches **generated C#** — persisted by
`91b`, emitted by `92b`.
✅ **HSM**: an HSM state can host a sub-tree **at all**, for the first time.
✅ **The `PARAMETER SYNCHRONIZATION` panel is usable** — `92d`.

⛔⛔ **NOT unlocked**: **Approach-B field sync does not execute** — `BP-342`, §2. ⛔ **HSM has no
authoring gesture and no blackboard aggregation.** ⛔ `HsmAsset : IBTreeSyncableAsset` (`M-24`) untouched.
