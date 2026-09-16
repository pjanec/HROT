# Authoring-UX decisions — settled 2026-08-09

> ⭐ **Every open architect question is now closed.** Two dissolved once checked against the code, one
> against Unreal's actual model, and the Print-node trio was nodded through. **Nothing in the backlog is
> architect-blocked.**

---

## D1 · Entry-node editability *(was BP-128 Q1 — not an architect question at all)*

| `EventEntryNode.EventTypeId` | Meaning | Inputs |
|---|---|---|
| **set** | engine/system event | **read-only** — the payload is not the designer's to change |
| **empty** | function or tick entry | **editable** — these *are* the graph's `Inputs` |

⚠ The discriminator already exists in the model. This was escalated when it should have been read from
the code — the same failure as the `TreatWarningsAsErrors` claim.

---

## D2 · Where asset state is edited *(was BP-128 Q2)*

**The premise needed correcting in both directions.**

✅ **Asset-level state is real** and distinct from graph signatures:

| | Scope | |
|---|---|---|
| `BlueprintAsset.Variables` | **per-instance** persistent state | `Count` lives here — that is why it survives ticks |
| `BlueprintAsset.Parameters` | per-instance spawn config | `IsExposedOnSpawn` |
| `BlueprintAsset.WorkingState` | per-instance scratch | |
| `Graph.Inputs` / `Graph.Outputs` | **per-graph** function signature | |

⚠ *"Class variable"* was the wrong word. Unreal means **declared on the class, stored per instance** —
nothing static. Restate as **instance state declared on the asset**.

⭐ **The user's architectural correction stands and replaces the "ownership" framing:**

> Panels own nothing. The model owns the data; panels are views.
> **Any number of panels may present the same state — every one must write through the same edit
> service.** Two writers is fine; two *different paths* is BP-125.

### Placement — settled, and matching Unreal

| Surface | Shows |
|---|---|
| My Blueprint | **lists** variables/functions/graphs |
| Select a variable in My Blueprint → Details | that variable's properties **+ initial value** |
| Entry node → Details | the graph's **Inputs** |
| Return node → Details | the graph's **Outputs** |
| A graph's Details | ⛔ **never** asset-level variables |
| Rename a graph | ⭐ **My Blueprint context menu** — where Unreal puts it |

⚠ **Correction to an earlier claim:** *"click empty canvas → Details shows graph/asset properties"* was
asserted as Unreal parity. **It is not.** Unreal clears Details on empty-canvas click; function
properties live on the **entry node**. ⇒ **the empty-canvas panel may not be needed at all.**

📌 **Deferred (recorded, not scheduled):** Unreal's **`Class Defaults`** toolbar mode — Details shows
*every* variable's initial value at once. Independent and additive; blocks nothing.

---

## D3 · `Return.Status` *(was BP-131 — resolved, and it closes BP-107)*

**User's rule:** an AiPrimitive tick graph's Return carries a status **data-in pin** that other nodes
feed; **nowhere else has a status surface at all.**

⭐ **Unreal's model is a simplification of that, and we already implement its semantics.** A Blueprint
Behavior Tree task has **no status pin anywhere**:

| | |
|---|---|
| Success / Failure | a **bool** on an explicit **`Finish Execute`** node |
| **Running** | ⭐ **not a value — it is the absence of finishing.** Don't call Finish and the tree keeps ticking you |

### Settled shape

- **AiPrimitive Return** → one **`Success : bool`** data-in pin. Wire a condition into it.
- **`Running`** → **never author-selectable.** It is emitted only by the latent lowering at suspension
  points — ⭐ **which is exactly what our compiler already does.** The machinery had Unreal's semantics
  all along; only the author-facing surface disagreed.
- **Everywhere else** → no status surface, per the user's rule.
- **Zero-output Library** → should return `void`, not `NodeStatus`. ⚠ The one place a test genuinely
  pins today's behaviour (`BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn`).

⭐ **The ABI does not change.** The emitted method still returns `NodeStatus`; the bool merely maps to
Success/Failure at the return statement. **That was the thing that made this look cross-subsystem — it
isn't.** The BTree/HSM hosting contract is untouched.

⇒ **Closes BP-107** (`Running` inexpressible): `Running` stops being something anyone tries to express.

---

## D4 · Unwired function return → **Error**

Confirmed by the user. Validates the seeded-Return trade-off: declaring an output now breaks the build
until wiring follows, and that is the intended behaviour.

---

## D5 · Print / Format String — the three open Q26 sub-questions: **approved, no architect round**

| | |
|---|---|
| Named-placeholder parse (`{Name}` → pins) | ✅ shipped, confirmed in the field |
| Silent truncation past the `FixedString` width | ✅ accepted; documented in the node tooltip |
| `Format String` is **pure** (no exec pins) | ✅ matches Unreal's Format Text |

---

## D6 · The 34 surfaced warnings — **triage deferred, deliberately**

32 × `BP3010` (orphan node eliminated) + 2 × `BP3011` (implicit `Byte`→`Int32`, benign).

⚠ **Do not bulk-fix yet.** Two reasons:
1. ⭐ **Some orphan GUIDs appear in no asset file**, so those nodes were **synthesized by the compiler**
   and then eliminated — a different defect from a designer leaving a node unwired.
2. The warnings name a GUID and a graph but **not the asset**, so attribution costs a repo grep.
   ⇒ **Triage after BP-206** makes them self-identifying.

---

## Process rule changed (third ID collision)

⭐ **The coordinator allocates NO ids.** `BP-200+` failed because *both* sessions reach into it.
Findings are described; **the implementation session numbers them** when it creates the rows.
Recorded in `.claude/CLAUDE.md`.
