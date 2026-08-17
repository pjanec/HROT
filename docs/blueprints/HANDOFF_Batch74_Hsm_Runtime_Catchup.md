# HANDOFF — Batch 74: **only what the runtime already supports** — `E7b` · BTree emit · `InspectorWindow` · park the picker

> ⛔⛔ **AMENDED AND RE-STAMPED `2026-08-17` under rule 1a** — ⭐ **ancestry checked: the original
> dispatch sha `6c49dc9db` was NOT in the implementation branch's history**, so no run was in progress.
> 📌 **RE-DISPATCHED at `8f3de52b8`.** ⭐ **The original item 1 (`BP-281`) is REMOVED — see §0b.**
>
> ✅ **Batch 73 MERGED at `0808253e4`** — gates re-run by me, blueprint golden set untouched,
> Examples.Scenarios now in the gate set at **56 / 68, 12 named quarantines**.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⛔⛔ **`E3` IS OUT — blocked on 📄 [`Architect_Question_35`](Architect_Question_35_Hsm_Occurrence_Delivery.md)**, which your census forced me to
> write. ⭐⭐ **Your census changed the answer**: measured against it, **the delegate need not widen at
> all.** ⛔ **Do not start `E3`.**

---

## 0b. ⛔⛔ USER RULING `2026-08-17` — **build only what the runtime already supports**

> ⭐⭐⭐ **User, verbatim:** *"lets amend the batch to just with what we already support, we can design
> the missing hsm stuff while that batch is being implemented."*

📌 **What prompted it — and it is a finding about MY scoping, not yours.** The user asked whether this
programme is *"building authoring for a not-yet-ready runtime."* 📐 **I measured, and one case is ours:**

| | measured `2026-08-17` |
|---|---|
| 🔴🔴 **`G7`+`W10`'s producer picker (Batch 70)** | `ProducerPicker.Persist()` is documented *"what to write to the asset"* ⇒ ⛔ **nothing calls it.** **0** references to `ProducerPicker`/`ProducerCatalog` outside their own folder · **0** asset fields storing a producer FQN · ⛔ **and the runtime it would feed — the blueprint-authored resolver (`R1`/`R2`/`R4`, resolver design §8.1) — is not built either** |

⇒ ⭐⭐ **A complete, well-tested component with NO CALLER ON EITHER SIDE** — the *producer with no
consumer* shape this programme keeps filing, **created by us, and I scoped the batch that did it.**
⚠ **The batch report told me it was scoped to "the picker and its catalog" and I read that as a healthy
narrowing rather than as shipping half a seam.**

### ⇒ ⛔ `BP-281` is PULLED from this batch

⭐ **Not because it is wrong — because its DESTINATION is undecided.** 📐 **There is no HSM params
storage story at all**, so *"where does `ParseParams` write?"* is the **same question `Q35`/`E3` has
open**. ⚠ **My own STOP in the original handoff half-admitted this** *("if HSM's answer depends on
`E3`'s storage move, say so and build the ROOT case only")* — ⭐ **the user's ruling makes it explicit
rather than leaving it as a stop you would have had to hit.**

⭐⭐⭐ **The coordinator is designing that while you build this batch** — `BP-281` · `E3` · `E5` · `E7a`
are **one question** *(where do an HSM occurrence's bytes live?)*, and they will come back as one
designed piece rather than four items that each re-derive it.

### ⭐ The rule this batch adopts

> ⭐⭐ **No authoring surface ships without its consumer — or it ships EXPLICITLY ASSERTED AS INERT.**

⭐ **You already do the second half instinctively** *(`BP-281`'s gap test, `E3`'s three gap tests)*.
⛔ **The picker got no such marker, and that is the whole difference.** ⇒ **item 4.**

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

## 1. ⭐⭐ `E7b`'s runtime half — **`ExpressionTargetField` is emitted NOWHERE**

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
| ⭐⭐⭐ **why this one SURVIVES the ruling** | ⭐ **its destination already exists**: the target is a **named blackboard variable**, and `E1`/`E2` shipped HSM variable slots + provisioning. ⛔ **Unlike `BP-281`, nothing here waits on the occurrence-storage decision** |

**rail:** ⭐⭐ **the named variable actually receives the expression result — assert the BYTES**, not the
binding. ⭐ Plus: the validator's writer-style walk *(`W7c`'s subject)* and the runtime now agree about
what writes that variable.

🔴 **STOP** if emitting it needs a per-transition storage location that does not exist — ⭐ **that is
`E3`-shaped and I want to know**, not have it worked around.

---

## 2. ⭐ BTree's emit tier — **over the REAL solution compilation**

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

## 3. ⭐ Retire `InspectorWindow`'s "STATIC PARAMETERS"

⭐ **Carried since Batch 69 and never scheduled.** 📄 The parameter model rules that **sections are the
classification** *(`DESIGN_Parameter_Model.md` §5.1)* and Track C built that panel ⇒ ⛔ **a separate
"STATIC PARAMETERS" block in the inspector is the second surface for one concept** *(ruling 9)*.

⚠ **`2026-08-15`'s rule applies before you delete anything:** ⭐⭐ **search `.dev/` for a design record
first.** ⛔ **If a record says it is a designed-but-unbuilt capability, ROUTE it rather than delete it,
and say so** — that is exactly the `BTreeTick` case.

---

## 4. ⭐⭐⭐ Park the producer picker — **assert it inert, do not delete it**

> 🔴 **The finding from §0b, and it is ours.** `ProducerPicker` + `ProducerCatalog` are complete and
> tested, and **nothing on either side calls them**: no panel constructs the picker, no registrar
> supplies the catalog, no asset field stores what `Persist()` returns, ⛔ **and the runtime it would
> feed does not exist** *(`R1`/`R2`/`R4`, resolver design §8.1)*.

⛔⛔ **DO NOT DELETE IT, and do not wire it either.**

| | why |
|---|---|
| ⛔ **not delete** | ⭐⭐ **`2026-08-15`'s rule**: unreferenced ≠ unintentional. It is built to a design *(plan §4c, architect `AQ2`)* and its answers are ruled. ⚠ **Deleting removes a capability, not a mistake** |
| ⛔ **not wire** | ⭐ **that is the very thing the user's ruling forbids** — an authoring surface whose consumer does not exist. **Wiring it now repeats the mistake at a larger size** |
| ⭐⭐ **assert it INERT** | ⭐ **exactly what you did for `BP-281` and `E3`'s gaps** — a test that states *"nothing constructs this / nothing persists a producer FQN"*, ⭐⭐ **naming the consumer it waits for**, so it **inverts** when the resolver runtime lands |

⭐ **And an XML-doc line on both types** saying the same in one sentence, with the pointer — ⛔ **so the
next session that greps for callers finds the answer instead of a deletion candidate.**

**rail:** ⭐⭐ **the inert assertion FAILS the moment someone wires it** *(that is the point — it becomes
the reminder to also build the consumer)*. ⛔ **A test that merely says "it exists" is not this.**

---

## 5. ⛔ NOT in this batch

⛔⛔ **`BP-281`** *(PULLED — §0b; the coordinator is designing its destination)* · ⛔⛔ **`E3`** *(blocked on `Q35`)* · ⛔⛔ **blueprint multi-occurrence** *(deferred by the user)* · ⛔ **wiring the producer picker** *(item 4 parks it; ⚠ **building its runtime is a decision the user has not taken**)* ·
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
| ⭐ **expected movement** | **item 1 may move the HSM emit baseline** *(`ExpressionTargetField` emission)* · ⭐ **item 2 CREATES a BTree emit baseline** · items 3–4 should move **nothing**. ⛔ **Say which files moved in which commit** |
| ⚠ **the quarantine count must not grow** | ⭐ **12 skipped is a number I will check.** ⛔ **A new skip is a finding, not a fix** |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ neither red nor green is evidence — `DEBT-AIB-030` |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 7. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐⭐ **item 1** — **the bytes assertion** · ⭐ **whether emission needed anything `E3`-shaped** *(if it
does, STOP — that is the design thread I am on)*.
⭐ **item 2** — **the acceptance test: a mutation reddens it** · or the named boundary if you stopped.
⭐ **item 3** — ⭐⭐ **what `.dev/` says**, before what you did.
⭐⭐⭐ **item 4** — ⭐ **does the inert assertion fail if you wire the picker?** *(show it)* · and
⛔ **anything ELSE you notice with the same shape** — an authoring surface whose consumer does not
exist. ⭐⭐ **That sweep is worth more than the item**, and you are better placed to do it than I am.
**Always:** ⭐ **every id you allocated** · **which `DEBT-AIB` rows this batch touched** ·
**the quarantine count**.

⭐⭐⭐ **Seven batches, a finding every time.** ⭐ **Batch 73's was that a gate which cannot name its
cause is not a gate** — ⛔ **that generalises past the scenario harness, and it is worth carrying into
every rail you write this batch.**
