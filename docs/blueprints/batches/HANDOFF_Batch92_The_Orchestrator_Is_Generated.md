<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file - the Batch 92 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: it CORRECTS one line of Q45-F ("the HSM arm would emit nothing").
  Section 2 carries the measurement. Where they differ, this file wins.
-->
# HANDOFF — Batch 92: **the orchestrator is GENERATED** *(`Q45`, `BP-340`)*

> 📌 **Dispatched at `27c83f5e0`.** ⭐ **Branch from THIS commit** *(rule 7)* — the handoff itself.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 92 at 27c83f5e0` FIRST, before any code.**
>
> 📄 **Design basis: [`Architect_Question_45`](Architect_Question_45_Who_Emits_The_Orchestrator.md),
> `A`–`F` APPROVED by the user `2026-08-19`.** ⭐ **This batch is `Q45` built.**
> 📌 **Batch 91 STOPPED here and was right** — `R-99` settled *that*, `Q45` settles *where*.

---

## 1. ⭐⭐⭐ WHAT `Q45` RULED — **do not re-derive any of it**

| | ruling |
|---|---|
| **`A2`** | ⭐⭐⭐ **the Roslyn generator emits `{Name}.Orchestrators.g.cs`, as a FOURTH `AddSource`** — ⛔ **not** the JSON save path *(PU-D11 moved it off C#)*, ⛔ **not** `AiAssetEmitService` *(nothing invokes it)* |
| **`B`** | ⭐ the **body** goes to **`Hrot.AiEditor.Persistence/Emit/`** beside its five siblings — ⭐⭐ **`netstandard2.0`, already `ProjectReference`d by the generator** ⇒ ⛔ **no wall to cross** |
| **`C`** | ⭐ **ROUTE** — the two editor emitters become **thin callers** of the new core, and **KEEP `WriteOrchestratorFile`** for the Category-1 hand-authored path |
| **`D`** | ⭐ `CompanionFileDiscovery` stays — ⛔ **comment only** |
| **`E`** | ⭐ `subAssetResolver` ships **with** this batch |
| **`F`** | ⛔ **NO HSM `SubtreeSyncBindings`** — HSM cannot produce one *(`M-24`)* |

---

## 2. ⭐⭐⭐ A CORRECTION I OWE — **the HSM arm does NOT emit nothing**

⛔⛔ **`Q45-F`'s last line said *"the HSM arm would emit nothing."* THAT IS WRONG.** 📐 Measured after
writing it:

```
HsmOrchestratorEmitter.Emit  →  "Returns null when there are no ALIAS BINDINGS."
                                driven ENTIRELY by asset.GetAliasesFor(v.Name)
BTreeOrchestratorEmitter.Emit → driven by aliases  AND  GetApproachBSyncGroups()
```

⇒ ⭐⭐⭐ **HSM emits from Approach A ALIASES — which Batch 91 JUST made persistent.** ⚠ **Before `91b`,
`GetAliasesFor` on a reloaded HSM asset always returned empty**, so even a wired HSM emitter would have
produced `null` for every asset on disk. ⭐ **`91b` is what makes this arm meaningful.**

### ⭐⭐ AND THIS IS WHY HSM ≠ BTREE — **the asymmetry is real, not arbitrary**

| | **BTree** | **HSM** |
|---|---|---|
| how a sub-asset is **hosted** | ⭐ a **Subtree NODE** — `BTreeSubtreePayload {SubtreeName, SubtreeAssetId, IsResolved}`, a **resolver**, and `BTreeBlackboardAggregatorStrategy` pulling the child's requirements up | ⛔ **no node kind at all.** A state invokes an **`[HsmAction]`** |
| ⭐⭐⭐ **what the orchestrator IS** | ⭐ **OPTIONAL** — it adds Approach B field-sync **around a tick the kernel already does** | ⭐⭐⭐ **THE HOSTING MECHANISM ITSELF** — 📐 `HsmOrchestratorEmitter:104` emits `[HsmAction(Name = "Orchestrate_{sub}")]` … `{sub}.GetInterpreter().Tick(...)`. ⛔ **Without it an HSM cannot host a sub-tree at all** |
| what drives the emit | aliases **+** sync groups | ⭐ **aliases only** |
| `SubtreeAssetId` | on the **node payload**, name-resolved, feeds aggregation | ⚠ on a **STATE**, and its own comment says it exists to feed the **S2-4 cross-region validator** |
| an authoring gesture that sets it | ⭐ drop a Subtree node, type the name | ⛔ **NONE — nothing in the editor sets `StateNode.SubtreeAssetId`** |

⇒ ⭐⭐ **BTree's kernel composes trees natively; HSM's does not compose behaviours, so composition is
expressed as an ACTION.** ⭐ **That difference is architectural and correct.**
⛔ **What is ACCIDENTAL is that HSM's half was never finished** — no authoring gesture, no aggregation,
and the emitter that *is* the mechanism was never called. ⚠ **This batch fixes only the last of those
three.** ⛔ **Do NOT build the other two** — 📌 say so in the report so nobody reads *"HSM orchestrators
generate"* as *"HSM subtree hosting works."*

---

## 3. 🛠 **`92a` — the emit BODY moves to `Hrot.AiEditor.Persistence/Emit/`** *(`Q45-B`)*

⭐ **`BTreeOrchestratorEmitCore` / `HsmOrchestratorEmitCore`**, beside `BTreeEmitCore` · `HsmEmitCore` ·
`BTreeBridgeEmitCore` · `HsmBridgeEmitCore` · `AiEmitCoreBase` · `BTreeBlackboardPackHelper`.

| ⚠ | |
|---|---|
| ⭐⭐⭐ **the input changes shape, and that is the whole difficulty** | 📐 today's emitters take the **editor MODEL** *(`BehaviorTreeAsset` / `HsmAsset`)*. ⛔ **A generator has only the DTO.** ⇒ ⭐ **the core must emit from `BehaviorTreeAssetDto` / `HsmAssetDto`** — which now carry **both** inputs: `SubtreeSyncBindings` *(since PU)* and **`Aliases`** *(since `91b`)* |
| ⭐⭐ **ONE body, not two** *(ruling 9)* | ⛔ **do not leave the copy-emitting algorithm in two places** — the editor emitters become callers *(`92c`)* |
| ⭐ **preserve the shape exactly** | 📄 §8.3: **copy · tick · copy**, ⛔ **no orchestrator at all when there is nothing to emit** *(`methods.Count == 0 && syncGroups.Count == 0` ⇒ `null`)*. ⚠ **Their 21 existing tests are the acceptance signal — they must keep passing** |
| ⚠ **`DtoType` across the boundary** | 📐 the editor emitters read `binding.DtoType.Name` / `.Namespace` — a **`Type`**. ⭐ **`91b` persists it as `Type.FullName`** ⇒ **the core splits the string; ⛔ it must NOT resolve a `Type`** — a generator cannot load behavior assemblies. ⭐ **If that turns out to be false, STOP and report** |
| ⚠ **`R-49`** | ⛔ never per-**variable** code. ⭐ per-**binding** copy statements are fine |

---

## 4. 🛠 **`92b` — the fourth `AddSource`** *(`Q45-A2`)*

📐 `BTreeJsonGenerator` emits **three** *(`:225` topology · `:244` blackboard · `:278` registrar)*;
`HsmJsonGenerator` emits **two** *(`:104` · `:119`)*.
⇒ ⭐ **add `{baseName}.Orchestrators.g.cs` to BOTH**, ⛔ **omitted entirely when the core returns null.**

⭐⭐ **Same trigger, same `obj/GeneratedFiles`, same lifecycle as the other three** — ⛔ **nothing new
for a host to remember.**

---

## 5. 🛠 **`92c` — route the editor emitters** *(`Q45-C`)*

⭐ `BTreeOrchestratorEmitter.Emit` / `HsmOrchestratorEmitter.Emit` become **thin callers** of the core.
⭐ **`WriteOrchestratorFile` STAYS** — 📌 it serves the **Category-1** hand-authored path, which
`EditorSubsystem:3136` explicitly keeps *"per spec"*.
⛔ **Do NOT delete either emitter** *("no rush removals")*. ⛔ **Do NOT wire `WriteOrchestratorFile` to
anything** — that is what `Q45` ruled against.

---

## 6. 🛠 **`92d` — pass `subAssetResolver`** *(`Q45-E`; was `91c`)*

📐 `InspectorWindow._subAssetResolver` is **`readonly`, ctor-only, no setter**;
`PerspectiveWorkspaceRegistrar:241` — the only production construction — omits it ⇒ the
`PARAMETER SYNCHRONIZATION` panel renders **`"Sub-asset resolver not configured."`** everywhere.
⭐ **The silent-default pattern** — ⭐⭐ **rail the CONSTRUCTED object** *(`R-67`)*, ⛔ never the source.
⚠ **Now coherent**: once `92b` lands, the bindings this panel authors are actually executed.

---

## 7. 🛠 **`92e` — three comments that mislead** ⭐ *(doc only, ⛔ no behaviour change)*

| # | where | what to say |
|---|---|---|
| ⭐⭐ **1** | `HsmValidator:395` | ⛔ **ROTTED** — it blames a missing `StateNodeDto.SubtreeAssetId`; **that field exists at `HsmAssetDto.cs:73`** and `DEBT-AIB-028(a)` is **resolved** *(Batch 75)*. ⭐ **The real blocker is `M-24`**: `HsmAsset` does not implement `IBTreeSyncableAsset` |
| ⭐⭐ **2** | `HsmAssetValidator:37` | ⚠ **HALF-ROTTED** — *"not persisted … so nothing sets the field yet."* ⭐ **Persistence: FIXED.** ⛔ **"Nothing sets it": still TRUE** — there is no authoring gesture. ⭐ **Split the two halves** |
| ⭐ **3** | `CompanionFileDiscovery:194`/`:208` | ⭐ **name the CATEGORY it serves** *(hand-authored `.cs` companions)* — 📌 **I read it as proof the JSON path had a consumer and it cost a stopped batch** |

---

## 8. ⚠⚠ **THE CORPUS EXERCISES NONE OF THIS — build a fixture, or ship an untested emitter**

📐 **Coordinator-measured `2026-08-19`**, over every source `*.btree.json` / `*.hsm.json`:

```
assets with POPULATED SubtreeSyncBindings or Aliases  →  NONE
```

⇒ ⭐⭐⭐ **TWO consequences, both load-bearing:**

| ⭐ | |
|---|---|
| ✅ **GOLDEN CANNOT MOVE** | every corpus asset emits **no orchestrator** ⇒ the fourth `AddSource` is omitted ⇒ **byte-identical output.** ⚠ **If a golden DOES move, STOP** — something else changed |
| ⛔⛔ **AND NOTHING WOULD EXERCISE THE FEATURE** | 📌 **this programme's signature failure**: an emitter that ships, is green, and has never produced a line. ⭐⭐ **Build a FIXTURE asset** *(an alias for HSM, and an alias + a sync binding for BTree)* and assert the **emitted TEXT** — ⛔ **not that the core returns a non-null string** |

⭐⭐ **The two arms differ and both must be covered** — 📌 §2: **BTree** exercises aliases **and** sync
groups; **HSM** exercises **aliases only**.

---

## 9. ⛔ SCOPE FENCE

| ⛔ not this batch | |
|---|---|
| **HSM subtree AUTHORING** *(a gesture that sets `StateNode.SubtreeAssetId`)* and **HSM blackboard aggregation** | 📌 §2 — the other two unfinished thirds. ⭐ **Name them in the report; do not build them** |
| **`HsmAsset : IBTreeSyncableAsset`** and the Inspector gate widening | 📌 **`Q45-F`'s steps ② and ③** — a separate item |
| **anything in `Q38`–`Q44`** | ⛔⛔ **`R-27` — the visual check has not run** |
| **`BP-341`** *(the readable auto-name)* | ⭐ belongs with `B2` |
| **the rest of `BP-337`** | ⭐ a native headless-rendering question in `GizmoMap.Presentation` |

---

## 10. ⭐ GATES — **the contract, plus the four this batch owns**

| # | report |
|---|---|
| **1–7** | the standard contract · **`--no-build` column** *(⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` report a STALE BIN)* · every red confirmed **pre-existing vs `27c83f5e0`** · clean tree after every suite · both quarantine counts · every id you allocated |
| ⭐ **7b** | ⭐⭐ **every gate script UNFILTERED with `EXIT=$?`** — 📌 your own Batch 90 §7b root cause. ⚠ **`tracker-counts --check` is RED on the first run of any batch that adds a row; that is the script working** |
| ⭐⭐⭐ **8 — GOLDEN** | ⭐ **§8 says it cannot move.** ⛔⛔ **Movement is a STOP-AND-REPORT, not a rebase.** ⚠ **Include `MigrationEquivalenceTests`** |
| ⭐⭐ **9 — THE ENUMERATION** | ⭐ **every `AddSource` in both generators, before and after**, and ⭐ **every production caller of each orchestrator emitter.** 📌 `R-74` |
| ⭐⭐⭐ **10 — WHAT EACH RAIL ASKS** | ⛔⛔ **A rail that the core returns non-null proves NOTHING.** ⭐⭐ **Assert the EMITTED TEXT** — §8.3's **copy · tick · copy** order, the `[HsmAction]` attribute on the HSM arm, and ⭐ **the null case: nothing emitted when there are no bindings** |
| ⭐⭐ **11 — REVERT-GOES-RED** | one probe per item, **INVERSE EDIT** — ⛔ never `git checkout --`. ⚠ **Separately for the BTree and HSM `AddSource`** — ⭐ two arms, two reds |

⭐ **Baseline** *(post-Batch-91)*: AiShared **1479** · Blueprints **3773/3783/10** · BTree.Editor **622** ·
Hsm.Editor **554** · Hrot.Editor **201** · Breakpoints **143** · NodeEditor.Core **211** ·
NodeEditor.UI **135** · Fhsm **300** · AiEditor.Generators **270** ·
`Fdp.Presentation.Tests` **146 FILTERED** *(`BP-337`)* · tracker **open 68 / done 208** · rulings **66/66**.
⛔ **`Fdp.Toolkits.Tests`: do not run it** — 📌 `DEBT-AIB-030`.

## 11. ⭐⭐ If you must stop

| ⭐ complete on its own | |
|---|---|
| **`92e`** | doc only — ⭐ land it whatever else happens |
| **`92a` + `92b`** | ⭐ the feature. ⛔ `92a` alone is a body nobody calls — **the exact shape this batch exists to end** |
| **`92c`** | ⭐ the de-duplication; ⚠ safe to defer **one** batch if `92a`'s signature fight is long |
| **`92d`** | ⭐ independent, ⛔ but pointless before `92b` |

⚠ **If the DTO cannot express what the emitters need — STOP AND REPORT.** ⛔ **Do not widen the DTO on
your own judgement**; that is a persistence-schema decision and it is mine to take to the user.

## 12. ⭐⭐⭐ WHAT THIS UNLOCKS — **state it precisely, and do not overclaim**

⭐ **Approach B field-sync EXECUTES on BTree** — authored in the Inspector, persisted in JSON, generated
into C#, run by the orchestrator. ⭐ **And an HSM state can host a sub-tree at all**, for the first time.
⛔⛔ **Do NOT write *"the sub-asset sharing model is complete."*** ⚠ **HSM still has no authoring gesture
and no aggregation** *(§2)*, so a designer cannot yet create the alias an HSM orchestrator would emit
from. ⭐ **Say exactly which half works.**
