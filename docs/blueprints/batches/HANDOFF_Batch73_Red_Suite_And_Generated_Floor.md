# HANDOFF — Batch 73: **a red suite, a floor that sees generated code, and `E3` for real**

> 📌 **Dispatched at `d98d8ab61`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 72 MERGED at `14f8b0ea4`** — gates re-run by me, blueprint golden set
> untouched.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⛔⛔ **USER RULING: blueprint multi-occurrence is DEFERRED** — *"too many files affected, we can skip
> it, could be done sometime later once really needed."* ⭐⭐ **`Q34`'s ANSWERS STAND; only the build is
> deferred**, and **your measured edit surface is recorded in plan §4A7** so re-dispatch costs no
> re-measurement. ⛔ **Do not start it.**

---

## 0. ⭐⭐⭐ Batch 72 — **you were right about `E3`, and I was wrong in the plan**

| | |
|---|---|
| ⭐⭐⭐ **`E3` is a STORAGE MOVE — escalating was right** | 📐 **Your two measurements killed my premise properly:** the thunk dispatches through a `delegate*` whose id is a **static function pointer chosen at build time**, and it resolves its DTO at `bb.BehaviorParameters[0] + <baked offset>` in the entity's **single 100-byte `BrainBlackboard`**. ⇒ ⭐⭐ **two occurrences have one home BY CONSTRUCTION.** **Plan row corrected** |
| ⭐⭐ **and you named the route yourself** | the partition allocator under `ComputeStatefulSlotKey(…, Scope.Node, …)` — ⭐ **which is exactly what `Q34` §7 rules for `E5`** ⇒ **one mechanism, two rows.** Item 3 builds it |
| ⭐⭐ **not starting item 3 was the right call** | *"a half-applied slot-entry widening is the worst possible intermediate state"* — ⛔ **and the user has now deferred it outright**, so the judgement is vindicated twice |
| ⭐ **item 4's self-correction** | *"three delegates"* held for **canonicalize**, not **emit**. ⭐ **You corrected your own Batch-71 claim, which I had turned into a line item on that word** — item 2 is the honest version |
| 🔴 **the vacuous rail, fourth in five** | *"recomputed the rule under test."* ⭐ **You found it by probing again.** ⛔ **The pattern is not slowing down — treat every "did production do X" rail as suspect until a revert reddens it** |

---

## 1. 🔴🔴 **`Fdp.Examples.Scenarios.Tests` — 12 RED, and nobody is gating it**

> ⭐⭐ **I measured both sides: 12 failures on `HEAD`, and the IDENTICAL 12 on the pre-batch tree
> `5d01a5c2a`.** ⇒ ⛔ **NOT a Batch-72 regression.** ⚠ **You ran `Fdp.Examples.UrbanCombat.Tests`
> (29/29) — a reasonable pick, and the hole is MINE for never listing this suite.**

```
ComponentDamageScenarioTests            × 5   (incl. …_Phase4_LocomotionCleared_ByHSM)
DistributedTankScenarioPhaseATests      × 7
                                         ── 12 failed / 56 passed / 68 total
```

⭐⭐⭐ **Why this is probably Track E evidence rather than noise:**
`ComponentDamage_Phase4_LocomotionCleared_ByHSM` fails as *"the HSM did **not** clear locomotion"* —
⭐ **exactly the symptom of an HSM action silently not firing**, which is the defect `E6` just fixed.
⛔ **And it is STILL red after `E6`** ⇒ **either a second cause, or `E6`'s fix does not reach these
scenarios.** 📌 **That question is the item.**

| | |
|---|---|
| ⭐ **what I want** | **a diagnosis, not necessarily a fix.** ⭐⭐ **Name the cause for each cluster** *(the two classes may have two different causes — do not assume one)* |
| ⭐⭐ **and the gate change regardless** | **this suite joins the gate set**, red or green. ⛔ **A suite outside the gates is how 12 failures live for an unknown number of batches** |
| ⭐ **fix what is cheap and in-scope** | if a cluster is *"an HSM action does not fire"* and the fix is Track-E-shaped, ⭐ **fix it and say so** |
| 🔴 **STOP if it is neither** | if the cause is a scenario/DDS/transport issue unrelated to this programme, ⛔ **do NOT fix it** — **file it, quarantine it explicitly (a named skip with the reason), and report.** ⚠ **An unexplained red in the gate set trains everyone to ignore the gate** |

**rail:** ⭐ the suite runs in the gate set and its result is **stated**, with every remaining red
**named and attributed**. ⛔ *"12 fail"* is not a report; *"12 fail, cause X, N fixed, M quarantined
because Y"* is.

---

## 2. ⭐⭐⭐ Give `E0` a **generator-driver emit tier** — ⭐ *the coverage limit that hid `E6`*

> 🔴 **Your finding, and it is the important one from Batch 72:** the HSM emitted `.g.cs` carries action
> **STRINGS**; the **ids** are computed by `HsmFlattener` at runtime and by the **analyzer's**
> `HsmActionRegistrar`. ⭐⭐ **`E0`'s emit tier covers `HsmEmitCore`/`HsmBridgeEmitCore` output only** ⇒
> ⛔⛔ **an id change is INVISIBLE to it.** ⚠ **That is why `E6`'s defect survived — and why `E3` would
> too.**

⭐ **And item 4 of Batch 72 landed on the same wall from the other side:** BTree's emit tier needs a
`CSharpGeneratorDriver` harness because `BTreeJsonGenerator` wants a Roslyn `Compilation` for
`structSizeResolver` and `BTreeDeactivatorScanner.Scan`.

⇒ ⭐⭐⭐ **ONE harness answers both.** ⛔ **Do not build two** *(ruling 9)*.

| | |
|---|---|
| ⭐ **what it is** | a `CSharpGeneratorDriver` run over the corpus, with the generated output baselined as **stored text**, same shape as the existing emit tier |
| ⭐⭐ **what it must cover** | **BTree's 26** *(item 4's unfinished half)* **and the HSM analyzer's registrar/thunks** — ⭐ **the second is the one that would have caught `E6`** |
| ⭐ **prove the reach, do not claim it** | ⭐⭐ **revert `E6`'s FQN key and show this tier reddens.** ⛔ **That is the acceptance test for this item** — if it stays green, the tier does not reach what it was built for |
| ⚠ **determinism first** | across two processes, as you did for the shape tier. ⛔ **A non-deterministic generator baseline is worse than none** |

🔴 **STOP** if the driver harness needs the full solution `Compilation` *(rather than a synthesized one)*
— ⭐ **say so and baseline what a synthesized compilation CAN reach**, naming the gap. ⚠ **Partial with
a named boundary beats nothing.**

---

## 3. ⭐⭐ `E3` — **the storage move.** ⭐ *One mechanism that `E5` then rides*

> ⛔ **Land it AFTER item 2** — ⭐ **item 2 is the gate that watches thunk emission**, and `E3` changes
> exactly that. **Same discipline as 71 → 72, which worked.**

| | measured, by you |
|---|---|
| **the two blockers** | the id is a **static function pointer chosen at build time** ⇒ the thunk cannot receive a runtime occurrence · the DTO resolves at a **fixed offset into the single `BrainBlackboard`** ⇒ nowhere for a second occurrence's bytes |
| ⭐⭐ **the route, already ruled** | per-occurrence bytes from the **partition allocator** under `ComputeStatefulSlotKey(assetId, Scope.Node, occurrence, variableId)` — 📄 **`Q34` §7**, and it is BTree's shipped algorithm. ⛔ **Do not invent a key** |
| ⚠ **spans `ExtDeps`** | `Fhsm.Kernel` + the analyzer's thunk emission + the allocator. 📄 **The design ANTICIPATED this** *(§4.4, user-accepted)*; ⛔ **what it got wrong — and I repeated — was the SIZE** |
| ⭐ **the corpus asset exists** | **`HsmOrthogonalRegions`**, seeded in Batch 71 for exactly this |
| ⭐ **three gap tests to INVERT** | you landed them last batch **with the mechanism named**. ⛔ **Invert, do not delete** |

**rail — pre-written, 📄 plan §4B `E3`:** ⭐⭐⭐ *"two concurrently-active orthogonal regions running the
SAME action write DIFFERENT bytes"*, ⛔ **failing before the change**.
⭐ **Plus, now that the route is the allocator:** a re-entered region reaches **the same bytes**
*(occurrence stability)*, and ⛔ **the 100-byte `BrainBlackboard` tail is untouched** — 📄 design §4.3.

### 🔴 STOP conditions — ⭐ **and an explicitly acceptable outcome**

| | |
|---|---|
| ⭐⭐⭐ **if `E3` is bigger than one commit, LAND ITEMS 1, 2 AND 4 AND ESCALATE IT AGAIN** | ⛔ **that is a good batch, not a failed one.** ⚠ **I would rather have the floor that watches it than a half-done storage move** — and you have now twice been right to stop |
| ⚠ **`ExtDeps` churn** | if the kernel change forces a public API break in `Fhsm`, ⭐ **name every consumer before changing it** — `FDP/Examples` is in the solution and item 1 is already about a suite nobody ran |
| ⛔ **do NOT fold in the params-base half** | 📄 design §4.4 says it folds into this seam; ⚠ **you measured that it did not come free.** ⭐ **Keep it out unless it is genuinely free, and say which** |

---

## 4. ⭐ The HSM `Dictionary.Values` ordering — **one line, its own diff**

⭐ **Your own framing, adopted:** *"deterministic by implementation detail rather than by
construction"*; insert-only `Dictionary<int,V>` enumerates in insertion order **in practice, not by
guarantee**, and a single removal breaks it. ⭐ **Better as a one-line item with its own diff** — this
is that item. ⚠ **It moves the HSM emit baseline on its own; that is expected and is the point.**

---

## 5. ⛔ NOT in this batch

⛔⛔ **blueprint multi-occurrence — DEFERRED BY THE USER** · **`E5`** *(⭐ it rides `E3`'s mechanism — so
it becomes cheap the moment item 3 lands; still needs `-028`(a), `SubtreeAssetId` persisted)* ·
**`E7a`** · **`E7b`'s runtime half** *(`ExpressionTargetField` is emitted NOWHERE)* · **`BP-281`** *(HSM
has no `ParseParams` counterpart)* · the `InspectorWindow` "STATIC PARAMETERS" retirement · the Track C
**visual check**.

---

## 6. Gates

**Baseline — coordinator-verified at `14f8b0ea4`:** build **0 / 69** · Blueprints **3690 / 3680 / 0 / 10** ·
AiShared **1280** · BTree.Editor **615** · Breakpoints **134** · Generators **245** · Hsm.Editor **543** ·
AiEditor.Persistence **136** · Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 61 / done 161**.
🔴 **NEW ROW — `Fdp.Examples.Scenarios.Tests`: 56 / 68, 12 RED (pre-existing).** ⭐ **It is in the gate
set from now on**, and item 1 owns its number.

| | |
|---|---|
| ⭐⭐ **name every Examples suite you ran** | ⛔ **that is how this hole opened** — `UrbanCombat` green while `Scenarios` was red |
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash` |
| ⭐ **expected movement** | item 2 **creates** a generated-emit baseline · item 3 **may move** it *(thunks)* · item 4 **moves the HSM emit baseline** deliberately. ⛔ **Say which files moved in which commit** |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ neither red nor green is evidence — `DEBT-AIB-030`, now **five** distinct tests across two registries |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 7. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
🔴🔴 **item 1** — ⭐⭐ **the cause of each cluster, named** · how many fixed, how many quarantined **and
why** · ⛔ **whether `E6`'s fix reaches these scenarios at all** *(that answer matters beyond this item)*.
⭐⭐⭐ **item 2** — ⭐ **the acceptance test: reverting `E6`'s FQN key REDDENS this tier** *(show it)* ·
determinism across two processes · what a synthesized `Compilation` could not reach.
⭐⭐ **item 3** — **did the two-regions rail FAIL first?** · occurrence stability across ticks · the
`ExtDeps` surface you touched · ⭐ **or the escalation, which is a fine outcome**.
⭐ **item 4** — the baseline diff is only ordering.
**Always:** ⭐ **every id you allocated** · **which `DEBT-AIB` rows this batch touched**.

⭐⭐⭐ **Six batches running, the standing ask has produced a finding every time.** ⭐ **Batches 71 and
72 escalated instead of half-building, and both were right.** ⛔ **Do not let this batch's size push you
off that** — item 3 is the one most likely to deserve another stop, and §3 says in advance that
stopping is an acceptable outcome.
