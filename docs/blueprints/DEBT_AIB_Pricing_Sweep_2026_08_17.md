<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: the per-row verdict table
note: a sweep record, not a design. Rows marked STILL REAL are open debt; rows marked
  ALREADY FIXED or SUPERSEDED are closed and kept as the record of why.
-->
# `DEBT-AIB` pricing sweep — 13 rows, one verdict each

> 📌 **Batch 78, item 3.** ⛔ **Nothing was fixed** — the deliverable is this table, and a fix hidden
> inside a sweep is a diff nobody reviewed for its own sake.
> 📐 **Measured on the tree at `9d8b214`.** Source of the rows: `.dev/_DONE/btree-ai-action-binding/DEBT-TRACKER.md`.
> ⛔ **`-030` excluded by the handoff** *(the `Fdp.Toolkits.Tests` race — nothing to price)*.
> ⚠ **`-012` is the cautionary id** — described and never filed; that number belongs to a different,
> resolved row. ⭐ **Every row below was read in full before being priced.**

---

## The table

| id | verdict | one line |
|---|---|---|
| **001** | ⭐⭐ **ALREADY FIXED — `BATCH-01`** | `BlackboardDtoEmitter.cs:317` emits `[MarshalAs(UnmanagedType.I1)]`, and `TASK-TRACKER.md:14` records `S1-0` *done BATCH-01*. ⛔ **The row was never ticked** — this is the stale-debt cost the sweep exists to remove |
| **002** | ⭐ **STILL REAL, and narrower than filed** | The cross-check exists *(`BTreeJsonGeneratorTests.cs:947-989`, build-time packer vs runtime `BlackboardBinPacker`)*, but ⛔ **still only over scalar shapes** — the `fixed` / `[InlineArray]` padding case the row asks for is **not** among them |
| **003** | ⭐ **STILL REAL** | No generator emits a heavy (>100 B) DTO from authored variables. `HeavyDtoType` is a **hand-written** binding — `BehaviorTreeAssetDto.cs:61` carries the type NAME, and `ActionSchemaExporter.cs:151-157` reads it off `[SharedAiHeavyAction]`. ⭐ **Unchanged since filing** |
| **004** | ⭐⭐ **SUPERSEDED — by the `S3` scope model** | The row's *"separate design pass"* happened: `WorkingStateScope` ships with **`Node` / `Behavior`** and `S3_SharedSlotProvisioningTests` covers per-scope slot keys. ⚠ **The row's SQUAD half is NOT built** *(virtual squad-leader blackboard)* ⇒ ⭐ **re-file that half narrowly rather than keeping a row whose first iteration shipped** |
| **005** | ⭐⭐ **ALREADY FIXED — by `I4`** | A blueprint-authored AiPrimitive is now emitted **and discoverable**: `AiPrimitiveEmitter.cs:184` stamps `[GeneratedAiPrimitiveAction]`, `BehaviorActionCatalog.cs:171` consumes it, and the corpus carries `T31_ComposedAiPrimitive` / `T32_ComposedGeneratedBlueprint` / `T33_ComposedParamBlueprint`. 📄 `BTree_AiActionParameterBinding_Detailed_Design_Status.md` §I4 says so explicitly |
| **008** | ⭐ **STILL REAL** | `Emit_BoolField_CarriesMarshalAsI1` still uses `{int, bool, int}` — ⛔ **the discriminating `{bool, byte}` layout the row asks for was never added**. ⚠ **The source-level `[MarshalAs]` assertions in the same test DO guard the regression**, exactly as the row says, so the risk stays low |
| **010** | ⭐⭐ **SUPERSEDED — by `-030`, in its own text** | The row's `BATCH-08` update already redirects: *"non-deterministic — see `DEBT-AIB-030`."* ⭐ **Measured this batch: run 1 `1964 / 0`, run 2 one red (`SC_GZ004_2`), `--filter Gizmos` `187 / 0`** ⇒ ⛔ **the "~24 failures" figure is long dead**; one row, `-030`, is the live description. ⭐ **Close `010` INTO `030`** |
| **011** | ⭐ **STILL REAL — the bounded half holds** | `BATCH-03` bounded the blast radius *(the per-asset struct is nominal; divergence can only over-pad, caught by the 100-byte budget)* and ⭐ **that reasoning still holds** — nothing projects through the generated struct. ⛔ **The heavy/array fidelity coverage is still absent**, same gap as `002` |
| **022** | ⛔ **STILL REAL — and USER-DEFERRED** | `EntityAffinity` has **zero hits** in code or assets; the interim option-a *(surface editor BTrees to all entity types)* still ships. ⭐ **Deferred by the user `2026-06-15`** ⇒ **not debt to pay, a decision to revisit** |
| **023** | ⭐ **STILL REAL — but the "DEAD" claim is WRONG** | ⛔ **`CgfNodes.Action_HoldPosition` is NOT dead** *(`CgfNodes.cs:603`, `ref BrainBlackboard`)*, and `Action_Wander` *(`:416`)* is still bound by **`CombatShowcase`, `BTreeRenderShowcase`, `T04`** and others. ⚠ **The row's own scoping is stale**; the DTO replacement it names *(`EqsCombatNodes`)* exists and is used elsewhere, so the migration is real work, not a delete |
| **024** | ⭐⭐ **FOLD INTO `023`** | It is a note *about* `Action_Wander`, and `023` already owns it. ⛔ **Two ids for one action is why the partition needed pricing.** ⚠ Its factual half — *"a BTree node action, not a Mission-Editor behavior"* — is still correct |
| **025** | ⭐⭐ **SUPERSEDED — by `I4`, stated in the design doc** | 📄 `BTree_AiActionParameterBinding_Detailed_Design_Status.md` §I4: *"(Supersedes the `DEBT-AIB-025` deferral.)"* ⚠ **But the same doc's §E2 still cites `-025` as the blocker** ⇒ ⭐⭐ **the document contradicts itself and one of the two lines must go** — 📌 **flagged, not resolved here** |
| **031** | ⭐ **STILL REAL** | `AiHotReloadCoordinator.cs:94` declares `OnHardReloadCompleted` and `:369` fires it; ⛔ **every `+=` in the repo is in `BehaviorIngressHardReloadRepublishTests`**. **No production subscriber**, exactly as filed |

---

## Summary

| verdict | rows |
|---|---:|
| ⭐⭐ **ALREADY FIXED** | **2** — `001` *(BATCH-01)*, `005` *(I4)* |
| ⭐⭐ **SUPERSEDED** | **3** — `004` *(S3 scopes)*, `010` *(into `030`)*, `025` *(I4)* |
| ⭐⭐ **FOLD** | **1** — `024` into `023` |
| ⭐ **STILL REAL** | **6** — `002`, `003`, `008`, `011`, `023`, `031` |
| ⛔ **USER-DEFERRED** | **1** — `022` |
| **CANNOT REPRODUCE** | **0** |

⇒ ⭐⭐ **Six of thirteen are no longer live as filed.** ⭐ The handoff predicted *"ALREADY FIXED is the
likely majority"* — ⚠ **it is not the majority, but it is close to half**, and the six that survive are
smaller than their text suggests.

---

## Two things the sweep found that a verdict column cannot hold

| | |
|---|---|
| ⛔ **`023`'s scoping is factually wrong** | it calls `CgfNodes.Action_HoldPosition` **DEAD**. 📐 It is not: `CgfNodes.cs:603`, live, `ref BrainBlackboard`. ⭐ **The row would have sent whoever picked it up to delete a method three shipped assets reach** |
| ⛔ **`025` is cited as both SUPERSEDED and BLOCKING, in one document** | §I4 *"(Supersedes the `DEBT-AIB-025` deferral.)"* vs §E2 *"blocked by I4; tracked `DEBT-AIB-025`"* ⇒ ⚠ **one of them is stale and the reader cannot tell which** |

⛔ **Neither is a live correctness defect**, so neither earns its own Batch-79 item under the handoff's
STOP — ⭐ **both are documentation that would mislead the next reader**, which is what pricing is for.
