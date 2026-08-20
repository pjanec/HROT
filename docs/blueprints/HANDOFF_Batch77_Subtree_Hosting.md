# HANDOFF — Batch 77: **`E5` — subtree hosting** · an `E3` tripwire · `BP-304`

> 📌 **Dispatched at `b66b7448a`.** Frozen per rule 1.
> ⭐⭐ **Rule 1b: push `chore: started batch 77 at <sha>` before writing any code.**
> ✅ **Batch 76 MERGED at `a8b3106ba`** — gates re-run by me, goldens untouched.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⭐⭐⭐ **`E5` is the item the whole Track-E queue was pointing at** — *HSM + BTree/blueprint on one
> entity*, which is the user's stated goal. ⭐ **And it is UNBLOCKED: its `E3` dependency was stale.**

---

## 0. ⭐⭐⭐ Batch 76 — **four instances, and a gate that was lying to us**

| | |
|---|---|
| ⭐⭐⭐ **I asked about two neighbours; you found THREE more** | `SaveHistory` recorded a **bystander region's** leaf as history · `RestoreHistory` at **all three** exits · `RestoreDeepHistory` propagated the `0` into every nested restore. ⭐ **One defect, four instances** |
| ⭐⭐ **and the index was already known** | *"`SelectTransition`'s loop is `for (r = 0; r < regionCount; r++)`, and the winner is chosen inside it"* ⇒ ⭐ **returned via `out`, assigned at the same statement as `bestTransition`, so the two cannot disagree.** ⛔ Not re-derived at the call site |
| ⭐⭐⭐ **the `Fhsm.Tests` gate finding** | 📐 **the project is not in the solution**, so `--no-build` tests a **stale bin** — ⭐ **which is how a suite stays outside the gate set AND how it produces a false regression on the way in.** 📐 **I verified your two reds in a worktree: pre-existing, 296/298 before.** ⭐ **Correctly not adopted** |
| ⭐⭐ **the rail asserts BOTH halves** | *"either alone passes a wrong fix: mover-only passes a fix that writes every slot; bystander-only passes one that writes none."* ⭐ **That reasoning belongs in every occurrence rail from here on** |
| ⭐ **and probe Q, reported though green** | *"a green probe that proves nothing is worth saying out loud."* ⚠ **Depth-1 was too shallow to break anything** — ⭐ **exactly the kind of thing that otherwise reads as coverage** |

---

## 1. ⭐⭐⭐ `E5` — **subtree hosting runtime.** ✅ *Unblocked; here is why*

> 📐 **The plan said `depends on: E3`. That is STALE, and I corrected it.** ⭐⭐ **Hosting provisions at
> STATE ENTRY, where the region and state are in scope** ⇒ ⛔ **it does not need `E3`'s delivery
> mechanism**; it needs the **storage route**, which already ships.
> 📄 **`Architect_Question_34` §7 — RULED: provision by KEY, ⛔ NEVER through `AttachToEntity`.**
> 📄 **`Architect_Question_33` §1.5.4 — RULED: a subtree is HOSTED, not slotted; `C` over `A`.**
> 📄 **`DESIGN_Hsm_Storage_Model.md` §4.**

### 📐 Ground truth — measured, do not re-measure

| | |
|---|---|
| ✅ **`StateNode.SubtreeAssetId` persists** | `-028`(a), Batch 75 — round-trips through `HsmAssetDto` + both mapper directions, **omitted when empty** |
| ✅ **rules 8/8b are live on loaded assets** | `-028`(a) + **`BP-299`**'s `OwnerStableId`, and **`-029`**'s descendant walk |
| 🔴 **and it is still read by NOTHING at runtime** | 📐 **0 hits in `FDP/ExtDeps/FastHSM/src` and 0 in the HSM emitters.** ⇒ **that is this item** |
| ⭐ **the storage route** | `ComputeStatefulSlotKey(assetId, StatefulSlotScope.Node, occurrence, variableId)` + `BlueprintBlackboardPartitions` — ⭐ **the same allocator `E1`/`E2` already use for HSM state** |

### ⭐ The lifecycle, from the pre-written rail

📄 **plan §4B, `E5`:** *"a state hosting a subtree: **entry** provisions + resolves · **tick** re-enters ·
**exit** invalidates the cursor · **completion** raises the event. ⭐ **Plus: a LATENT child suspends and
resumes across ticks.**"*

| | |
|---|---|
| ⭐⭐ **the occurrence is the HOSTING STATE** | so two states hosting one asset get two slots, and a re-entered state gets **the same** slot |
| ⭐ **resolve-before-commit** | 📄 `DESIGN_Parameter_Model.md` §3.3 — a failed resolve must leave the host **without** the child, not half-attached |
| ⭐ **the child's params may come from the host** | ⛔ **NOT this batch** — that is `E7a`, and `IHostVariableAccess` stays declared-only. **Pass `null`** |

### 🔴 STOP conditions

| | |
|---|---|
| ⛔⛔ **`AttachToEntity` is the WRONG route, and it will look tempting** | ⭐ it is the shipped, tested path for Instance blueprints — ⚠ **and it keys by `blueprintId` in the slot table, which is `Q34`'s deferred problem.** 📄 **`Q34` §7 ruled hosting goes by KEY precisely to avoid inheriting it.** 🔴 **If hosting genuinely cannot work by key, STOP** — that reopens `Q34`, and it is not an implementation detail |
| ⚠ **the honest inherited limit — ASSERT it** | ⭐⭐ **a hosted subtree's OWN actions still resolve DTOs at baked offsets** ⇒ **the `E3` hazard is inherited.** ⛔ **Do not fix it here** *(zero instances — see item 2)*; ⭐ **assert it as a named gap so it inverts when `E3` lands** |
| ⚠ **latency** | ⭐ **a LATENT child must suspend and resume** — 📌 the plan's `E5` rail says so explicitly, and ⛔ **`Q33`'s finding is that a blueprint hosted as an ACTION cannot suspend** *(`StateStructBase` 8 vs 16)*. **Say which shape you host** |
| 🔴 **if the FastHSM kernel needs a public API change** | ⭐ **name every consumer before changing it** *(the `E3` census is the template)*; ⛔ **additive is fine, breaking is a STOP** |

**rails:** the four lifecycle phases above · ⭐ **two states hosting ONE asset get DISTINCT slots**, and
⭐⭐ **a re-entered state gets the SAME slot** *(the `E3` rail's both-halves discipline: the mover moved
AND the bystander is untouched)* · ⭐ a failed resolve leaves the host **unhosted** · ⛔ **the 100-byte
tail is untouched.**

---

## 2. ⭐⭐ An `E3` **TRIPWIRE** — ⛔ *do not manufacture a subject*

> ⭐ **My decision, `2026-08-17`, and it follows your own measurement:** `E3` has **zero instances**, so
> ⛔ **inventing a `[SharedAiAction]` in `Hrot.AI.Behaviors` just to give it one would be building for a
> demand nobody has expressed** — the exact thing the `2026-08-17` user ruling is about.

⭐⭐⭐ **Instead, make the hazard announce itself the day it becomes real:**

| | |
|---|---|
| ⭐ **the assertion** | **no DTO-bound HSM action** *(`[SharedAiAction]`/`[SharedAiCondition]` reachable by `HsmActionGenerator`)* **exists in a generator-bearing assembly while `E3` is unbuilt** |
| ⭐⭐ **why this shape** | ⛔ **a latent hazard nobody can see is the worst kind this programme has hit** *(it is `W3`'s shape, and `E6`'s)*. ⭐ **This converts it into a build failure with a pointer to `Q35`** |
| ⭐ **and it must POINT** | the failure message names **`Q35` (resolved)** and **`DESIGN_Hsm_Storage_Model.md` §3** ⇒ whoever trips it finds the rulings already made, not a puzzle |
| ⚠ **scope it honestly** | ⭐ **it is a tripwire, not a ban.** If the message reads as *"you may not do this"*, rewrite it: it means *"this now needs `E3`, which is designed and ready"* |

🔴 **STOP** if you cannot express it without a fragile assembly scan — ⭐ **say so, and put the same
sentence in `HsmActionGenerator`'s XML doc instead.** ⚠ **A vacuous tripwire is worse than none**, and
this programme has six of those on record.

---

## 3. ⭐ `BP-304` — **the two `Fhsm.Tests` reds**

⭐ **Now that the suite is in the gate set, an unexplained red trains everyone to ignore it** — ⛔ the
Batch-73 lesson, and it applies to a suite of **300** the same way it applied to one of 68.

| test | what I want |
|---|---|
| `OrthogonalRegionTests.OutputLane_Conflict_Detected` | ⭐ **it explains itself in-file** *(`SetTraceBuffer` removed in `behav-diag-1`; needs an `HsmTraceContext` rewrite)* ⇒ **quarantine with that cause recorded**, or fix it if the rewrite is small |
| `FailSafeTests.InfiniteLoop_Detected_And_Stops` | ⚠ **unexplained** ⇒ ⭐⭐ **diagnose it.** 📌 **It is the RTC fail-safe** — ⚠ **and item 1 of the last batch changed the region-loop neighbourhood**, so ⭐ **confirm against `6a6bdc6` that it is genuinely pre-existing** *(I verified it is, in a worktree — ⭐ but you are closer to the code)* |

⛔ **Do NOT adopt either into this programme's scope if the cause is outside it** — ⭐ **quarantine with
a named cause is the accepted outcome**, exactly as with the 12 scenario tests.

---

## 4. ⛔ NOT in this batch

**`E7a`** *(needs `E5`'s host — next)* · ⛔⛔ **`E3`** *(latent; item 2 is the tripwire, ⛔ not the fix)* ·
**`E7b`'s bytes** *(same blocker)* · ⛔⛔ **blueprint multi-occurrence** *(user-deferred)* · ⛔ **wiring
the producer picker** *(parked)* · the 12 quarantined scenario tests · the Track C **visual check**.

---

## 5. Gates

**Baseline — coordinator-verified at `a8b3106ba`:** build **0 / 69** · ⭐ **FastHSM 298 / 300** *(2
pre-existing reds, `BP-304`)* · Blueprints **3691 / 3681 / 0 / 10** · AiShared **1289** · BTree.Editor
**615** · Breakpoints **134** · Generators **266** · Hsm.Editor **551** · AiEditor.Persistence **136** ·
Examples.Scenarios **56 / 68 (12 skipped)** · Examples.UrbanCombat **29** · Toolkits **1964** ·
NodeEdit **208 / 131** · tracker **open 63 / done 176**.

| | |
|---|---|
| ⭐⭐⭐ **`Fhsm.Tests` takes NO `--no-build`** | ⛔⛔ **your finding, now a standing gate rule** — the project is not in the solution, so `--no-build` reports a **stale bin**. ⭐ **Three gates now share this: NodeEdit ×2 and FastHSM** |
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash` |
| ⭐ **expected movement** | ⚠ **I am NOT predicting one this time** — ⭐ **three batches running my golden-movement predictions were wrong for the same structural reason** *(the HSM golden hashes checked-in assets, not emitter output)*. **Report what moved and why** |
| ⚠ **the quarantine count** | 12 scenario + whatever item 3 decides for `BP-304`. ⭐ **State the new number explicitly** |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ neither red nor green is evidence — `DEBT-AIB-030`, **and the identity rotated WITHIN a single batch last time** |
| **per-item revert-goes-red** · `tracker-counts.py --check` | |

---

## 6. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐⭐⭐ **item 1** — ⭐ **which route you provisioned by**, and that it is **not** `AttachToEntity` ·
**two hosts of one asset get distinct slots; a re-entered host gets the same one** *(both halves)* ·
⭐ **what shape you host, and whether a latent child suspends** · the inherited-`E3` gap, asserted ·
any FastHSM API you touched and whether it stayed additive.
⭐⭐ **item 2** — ⭐ **does the tripwire FAIL if you add a DTO-bound HSM action to a generator-bearing
assembly?** *(show it — ⛔ otherwise it is the seventh vacuous rail)* · the failure message.
⭐ **item 3** — the cause of each, and **fixed / quarantined-with-cause**.
**Always:** ⭐ **the started-marker sha** · **every id you allocated** · **which `DEBT-AIB` rows you
touched** · ⭐ **both quarantine counts**.

⭐⭐⭐ **Ten batches. The last four each corrected a premise of mine** — *signature widening · the
dangerous case with no instances · two neighbours that were four · a dependency that was stale.*
⭐ **That is the protocol working, not failing.** ⛔ **Keep it up.**
