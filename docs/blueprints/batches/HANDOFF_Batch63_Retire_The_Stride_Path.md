# HANDOFF — Batch 63: ⭐⭐⭐ **`BP-251` IS `W13`** — retire the standalone stride path, then the rest

> 📌 **Dispatched at `a319114cb`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ✅ **Batch 62 item 0 MERGED at `665bb29b6`** *(docs-only — ⭐ **no gate run: the diff is one tracker
> line**, and saying so is cheaper than implying I re-ran eight suites for a comment)*.
> ⭐ **Rule 7:** branch from this branch. ⭐ **Rule 4:** pull it again before your final commit.
> ⛔ **Rule 3: the coordinator allocates no ids.** ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⛔⛔ FIRST — a coordinator claim you corrected, and how I got it wrong

⭐⭐ **I told you `paramIndex` is *"the BTree node payload index ⇒ the multiplier is HOW MANY TIMES THE
TREE BINDS THAT PRIMITIVE."*** ⛔ **Wrong, and you measured the right answer:** `TreeCompiler:155` —
**`payloadIndex = GetOrAddMethodName(methodNames, node.MethodName)`** ⇒ ⭐ **the ordinal among DISTINCT
Action and Condition method names in the whole tree.** ⇒ **the multiplier grows with TREE SIZE, not
with how often one primitive is bound.**

⚠⚠ **How I got there matters more than the error:** I read `NodeLogicDelegate.cs:11`'s **doc comment**
(*"Index for looking up parameters"*) and treated it as the mechanism. ⛔ **I read the parameter's
DOCUMENTATION instead of its ASSIGNMENT** — the producer. ⭐ **That is this programme's own rule broken
by the person who keeps writing it down, twice in this session.** 📌 **Recorded, not smoothed over.**

⇒ ⭐ **And your number is worse than mine:** `PlatoonHillAttack2` puts
`HillAssault2I_DispatchWaveWithTargets` at method-name index **5** with a 40-byte `Params` ⇒ bytes
**200..240** of a **100-byte buffer inside a 128-byte component** — ⛔ **past the component entirely.**

---

## 1. ⭐⭐⭐ The synthesis: **`BP-251` and `W13` are the same work item**

⭐ **You reframed it as ruling 9 — *"two implementations of one concept, where the bridge does it
correctly and the raw thunk does it a second way with no bound and a key that ends `@0`, asserting an
offset it does not use."*** ⭐⭐⭐ **The cross-host handoff already has that item, and it names your file:**

> **`W13` — Retire the standalone stride path — route `BTreeTick` through the offset form.**
> *Acceptance: **one projection formula repo-wide**. ✅ `U-12` is CLOSED, so this is UNBLOCKED.*

⇒ ⛔ **`BP-251` is not a missing bound to add. It is `W13`, discovered from the other end** — the design
session predicted the duplication; you measured **why it is dangerous** and that **nothing binds it.**
⭐ **Two routes, one answer — for the second time in this programme.**

### ⚖️ What to do — **lean: DELETE, on `W3`'s precedent**

| | |
|---|---|
| ✅ **the evidence for deletion** | ⭐ **nothing binds it** — coordinator-confirmed: no `BTreeTick`/`BTreeEvaluate` anywhere under `Assets/`, while **20+ are registered**. ⭐ **Exactly `BP-248`/`W3`: unreachable AND dangerous** |
| ⭐⭐ **state the rail as an ABSENCE** | ⛔ **do not add a bound to a path nobody uses** — ⭐ **your own `W3` wording: naming `100`/`200` *"would pass again the moment someone reintroduced the mechanism at 300."*** ⇒ **the rail is: the blueprint registrar emits NO un-bounded parameter projection at all** |
| 📐 **but answer this first** | ⭐ **WHY does `CSharpEmitter` emit `BTreeTick@0` at all?** ⚠ **A vestigial registration and a deliberate standalone entry point look identical from the call graph.** 📐 **Check the editor preview / hot-reload / test paths before deleting — `grep` over `Assets/` cannot see a programmatic binder** |
| ⭐ **the `@0` is itself a lie** | a content-addressed key **asserting an offset it does not use.** ⭐⭐ **This is what `W1`'s third rail was about** — *"refuse any standalone key but `@0`"* — ⛔ **the one rail I could not verify and asked you to measure.** 📐 **Connect them: does retiring the path make that rail moot, or is the rail what ENFORCES the retirement?** |
| 🛑 **STOP if** | ⭐ **anything at all binds the standalone form** ⇒ **then it is route-through-the-offset-form, not delete, and that is a bigger change — report before doing it** |

**Gate:** ⭐ **one projection formula repo-wide** — assert it, do not assert the absence of a literal.
⭐ **Plus: the registrations that disappear, counted.**

---

## 2. `S2` — struct size resolution ⭐ **back on its own footing**

⚠ **I put `S2` ahead of `BP-251` because the analyzer needed its size oracle.** ⭐ **If `BP-251` is a
deletion, that dependency dissolves** ⇒ **`S2` is now ordered by nothing but its own value. Do it
second because it is the largest, not because anything waits on it.**

🔴 An unregistered struct resolves at a **guessed 4 bytes**; `StaticTypeRegistry:75-81` hardcodes three
with hand-computed sizes. ⭐⭐ **`StructSizeResolver` is fully general and the compiler never calls it.**

| | |
|---|---|
| ✅ **no project cycle** | `Hrot.AiEditor.Generators` references Schema + Persistence, ⛔ **not the compiler** |
| ⚖️ **lean** | the existing **`IClrSignatureResolver`** seam, or move `StructSizeResolver` where both can see it — ⛔ **not a new Compiler→Generators reference** (Roslyn into a deliberately reflection-less compiler) |
| **gate** | an unregistered user struct gets its **real** size · ⭐ **reuse Batch 60's `EmittedStateLayoutTests`** |
| 🛑 **STOP if** | a new project reference is the only workable placement *(architecture change → the user)* · **or** a **shipped** asset uses an unregistered struct *(live wrong-layout defect)* |

---

## 3. `W6` → `W7` *(carried, third time — take them if the run has room)*

| | |
|---|---|
| **`W6`** | guard read-only projection — `GetComponent` not `GetComponentRW`; `in`/`ref readonly` at the thunk boundary. 📐 **Re-measure the `[SharedAiCondition]` usage count and state it** |
| **`W7`** | concurrent-region rule — error on concurrent **writers**, permit concurrent **readers**. ⚠ needs `W6` |
| 🛑 **STOP if** | the count is not ~0 · or `W6` did not land cleanly |

---

## 4. ⚠ The `Fdp.Toolkits.Tests` race *(carried — still not yours)*

`StatelessGizmoRegistryTests.SC_GZ022_2` — **three runs of an identical binary: 1 · 1 · 2 failures**
⇒ **a RACE, not order-dependence.** Passes in isolation; your diffs touch nothing in `Fdp.Toolkits/`.
⭐ **File it; fix it if the cause is a shared static registry** *(xUnit parallelises by collection)*.
🛑 **STOP if** the cause is **production** static state — that is a much larger finding.
⚠ **Fifth racy/order-dependent result in this programme — it undermines every gate.**

---

## 5. Gates

**Baseline — coordinator-run at `f5c1dd7c5`** *(unchanged; Batch 62 added no code)*: build
**0 errors / 69 warnings** · Blueprints **3615 / 3605 / 0 / 10** · AiShared **1216** · BTree **612** ·
Breakpoints **130** · Generators **196** · NodeEdit **208 / 131** · ⚠ **Toolkits 1942 ± the §4 race.**

| | |
|---|---|
| 🔴🔴 **`StructureHash` unchanged for all 43** | ⚠ **`S2` is the item that could move it — if it does, STOP** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| ⭐ **golden Tier 1 unchanged** | ⚠ **Tier 2 WILL move for item 1** — every asset loses a registration line ⇒ **declare the count** |
| ⭐ **per-item revert-goes-red** | |
| `tracker-counts.py --check` | clean |

---

## 6. Reporting

⭐⭐ **Why `BTreeTick@0` was emitted, and what you found before deleting it** · ⭐ **the registration
count that disappeared** · ⭐ **how the `@0` key relates to `W1`'s third rail** · ⭐ **`S2`'s placement
decision** · ⭐ **the `[SharedAiCondition]` count** · ⭐ **the race's cause, or that you could not
localise it** · 🔴 **`StructureHash` unchanged** · per-suite numbers **full and filtered** ·
`tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The question to carry:** ⛔ **Twice now — `W3` and `BP-251` — a mechanism has been found that is
simultaneously UNREACHABLE and DANGEROUS, registered by an emitter, bound by nothing.** 📐 **What else
does this repo REGISTER that nothing BINDS?** ⭐ **That grep is cheap and it has paid twice.**
