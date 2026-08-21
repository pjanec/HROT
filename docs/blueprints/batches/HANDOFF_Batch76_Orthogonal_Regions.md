# HANDOFF — Batch 76: **make orthogonal regions actually work** — region 0 · `BP-299` · `-029`

> 📌 **Dispatched at `f2a4fc88b`.** Frozen per rule 1.
> ⭐⭐ **Rule 1b: push `chore: started batch 76 at <sha>` before writing any code.** ⭐ **It worked first
> time last batch — I checked for `4ab9483` before touching anything.**
> ✅ **Batch 75 MERGED at `6a6bdc6cb`** — gates re-run by me, goldens untouched.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⭐⭐⭐ **All three items are LIVE defects.** ⛔ **`E3` is OUT — it is latent and blocked on a decision
> I owe the user** *(§0b)*.

---

## 0. ⭐⭐⭐ Batch 75 — **stopping `E3` a third time was right, and it corrected me again**

| | |
|---|---|
| ⭐⭐⭐ **`E3` has no subject — and you proved it exhaustively** | **zero DTO-bound HSM thunks in any binary**: the four production `[SharedAi*]` live in `Fdp.Toolkits` *(no generator)*, and the one assembly that runs the generator has **`StubIdle`, body empty**. ⇒ ⭐ **the pre-written rail cannot fail before the change** |
| ⭐⭐⭐ **and refusing to land the kernel half alone was the call** | *"a delivery mechanism with no consumer, and with the baked-offset path still in place, TWO mechanisms"* ⇒ ⛔ **exactly what `Q35-C` and the `2026-08-17` ruling forbid.** ⭐ **You applied both rules against a batch I had written** |
| ⚠ **my framing, corrected a THIRD time** | `E3` was *"a signature widening"* (wrong), then *"the dangerous case that silently corrupts"* — ⭐ **true in principle, ZERO instances.** ⇒ **latent, and priced as latent it is not the top of the queue** |
| ⭐⭐⭐ **you found the live one while measuring** | `HsmKernelCore:733`. 📐 **I verified it myself before writing this.** ⇒ **item 1** |
| ⭐⭐ **the empty dialog, and the sixth vacuous rail** | *"a scope that selects NOTHING still has exactly one included path"* — ⚠ **and child-count was 0 in BOTH cases**, so the obvious discriminator would not have worked either. ⭐ **That is the subtlest one yet** |
| ⭐ **`DEBT-AIB-030`: the red ROTATES between runs** | `SC_GZ004_2` this batch, `SC_GZ022_2` last ⇒ ⭐⭐ **strongest evidence yet that it is scheduling, not code** |

---

## 0b. ⛔ Why `E3` is not in this batch

⭐ **Its blocker is a DECISION, not an implementation:** a `[SharedAiAction]` must exist **in a
generator-bearing assembly** before there is anything whose bytes can move. ⚠ **Two candidate shapes**
*(move one into `Hrot.AI.Behaviors`, or run the analyzer over `Fdp.Toolkits`)* — ⭐⭐ **and it is the
SAME blocker as `E7b`'s bytes**, which I had written down at the end of Batch 74 without connecting the
two. ⇒ **I am taking that to the user; `Q35` stays resolved and nothing about its rulings changed.**

⛔⛔ **And item 1 comes first regardless** — ⭐ **per-occurrence storage is downstream of a region model
that currently writes the wrong region's leaf.**

---

## 1. 🔴🔴 `ExecuteTransition` hard-codes region 0 — ⭐ *the live orthogonal-region defect*

📐 **Coordinator-verified `2026-08-17`:**

```csharp
// HsmKernelCore:497  — SelectTransition scans ALL regions (takes activeLeafIds + regionCount)
// HsmKernelCore:513  — and the winner is executed:
ExecuteTransition(definition, instancePtr, instanceSize, selectedTransition.Value,
                  activeLeafIds, regionCount, contextPtr, ref cmdWriter, traceCtx);
// HsmKernelCore:733  — inside it, unconditionally:
activeLeafIds[0] = finalLeafId;
```

⇒ ⛔⛔ **a transition selected in region 1 writes region 0's active leaf.** ⭐ `ExecuteTransition`
receives `regionCount` but **no region index**. ⚠ **Harmless at `regionCount == 1`**, which is exactly
why nothing has caught it.

| | |
|---|---|
| ⭐⭐ **the fix shape** | thread the **region index of the selected transition** through. ⭐ `SelectTransition` already iterates regions to find it — **it knows which one**; the index is dropped on the way out |
| ⭐ **`ExecuteTransition` is `private static`** | ⇒ ⛔ **no ABI concern**, unlike `E3`. This is contained inside `Fhsm.Kernel` |
| ⚠ **check the neighbours while you are there** | the RTC fail-safe loops `0..regionCount` correctly; ⭐ **history restore and the terminal-state check sit in the same block** — **say whether either has the same shape** |

### 🔴 STOP conditions

| | |
|---|---|
| ⭐⭐ **the rail must FAIL first** | ⛔ **a green new test proves nothing.** ⭐ **Two orthogonal regions, a transition fired in region 1, assert region 0's leaf is UNCHANGED and region 1's moved** — that must be red before your fix |
| ⚠ **if `SelectTransition` does not surface which region won** | ⭐ **that is the item** — return it, do not re-derive it at the call site. ⛔ **Re-deriving is a second home for "which region"** |
| 🔴 **if fixing it changes shipped behaviour** *(an asset that depended on the collapse)* | ⛔ **STOP and report** — ⚠ **`HsmShowcase` and the Examples are the population to check** |

---

## 2. ⭐⭐ `BP-299` — **a region with no `InitialChild` is orphaned on load**

> 🔴 **Your finding, and the rail you wrote is what exposed it.** 📐 The JSON region list carries **no
> parent reference**, so ownership is re-derived from `region.InitialChild?.Parent` ⇒ ⛔ **no initial
> child ⇒ no owner ⇒ `composite.RegionNodes.Count < 2` ⇒ rules 8/8b skip the composite, SILENTLY.**

⭐⭐ **This is the same class as `-028`(a) itself: a rule that cannot reach its input.** ⚠ **And it makes
`E4`'s "done" conditional** — the rules fire on a loaded asset **only when every region happens to have
an initial child.**

| | |
|---|---|
| ⭐ **the fix you named** | a **parent reference on `RegionNodeDto`** ⇒ ⛔ **this IS a `persistence-shape` change** — ⭐ **expected to move `hsm-persistence-shape`**, deliberately |
| ⚠ **back-compat** | an asset saved **before** this field must still load. ⭐ **Keep the `InitialChild?.Parent` derivation as the fallback**, and say so — ⛔ **do not make the new field required** |
| ⭐⭐ **the rail is not the round trip** | *(your own words last batch, and they apply again)* — ⭐ **author a rule-8 violation in a composite whose region has NO initial child, save, load, and assert the error appears** |

---

## 3. ⭐ `DEBT-AIB-029` — **rule 8's deep walk**

⚠ **Promoted from theoretical to REAL by `-028`(a):** with `SubtreeAssetId` round-tripping, ⭐ **a
designer can author a NESTED subtree host and SAVE it**, and rule 8's **direct-children-only** walk stays
silent on exactly the corruption it exists to prevent.

⭐ **Invert `ANestedSubtreeHost_EscapesRuleEight_Yet`** — ⛔ **invert, do not delete.**

🔴 **STOP** if the deep walk needs a cycle guard *(a subtree hosting an ancestor)* — ⭐ **that is a real
sub-question and I want it named**, not silently handled with a depth cap.

---

## 4. ⛔ NOT in this batch

⛔⛔ **`E3`** *(§0b — latent, blocked on a decision with the user)* · **`E7b`'s bytes** *(the same
blocker)* · **`E5`** · **`E7a`** · ⛔⛔ **blueprint multi-occurrence** *(user-deferred)* · ⛔ **wiring the
producer picker** *(parked)* · the 12 quarantined scenario tests · the Track C **visual check**.

---

## 5. Gates

**Baseline — coordinator-verified at `6a6bdc6cb`:** build **0 / 69** · Blueprints **3691 / 3681 / 0 / 10** ·
AiShared **1289** · BTree.Editor **615** · Breakpoints **134** · Generators **266** · Hsm.Editor **549** ·
AiEditor.Persistence **136** · Examples.Scenarios **56 / 68 (12 skipped)** · Examples.UrbanCombat **29** ·
Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 63 / done 172**.

| | |
|---|---|
| ⭐⭐ **item 1 is in `ExtDeps/FastHSM`** | ⇒ **run FastHSM's own suite** *(`Fbt`/`Fhsm` tests)* **and name it in the table** — ⚠ **it is not in my baseline and that is my omission** |
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash` |
| ⭐ **expected movement** | ⭐⭐ **item 2 MOVES `hsm-persistence-shape`** *(a new DTO field)* — **deliberate**; items 1 and 3 should move nothing |
| ⚠ **the quarantine count stays 12** | ⛔ a new skip is a finding |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ neither red nor green is evidence — `DEBT-AIB-030`, **seven** tests, **and the identity rotates** |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 6. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.** ⭐ **Including FastHSM's own.**

**Per item:**
⭐⭐⭐ **item 1** — ⭐ **did the two-regions rail FAIL first?** *(show it)* · **how the region index
travels out of `SelectTransition`** · ⭐ **whether history-restore or the terminal check has the same
shape** · any shipped asset whose behaviour changes.
⭐⭐ **item 2** — the **pre-field asset still loads** · ⭐ **the rule fires on a composite whose region has
no initial child** · what moved in `hsm-persistence-shape`.
⭐ **item 3** — the inverted gap test · ⭐ **the cycle question, named**.
**Always:** ⭐ **the started-marker sha** · **every id you allocated** · **which `DEBT-AIB` rows you
touched** · **the quarantine count**.

⭐⭐⭐ **Nine batches, a finding every time — and three of the last four were about MY premises.**
⭐ **Batch 75's was the sharpest: an item I called dangerous has no instances, and the dangerous thing
was two files away.** ⛔ **Keep doing exactly that.**
