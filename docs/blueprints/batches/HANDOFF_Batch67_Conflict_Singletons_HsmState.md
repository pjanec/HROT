# HANDOFF — Batch 67: **the conflict rule's blind spot · service singletons · HSM state, end to end**

> 📌 **Dispatched at `bcebcc09b`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 66 MERGED at `3ed92905a`** — gates re-run, all matching.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⭐⭐ Batch 66 — what I accepted, and one thing I owe you

| | |
|---|---|
| ⭐⭐⭐ **`G4` grew and you were RIGHT to grow it** | I specified only the design's *"duplicate name = hard error"*. **You found the silent failure underneath**: the id is FNV-1a-32 of the name, so **two distinct names can hash to one id**, and the first behaviour then resolves to the second's topology. ⇒ **`W1`'s defect on the other registry.** ⭐ Transplanting `BlueprintRegistry.RegisterDirect`'s shape was right |
| ✅ **the throwing default is ACCEPTED** | a silent no-op there is a **lost edit** — the exact class the method removes. ⚠ **Residual risk recorded:** a FUTURE production wrapper that forgets to delegate fails at **runtime**, not compile time ⇒ **item 5 closes it** |
| ⭐⭐ **your `Fdp.Toolkits` diagnosis is CONFIRMED independently** | my two consecutive full runs failed on **two DIFFERENT tests** (`GizmoRegistryTests`, then `StatelessGizmoRegistryTests`), both **green in isolation (8/8)**. ⇒ **3 of 6 full runs red across 65–66, all registry-shaped.** ⭐ **Gate rule changed**: a full-suite red there is **not signal by itself**, and ⛔ **a full-suite green is not evidence either** |
| 📐 **I owed you `W7`'s re-derivation — it is done** | 📄 **`PLAN_Remaining_Work.md` §4i.** ⛔ **`W6` is DROPPED.** Items 1–2 below are what survives |

---

## 1. 🔴🔴 `W7c` — **the conflict rule has a COVERAGE HOLE** ⭐ *the most valuable item here*

📄 **Design: `.dev/_DONE/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md` §9.2 + §9.5.**
📄 **My re-derivation: `PLAN_Remaining_Work.md` §4i.**

⭐ **Most of §9 is BUILT** — rule 9 (`HsmValidator.CheckBlackboardRegionConflicts`) exists **and is wired
at production** (`HsmGraphModel:43`), unlike rules 8/8b.

| 🔴 **the hole** | measured |
|---|---|
| rule 9 iterates **`GetAliasesFor` ⇒ `BlackboardAliasBinding` only** | ⛔ **the `[SharedAiAction(typeof(Dto),"Field")]` static-offset binding style is NOT covered** |
| §9.5's **Sync-Out** bindings are never enumerated as writers | `SubtreeSyncBinding.SyncOut` exists; **the validator never reads it** |

⇒ ⭐⭐ **A rule that covers one binding style READS AS GUARDED while leaving the other open.**
⚠ **`BP-240`'s shape a third time** — green because of what it happens to look at.

**§9.2 says the writer set is *"every action method that mutates this variable"*** — ⛔ **not "every
alias"**. ⇒ **widen the enumeration to the union: aliases · static-offset bindings · Sync-Out.**

⭐ **Reuse, do not invent:** `HasWritingAction` already classifies writers correctly per §9.6
*(conservative on `_schema == null`, on unknown FQN, and on any non-`ReadOnly` access)*. **Only the
enumeration is short, not the classification.**

**rail:** ⭐ **two regions writing one variable through the STATIC-OFFSET style produce the diagnostic**
*(it does not today)* · **and two Sync-Out bindings to one master variable produce it** (§9.5).
**impact:** editor validation only. ⛔ **`StructureHash` / `persistence-shape` MUST NOT move.**

---

## 2. `W7a` — **the suppression is authored, persisted, emitted… and the rule never reads it**

| | measured |
|---|---|
| ✅ **built and round-tripped** | `_conflictSuppressions`, `HsmAssetMapper`, `HsmAssetProjector`, emitted as `.SuppressBlackboardConflict(var, writerPair)` — **on BTree assets too** |
| 🔴 **but** | `IsConflictSuppressed` is consulted **only** by `BlackboardAliasDropValidator:43` ⇒ **suppressing silences the DROP TARGET while the PANEL WARNING persists** |

⇒ ⚠ **An affordance that half-works is worse than one that is absent** — the designer clicks *Suppress*
and nothing appears to happen.

⭐ **§9.3 is explicit: suppression is PER-PAIR, not per-variable.** ⛔ **Do not collapse it to the
variable** — *"a new aliasing relationship on the same variable would surface a fresh diagnostic."*

**rail:** a suppressed `(variable, writerPair)` **produces no rule-9 diagnostic**, and ⭐ **an
unsuppressed pair on the SAME variable still does.**
**impact:** editor only. ⛔ **hash / persistence MUST NOT move.**

---

## 3. `G3` — make the geo transform reachable **from a resolver**

📄 **`DESIGN_Parameter_Model.md` §6** · resolver design `G3`.

| | measured |
|---|---|
| **the need** | `G6` retired `AiBehaviorFactory`, so a **JSON- or blueprint-authored** resolver has **no closure** to reach these through. `ParseParamsDelegate` gets `world` + `self` and nothing else |
| ⛔ **today** | `IGeographicTransform` is **constructor-injected** — `GeographicModule(IGeographicTransform)`, `CoordinateTransformSystem(IGeographicTransform)`. ⛔ **Not reachable from the world** |
| ⭐ **the precedent** | **`NetworkEntityMap` is already singleton-shaped** *(`SingletonRenderers.cs:295` renders it as one)*. ⇒ **mirror whatever mechanism it uses; do not coin a new one** |
| ⛔ **the correction to carry** | ⚠ **`BlueprintRegistry.RegisterWorldSingleton(blueprintId, tier)` is NOT this** — it registers **a blueprint to tick as a singleton**. ⭐ **I got that wrong once; do not repeat it** |

**STOP:** 📐 **if the two are genuinely different mechanisms, say so and propose one** — ⛔ **do not build
a third.**
**rail:** a resolver reaches the geo transform **through the world/view**, with **no constructor
injection and no closure**.
**impact:** runtime wiring. ⛔ **hash / persistence MUST NOT move.**

---

## 4. ⭐⭐⭐ `E1` + `E2` — **HSM authored state variables, end to end** *(Track E's entry point)*

📄 **`PLAN_Remaining_Work.md` §4B.** ⭐ **User ruling: *"if something is not present in HSM, it is not
because it is not needed, just not implemented yet."***

| | measured |
|---|---|
| 🔴 **`E1`** | `HsmEmitCore` + `HsmBridgeEmitCore` contain **0** `Role`/`Scope` references; `BTreeBridgeEmitCore` contains **45**. `HsmBlackboardVariableDto` persists both faithfully ⇒ ⛔ **HSM has NO authored variables at runtime at all** |
| **`E2`** | provisioning — adopt **`ComputeStatefulSlotKey` + `BlueprintBlackboardPartitions`**, the allocator **Instances and the BTree bridge already share** |
| ⭐ **the template** | `BTreeBridgeEmitCore:203–239` — the key switches on `WorkingStateScope.Node` / `Behavior` / `Entity`, and `nodeVisualId` is consumed **only** for `Node`. ⛔ **Adopt this algorithm; a second key algorithm fails the rail** |

⭐ **Why both in one item:** `E1` alone emits a manifest **nothing provisions** — dead data. ⇒ **ship the
pair, or ship neither.**

### 🔴 STOP — **the corpus decision, which you must STATE not assume**

⛔⛔ **Measured: `persistence-shape.txt` is 43 assets, ALL `.bp.json`. `grep -ci "hsm\|btree"` ⇒ 0.**
⇒ ⭐⭐ **The golden corpus does not cover HSM at all**, so this item changes emitted output and **no
golden gate would notice.** ⚠ **`BP-240`'s shape inverted — green because the corpus lacks the thing.**

📐 **Choose and say which: (a) extend the corpus to HSM assets, or (b) accept unit-test-only cover.**
⛔ **Do not let it pass silently.** ⭐ **If (a) looks large, (b) plus a stated follow-up is acceptable.**

**rails:** an HSM asset declaring a `Role=State` variable **emits a slot-manifest entry whose key matches
BTree's algorithm for the same inputs** · **N state variables ⇒ N slots provisioned and zeroed at
activation, through the production ingress path**.
**impact:** ⭐ **HSM emitted output CHANGES.** ⛔ **`.bp.json` hash / `persistence-shape` MUST NOT move** —
if they do, you touched the blueprint path by accident.

---

## 5. ⭐ The rail I owe from Batch 66 — small

**Assert every NON-TEST `IEntityCommandBuffer` implementer overrides `SetComponentFieldRaw`.**
⭐ **Why:** the throwing default is accepted, but it turns *"a production wrapper forgot to delegate"*
from a **compile error** into a **runtime throw**. ⇒ **a reflection test closes the hole.**
⚠ **Test mocks are exempt by design** — they must not be forced to implement it.

---

## 6. ⏭ Carried — ⚠ **second time; take it if the run has room, or tell me to stop carrying it**

**The latency rail.** 🔴 `BTreeEvaluate` emits `return TickCore(…) == NodeStatus.Success;` ⇒ **`Running`
maps to `false`**, so **a latent CONDITION silently reads false while it waits**, then flips true later
with `__phase` left mid-sequence. ⛔ **Silent wrong behaviour.**
⭐ **Rule: latency is legal iff the hosting can RE-ENTER** — ⛔ `Condition` → `BTreeCondition`/`HsmGuard`
**never** · ✅ `Action` → `BTreeAction` · ✅ `Action` → HSM Activity/subtree · ⛔ `Action` → HSM
Entry/Exit/Timer. ⭐⭐ **A third dimension on `V_DispatchKindCompatibility`**; ⭐ **the detector already
exists** — `MacroLatency.IsLatent`, used by `BP1661`.
📌 **The `Condition` row is fully specified today; the HSM rows are speculative until `E5`.**

---

## 7. ⛔ NOT in this batch

`W7b` *("Allow concurrent writes" — UX, after `W7c`/`W7a`)* · `E3`–`E7b` · the rest of Track C
*(table, dialog, Watch, `C-outline`)* · the Instance params seam · multi-occurrence · `G7`+`W10`.

---

## 8. Gates

**Baseline — coordinator-verified at `3ed92905a`:** build **0 / 69** · Blueprints **3638 / 3628 / 0 / 10** ·
AiShared **1216** · BTree **612** · Breakpoints **134** · Generators **196** · Toolkits **1951**
*(see below)* · NodeEdit **208 / 131** · tracker **open 61 / done 133**.

| | |
|---|---|
| ⭐⭐ **`Fdp.Toolkits.Tests` — NEW RULE** | a **full-suite red is not signal by itself.** ⭐ **Confirm with `--filter`/isolation before calling it a failure**, and ⛔ **do not present a full-suite green as evidence either.** 📌 `DEBT-AIB-030` / `DEBT-AIB-010` |
| 🔴 **`.bp.json` `StructureHash` unchanged for all 43** | ⚠ **item 4 changes HSM output — the blueprint corpus must NOT move** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| ⭐ **per-item revert-goes-red** · `tracker-counts.py --check` · **the two NodeEdit gates take NO `--no-build`** | |

---

## 9. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.** ⭐ **It is working: I re-ran the
NodeEdit pair, the suites your diff could reach, and Toolkits for a second sample, and accepted the rest
on your table.**

**Per item:** 🔴 **item 4's corpus decision — (a) or (b), stated** · ⭐ **whether `W7c`'s rail failed
before the change** *(it should — the hole is real)* · ⭐ **whether `G3` found one singleton mechanism or
two** · **`StructureHash` unchanged, stated FIRST** · `tracker-counts.py --check` · ⭐ **every id you
allocated**.

⭐⭐⭐ **The question to carry — you started it, please finish it.** Batch 66 gave the `DEBT-AIB` triage.
📐 **Of the ~22 unresolved rows, which sit inside Track C, the parameter seam, or Track E?** ⛔ **Do not
fix them — NAME them**, so I fold them into the plan instead of rediscovering them one batch at a time.
⭐ **Two have already paid for themselves this way** (`DEBT-AIB-030`'s race, and the `-012` mis-citation).
