# HANDOFF — Batch 74: **HSM's runtime catches up with its authoring** — `BP-281` · `E7b` · BTree emit · `InspectorWindow`

> 📌 **Dispatched at `6c49dc9db`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 73 MERGED at `0808253e4`** — gates re-run by me, blueprint golden set
> untouched, Examples.Scenarios now in the gate set at **56 / 68, 12 named quarantines**.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⛔⛔ **`E3` IS OUT — it is now blocked on 📄 [`Architect_Question_35`](Architect_Question_35_Hsm_Occurrence_Delivery.md)**, which your census forced me to
> write. ⭐⭐ **Your census changed the answer**: measured against it, **the delegate need not widen at
> all.** ⛔ **Do not start `E3`.**
>
> ⭐⭐⭐ **This batch is one theme:** *HSM's authoring model is ahead of its runtime.* **Three of the four
> items are a feature you can AUTHOR today that reaches nothing at runtime.**

---

## 0. ⭐⭐⭐ Batch 73 — **you were right, I was wrong, and the harness fix was the real work**

| | |
|---|---|
| ⛔⛔ **my `_ByHSM` hypothesis was WRONG, and you disproved it properly** | 📐 the scenario throws at **phase 2 (tick 21)** and `ExitWith(1)` ends the run ⇒ **tick 25 never happens; phase 4 is never evaluated.** ⭐⭐ **A casualty, not evidence.** 📌 **My error has a name: I attributed a failure from its NAME** — the label said `_ByHSM`, the mechanism said phase 2. ⚠ **Same shape as reading *"is it used?"* for *"is it wanted?"*** |
| ⭐⭐⭐ **fixing the harness FIRST was the right instinct** | *"`ExitWith(1)` — the same code for every phase"* ⇒ a red could only say *"exit 1"*. ⭐ **You made the gate able to speak before asking it a question**, and got both diagnoses in one run each. ⛔ **I did not ask for that; it was the correct read of *"a red that names no cause trains everyone to ignore the gate."*** |
| ⭐⭐ **0 fixed / 12 quarantined was RIGHT** | both causes are outside this programme, and ⭐ **every skip carries phase + message + subsystem + "identical on `5d01a5c`"** ⇒ **a future session can act on it without re-measuring** |
| ⭐⭐⭐ **the generated-code tier's acceptance test PASSES** | reverting `E6` reddens it at the id line, and a **second test derives the id independently of the generated text** ⇒ ⛔ **the two sides cannot agree by construction.** That is the strongest form this gate could take |
| ⭐ **and `E3` escalated a second time — correctly** | ⭐⭐ **the census is what made `Q35` possible.** Without those five emitter sites I would have specified `A` again |

---

## 1. 🔴🔴 `BP-281` — **HSM has no `ParseParams` counterpart.** ⭐ *Author an input; nothing supplies it*

> ⭐ **Your own finding, Batch 71:** *"An HSM `Role=Input` variable reaches NO emitted output. Not the
> topology core, not the registrar. `HsmBridgeEmitCore` emits a slot manifest and **no params handling
> of any kind**."* ⇒ ⛔ **`DEBT-AIB-021`'s fix had nothing to fix on this host.**

⭐⭐ **This is Track E's thesis in its purest form** — 📄 plan §4B: *"HSM's authoring model is ahead of
its runtime."* You can declare `Role=Input`, it round-trips, the editor shows it in its section — **and
at runtime it is never written.**

### ⭐ The shape is already decided — **do not re-derive it**

📄 **`DESIGN_Parameter_Model.md` §3** — one pipeline for every host.

| | |
|---|---|
| ⭐⭐ **the delegate is `ParseParamsDelegate`, unchanged** | `(string json, byte* memory, EntityRepository world, Entity self, IHostVariableAccess? host)` ⛔ **no second delegate type** *(ruling 9)* |
| ⭐⭐⭐ **mirror the BTree bridge, do not invent** | `BTreeBridgeEmitCore.EmitParseParamsLocal` **as it now stands after `DEBT-AIB-021`** — ⭐ **baked defaults first, then the incoming JSON overlays per variable by name**, unknown keys **ignored** *(the decision test exists; match it)* |
| ⚠ **emit whenever there is ≥1 packed managed variable** | ⛔ **not "≥1 default"** — ⭐ **that was defect (b) of `-021`, and copying the old guard would reproduce it on a second host** |
| ⭐ **and the `JsonSerializerOptions` field's guard too** | 📌 **defect (c)** — the same key, one level up |

🔴 **STOP conditions**

| | |
|---|---|
| ⭐⭐ **where does the destination pointer come from?** | 📐 **`-021`'s BTree path writes into the behaviour's params area.** ⚠ **If HSM's answer depends on `E3`'s storage move, say so and build the ROOT case only** — a root HSM behaviour has one params area, and that is not blocked |
| ⭐ **invert the gap test, do not delete it** | you asserted this absence deliberately in Batch 71. ⛔ **Invert it** — Batch 70's rule |
| ⚠ **the HSM emit baseline WILL move** | ⭐ expected; **show the diff is only the new params emission** |

**rails:** ⭐ an HSM asset with a `Role=Input` variable and a default gets that default **at activation**
· ⭐⭐ **an incoming JSON overlay wins over the default, per variable, others untouched** · ⭐ **an asset
whose inputs have NO defaults still gets a working `ParseParams`** *(the `-021`(b) rail, on this host)*.

---

## 2. ⭐⭐ `E7b`'s runtime half — **`ExpressionTargetField` is emitted NOWHERE**

> 📐 **Your measurement:** 0 occurrences in `HsmEmitCore` **and** `HsmBridgeEmitCore` ⇒ ⛔ **it never
> reaches the blob, so there are no bytes to assert.** ⭐ **And my `E3` guess was wrong** — it is not
> blocked on the occurrence key.

⭐ **The authoring half is complete and has been for a while:** `HsmAssetMapper:114/135` round-trips it ·
`HsmCommandSink:249` maintains it · **`HsmValidator:394` already treats it as a writer style** ·
⭐ Batch 71 made `CountNodesReferencingVariable` count it. ⇒ ⛔⛔ **a producer with no consumer** —
the shape this programme keeps filing.

| | |
|---|---|
| ⭐ **what it means** | *"the blackboard field that receives the expression **result** of `ActionFunction`"* — ⛔ **an OUTPUT binding**, not input wiring |
| ⭐⭐ **both hosts have it** | BTree **per node**, HSM **per transition** ⇒ ⭐ **BTree is the template again — check what the BTree side emits and mirror it** |

**rail:** ⭐⭐ **the named variable actually receives the expression result — assert the BYTES**, not the
binding. ⭐ Plus: the validator's writer-style walk *(`W7c`'s subject)* and the runtime now agree about
what writes that variable.

🔴 **STOP** if emitting it needs a per-transition storage location that does not exist — ⭐ **that is
`E3`-shaped and I want to know**, not have it worked around.

---

## 3. ⭐ BTree's emit tier — **over the REAL solution compilation**

> ⭐ **Your measurement, and it is the whole specification:** `BTreeJsonGenerator` builds
> `structSizeResolver` from the **semantic model** and runs `BTreeDeactivatorScanner.Scan` over **real
> method bodies** ⇒ ⛔ **a synthesized compilation emits fallback output — *a baseline of what
> production never produces*, the trap `GoldenCorpus.Options()` already records.**

⇒ ⭐ **So the harness must build against the real compilation.** ⚠ **How is yours to choose** — a
reference to the real assemblies, a `Compilation` assembled from the actual project, whatever the
generator genuinely needs.

🔴 **STOP if that is not achievable in this batch's room** — ⭐ **it is the least important of the four**
and *"named boundary, not built"* is an acceptable outcome for it. ⛔ **Do not synthesize a compilation
and baseline the fallback** — that is worse than no tier.

**rail:** ⭐ **the same acceptance test shape as the HSM tier** — mutate something the generator bakes
*(a struct size, a deactivator)* and show the baseline reddens. ⛔ **A green new tier proves nothing.**

---

## 4. ⭐ Retire `InspectorWindow`'s "STATIC PARAMETERS"

⭐ **Carried since Batch 69 and never scheduled.** 📄 The parameter model rules that **sections are the
classification** *(`DESIGN_Parameter_Model.md` §5.1)* and Track C built that panel ⇒ ⛔ **a separate
"STATIC PARAMETERS" block in the inspector is the second surface for one concept** *(ruling 9)*.

⚠ **`2026-08-15`'s rule applies before you delete anything:** ⭐⭐ **search `.dev/` for a design record
first.** ⛔ **If a record says it is a designed-but-unbuilt capability, ROUTE it rather than delete it,
and say so** — that is exactly the `BTreeTick` case.

---

## 5. ⛔ NOT in this batch

⛔⛔ **`E3`** *(blocked on `Q35`)* · ⛔⛔ **blueprint multi-occurrence** *(deferred by the user)* ·
**`E5`** *(rides `E3`'s mechanism, and needs `-028`(a))* · **`E7a`** *(`IHostVariableAccess` stays
declared-only)* · the 12 quarantined scenario tests *(⛔ **out of programme — do not adopt them**)* ·
the Track C **visual check**.

---

## 6. Gates

**Baseline — coordinator-verified at `0808253e4`:** build **0 / 69** · Blueprints **3690 / 3680 / 0 / 10** ·
AiShared **1280** · BTree.Editor **615** · Breakpoints **134** · Generators **249** · Hsm.Editor **543** ·
AiEditor.Persistence **136** · **Examples.Scenarios 56 / 68 (12 skipped)** · Examples.UrbanCombat **29** ·
Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 61 / done 165**.

| | |
|---|---|
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash` |
| ⭐ **expected movement** | **item 1 moves the HSM emit baseline** *(new params emission)* · item 2 may · ⭐ **item 3 CREATES a BTree emit baseline.** ⛔ **Say which files moved in which commit** |
| ⚠ **the quarantine count must not grow** | ⭐ **12 skipped is a number I will check.** ⛔ **A new skip is a finding, not a fix** |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ neither red nor green is evidence — `DEBT-AIB-030` |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 7. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐⭐⭐ **item 1** — ⭐ **did you reproduce `-021`'s defects (b) and (c) by copying the BTree guard, or
avoid them?** *(either answer is fine; I want to know)* · **where the destination pointer comes from**,
and whether the root case was enough · the inverted gap test.
⭐⭐ **item 2** — **the bytes assertion** · whether emission needed anything `E3`-shaped.
⭐ **item 3** — **the acceptance test: a mutation reddens it** · or the named boundary if you stopped.
⭐ **item 4** — ⭐⭐ **what `.dev/` says**, before what you did.
**Always:** ⭐ **every id you allocated** · **which `DEBT-AIB` rows this batch touched** ·
**the quarantine count**.

⭐⭐⭐ **Seven batches, a finding every time.** ⭐ **Batch 73's was that a gate which cannot name its
cause is not a gate** — ⛔ **that generalises past the scenario harness, and it is worth carrying into
every rail you write this batch.**
