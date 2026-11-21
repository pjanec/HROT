# The Return node's `Status`: combo vs. data pin — design note

> ## ✅ SHIPPED — Batch 29 (BP-131). This document is now history; the code is the reference.
> Landed exactly as §7 settled: one `Success : bool` data-in pin, **AiPrimitive only**, ABI unchanged.
>
> | Hazard | How it landed |
> |---|---|
> | **H1** | `IrTerm_ReturnStatus` gained an optional `IrValue Condition`; `TerminatorEmitter` renders `return cond ? NodeStatus.Success : NodeStatus.Failure;` when set, the old constant otherwise |
> | **H2** | Handled **twice**, because the two mechanisms fail independently: the projection gate (AiPrimitive only) is primary containment, and `BuildReturnTerminator` also drops the pin **by name** from `valuePins` — a hand-authored asset can carry pins no projection wrote |
> | **H3** | `EnrichReturnPins` gained an `asset` parameter (the editor half already had it in scope) |
> | **unwired** | Falls back to `rn.Status` — §8's preferred option. `default(bool)` is `false` = Failure and would have flipped every shipped AiPrimitive Return |
>
> ⭐ **Proved at runtime, not on the IR:** `BP131_ReturnSuccessPinTests` compiles through the real
> Roslyn generator and ticks one assembly twice across a component change — **Failure then Success**.
> An IR assertion cannot distinguish *"the status is computed"* from *"the constant happened to match"*.
>
> 📌 **Still separate and NOT done:** D3's zero-output-`Library`-returns-`void` change (§9's last
> paragraph). 📌 **BP-200's unwired-pin question is untouched.**


> **BP-131.** Written Batch 26 by the implementation session, for the architect round.
> ⚠ **Design only — options are laid out, none is picked.** That was the instruction, and it is also the
> right call: one of the options changes a contract that reaches outside the blueprint subsystem.

---

## 0. The ask

> User: *"a combo with fixed values for Success, Error, In progress — meaningless … the status must be an
> input data pin of the return node, not a fixed value to select in a combo."*

⭐ **The point is sharper than the visibility complaint it looks like.** A status chosen at author time
from a combo is a **compile-time constant**, so it cannot express a runtime outcome. A node whose whole
job is to report how execution went cannot report anything that depends on execution.

---

## 1. Where it still bites

| Dispatch | Status combo | Notes |
|---|---|---|
| Instance | hidden | ✅ BP-105; user confirmed |
| Library **with** outputs | hidden | ✅ BP-105 |
| **Library, zero outputs** | **shown** | 🔴 the method returns `NodeStatus`; the combo is the only writer |
| **AiPrimitive** | **shown** | 🔴 ⚠ and this is the hard case — see §3 |

---

## 2. This is the same defect as BP-107, one layer up

[BP-107](Blueprint_Issues_Detail.md#bp-107) is already open: *`Return.Status` is a compile-time constant
⇒ `Running` is inexpressible.* ⭐ **BP-131 is the editor-facing half of that same statement.** They should
be settled together, or the combo will be removed while the underlying constant remains — or vice versa.

⚠ **`Running` is the case that proves the point.** A latent/multi-tick primitive must return `Running`
*while it is still running* — a fact known only at runtime. No author-time combo value can express it,
so today the shape is not merely awkward, it is **incapable**.

---

## 3. ⚠ Why AiPrimitive is not a free choice

An AiPrimitive's `NodeStatus` return **is the BTree/HSM hosting contract** — the tree reads it to decide
whether to continue, abort, or advance. Changing how it is produced is not an editor change:

- The hosting layer depends on the emitted method's return type and semantics.
- `Fbt.NodeStatus : byte` is marshalled through the Library ABI in places.
- ⚠ **Test-locked:** `BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn` pins the
  implicit-Success behaviour, and the AiPrimitive hosting tests pin the contract. Any option below turns
  those red **by design** — that is a signal to think, not a mechanical fix-up.

---

## 4. Options

| | Option | What it means | ⚖️ Trade |
|---|---|---|---|
| **A** | **Status becomes a data-in pin** (`NodeStatus`-typed), combo removed | The user's literal request. Wire a `Compare`/`Literal`/primitive output into it | ✅ Expresses runtime outcome, `Running` included · ⚠ needs a `NodeStatus` literal + enum pin editor · ⚠ **an unwired pin is `BP4001` + `default(T)`** ⇒ every existing Return node becomes a warning and silently defaults to `Success` (value 0 — verify) |
| **B** | **Pin *or* combo** — combo used only when the pin is unwired | Backwards compatible; the combo becomes the literal's default | ✅ No migration, no new warnings · ⚠ **two writers for one value**, which is exactly the shape that caused BP-125. Needs a hard rule about which wins, visible on the node |
| **C** | **Keep the combo; add explicit latent/`Running` support elsewhere** | Treat `Running` as a scheduling concern, not a return value | ✅ Leaves the ABI alone · ⚠ does not answer the user; the combo stays meaningless for the cases they hit |
| **D** | **Hide the combo everywhere** (extend BP-105 to the two remaining cases) and always emit implicit `Success` | Smallest change | ✅ Removes the misleading control · 🔴 **removes the only writer** — a Library-zero-output or AiPrimitive graph could then never report failure. Almost certainly wrong, listed for completeness |

⭐ **B is the cheapest thing that answers the complaint**; **A is what the user actually described**;
**A+BP-107 together** is the only combination that makes `Running` expressible.

---

## 5. ⚠ Questions the architect should settle first

| # | |
|---|---|
| **Q1** | **Is `Running` in scope?** If yes, A (with BP-107) is effectively forced and B is a stepping stone. If no, B is sufficient and much cheaper |
| **Q2** | **For AiPrimitive, may the emitted `NodeStatus` come from a runtime value at all**, or is the hosting contract built on it being statically known? ⭐ **This is the load-bearing question** — everything else follows from it |
| **Q3** | **Migration for option A:** does an unwired Status pin warn (`BP4001`) or default silently? ⚠ Warning is more honest but instantly marks every existing Return node in the repo |
| **Q4** | **Does a `NodeStatus` literal node get added to the palette**, or is Status wired only from primitive outputs? |

---

## 6. Recommendation

⭐ **Settle Q2 first, and settle BP-131 together with [BP-107](Blueprint_Issues_Detail.md#bp-107)** — they
are the editor and compiler halves of one statement, and fixing either alone leaves the other incoherent.

Absent an architect ruling, **B** is the lowest-risk step that stops the control being meaningless, and it
does not foreclose **A**. ⚠ But B reintroduces a two-writer shape, so if **A** is where this is going
anyway, going straight there is cleaner than passing through B.


---

# ✅ SETTLED — and what implementing it actually costs

> Appended Batch 27 by the implementation session, after the decisions round closed §5's questions and
> after reading the code the change lands in. ⚠ **Not started this batch** — see §9 below for why.

## 7. The answers (from `DECISIONS_Authoring_UX.md` §D3)

| Q | Answer |
|---|---|
| **Q1** `Running` in scope? | **No, and it never was.** Unreal's model: `Running` is *the absence of finishing*, not a value. ⭐ **Our compiler already does exactly this** — the latent lowering emits it at suspension points. Only the author-facing surface disagreed |
| **Q2** ⭐ may an AiPrimitive's status come from a runtime value? | **Yes, and the ABI does not change.** The method still returns `NodeStatus`; a `bool` maps to Success/Failure *at the return statement*. **This is what made the item look cross-subsystem — it isn't** |
| **Q3** migration | Moot. The pin is `bool`, and an unwired one is `false` — see the hazard in §8 |
| **Q4** a `NodeStatus` literal node? | **No.** No status surface anywhere except the AiPrimitive Return's `Success : bool` pin |

⇒ **Option A, narrowed**: one `Success : bool` data-in pin, AiPrimitive only. Not the `NodeStatus`-typed
pin §4 described — a bool is simpler and matches Unreal's `Finish Execute`.
⇒ **Closes [BP-107](Blueprint_Issues_Detail.md#bp-107)**: `Running` stops being something anyone tries to express.

## 8. ⚠ Three hazards found by reading the code, none of them obvious from the decision

| # | |
|---|---|
| **H1** | ⭐ **`IrTerm_ReturnStatus` carries a `NodeStatus` CONSTANT.** A runtime bool needs it to carry an optional `IrValue` condition, and `TerminatorEmitter` to render `return cond ? NodeStatus.Success : NodeStatus.Failure;`. **This is the actual work** — the pin is the easy half |
| **H2** | ⚠ **`Stage5.BuildReturnTerminator` collects `valuePins` as "every non-exec pin on the Return node"** and branches on `valuePins.Count == 0`. A `Success` pin is a non-exec pin, so adding one **silently changes the zero-output-Library branch and the multi-output tuple packing**. The new pin must be excluded by name at that site, or two unrelated shapes break |
| **H3** | ⚠ **`Stage0.EnrichReturnPins(pins, graph, staticShapes)` does not receive the ASSET**, so it cannot see `Dispatch`. Both projections need it (Stage0 *and* the editor's `NodePinSchema`, which already has the asset) — and they must agree, or the pin exists on one side only |

⚠ **An unwired `Success` pin is `BP4001` + `default(bool)` = `false` = Failure.** Every existing
AiPrimitive Return would start reporting failure. ⇒ **The pin needs an inline default of `true`**, or
the terminator must fall back to `rn.Status` when the pin is unwired. ⭐ **Prefer the fallback**: it is
back-compatible with every shipped asset and needs no migration.

## 9. Why this was not started in Batch 27

Not blocked and not disagreed with — **out of room**. It is a five-site change (IR + emitter + two pin
projections + the drawer) landing on the AiPrimitive emit path, and Batch 27 had already delivered
eleven items. ⚠ **Rushing an ABI-adjacent change at the end of a large batch is how a green suite ships
a wrong-values defect**, which is the specific failure this whole programme has been correcting.

**The zero-output-Library `void` change (D3's last bullet) is separate and should stay separate.** It
changes an emitted signature that `BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn`
pins deliberately, and unlike the `Success` pin it has **no user-visible benefit** — it is a tidiness
change to a contract that currently works.
