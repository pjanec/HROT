# 👀 Visual check — **step by step**

> **Rewritten 2026-08-09 as literal steps**, because the previous guide listed *features to verify*
> rather than *what to click*. Supersedes [VISUAL_CHECK_Batches20-23.md](VISUAL_CHECK_Batches20-23.md).
>
> **Every step is: do this → expect exactly this.** If what you see differs, stop and report the step
> number — you do not need to diagnose it.
>
> ⚠ **Where I am unsure of an exact menu label I say so**, rather than inventing one. Labels below that
> appear in `code font` are read from the source and should match exactly.

---

## 🛑 Before you start

| | |
|---|---|
| **1** | ⚠ **Do not touch the `Type` dropdown on a parameter that already exists.** It shows the wrong type ([BP-114](Blueprint_Issues_Detail.md#bp-114) — not yet fixed) and *changing it to the "right" value silently retypes the parameter for real.* Parameters **you** add in these steps are fine. |
| **2** | ⚠ **Three of your findings are already confirmed as real bugs** — BP-116, BP-117, BP-118 below. **Steps that would hit them are marked 🔴 KNOWN and tell you to skip.** Do not spend time re-reporting them. |
| **3** | Delete any scratch `.bp.json` you create under `Assets/` when you finish. |

---

## 🔴 KNOWN — already found, already registered, **do not re-test**

| | What you hit | Status |
|---|---|---|
| **BP-116** | `BP1300: … not in CallablePeers list` on any peer call you author in the editor | ⭐ **Root cause found: the editor never writes `CallablePeers` at all.** Picking a peer in `Details` does not add it. **Every** editor-authored peer call fails this way. Not your mistake — the feature is unusable from the UI |
| **BP-117** | `CS0126: An object of a type convertible to '(bool, bool)' is required` | ⭐ **Root cause found:** `Stage5.SealFallThrough` emits a bare `return;` when an outputs-declaring **Library** graph's exec chain ends without a `Return` node |
| **BP-118** | `SmokePatrol` / `SmokeMathLib` not in the picker | ⭐ **My guide was wrong, and there is a real gap.** They ship only under `Recipes/`, so they are **templates you instantiate**, not files you open. `Assets/Blueprints/` has no copy |

⇒ **Skip sections D and E below until those land.**

---

## A · Function Library end to end — ~6 min

| Step | Do | Expect |
|---|---|---|
| **A1** | Open the Blueprint editor. Create a new blueprint. | A template list appears |
| **A2** | In the template list, find the two blank templates | `Empty` and `Function Library`, each with a description, listed alongside the disk recipes |
| **A3** | Select **`Function Library`**, accept the default name, confirm | Defaults to `NewBlueprint`. **It opens without an error dialog** |
| **A4** | Look at the `My Blueprint` panel | `Functions` is **not** empty — a starter function graph exists |
| **A5** | **Without editing anything**, run a full solution build | ✅ **0 errors.** *(This is the `CS9191` case that failed for you — fixed)* |
| **A6** | Open the asset's `.bp.json` in a text editor | `"Dispatch": "Library"`, and `Graphs` is not `[]` |

## B · Function outputs — ~8 min

| Step | Do | Expect |
|---|---|---|
| **B1** | Open the starter function graph from `My Blueprint` → `Functions` | The graph canvas opens with an entry node and a `Return` node |
| **B2** | Click the `Return` node. Look at the `Details` panel | An **Outputs** section, headed *"Outputs — one data-in pin on this Return node, and one data-out pin on every call site."* |
| **B3** | Before adding any output, read the empty-state text | *"This function declares no outputs. Add one to return a value."* |
| **B4** | Click **`+`** in that Outputs section | A row appears, **and the `Return` node grows a data-in pin immediately** |
| **B5** | Rename that output to something **shorter** than its default name | ⚠ Exactly what you typed. **No `?`, no leftover characters from the old name.** *(This is the `P1?am0` bug)* |
| **B6** | Wire something into the Return node's new pin, then build | ✅ Clean |
| **B7** | Press **Ctrl+Z** once | The wire OR the rename undoes — **one** step, not two, not zero |
| **B8** | Add a **second** output, wire it, build | ✅ Clean — the tuple path |
| **B9** | Add a **third**, then **Ctrl+Z ×2**, then **Ctrl+Y ×2** | Ends exactly where it started. ⭐ **Stress this one** — it had an aliasing bug |
| **B10** | Remove **all** outputs, build | ✅ Still clean *(a zero-output library function returns `NodeStatus` — deliberate)* |

⚠ **B8/B9 may hit [BP-117](#) if your exec chain ends without reaching the `Return` node.** If you see
`CS0126`, that is the known bug — note it and move on.

## C · Type picker — ~5 min

| Step | Do | Expect |
|---|---|---|
| **C1** | Open the `Graph Signature` window for your function. Click `+` under `Inputs` | A new parameter row, with a `Type` dropdown |
| **C2** | Open that dropdown **on the parameter you just added** | ⭐ `FixedString32` and `FixedString64` are in the list *(you confirmed this — ✅)* |
| **C3** | Set it to **`ushort`**, build | ✅ **No `BP1500`** *(this was your failure)* |
| **C4** | Add another parameter, set it to **`Vector3`**, build | ✅ No `BP1500` |
| **C5** | Add a `ushort` **output**; wire it into an `int` input somewhere | ⭐ **The wire connects.** *(Your condition for keeping unsigned types)* |
| **C6** | Try wiring an `int` output into a `uint` input | ❌ **Refused** — correct; C# needs a cast, and a silent lossy conversion would be an invisible wrong-value bug |
| **C7** | ⚠ Look at a parameter in a **pre-existing** asset | Shows the **wrong type name** — that is BP-114. **Do not click the dropdown.** |

## D · 🔴 Cross-asset peer call — **SKIP, blocked by BP-116**

Nothing you can do here will work: the editor never records the peer, so every attempt ends in
`BP1300`. Comes back once BP-116 lands.

## E · 🔴 Sample data — **SKIP, blocked by BP-118**

`SmokePatrol` / `SmokeGuard` / `SmokeMathLib` are recipe **templates**, not openable assets.
If you want to look at a library today, open **`Assets/Blueprints/LibraryFunctionsDemo.bp.json`** —
that one *is* an openable asset, and it covers 1-output, 2-output and zero-output library functions.

## F · Return-node `Status` visibility — ~3 min

`Status` and `Outputs` now appear only where the compiler reads them:

| Asset dispatch | Outputs | Status |
|---|---|---|
| Instance | ✅ shown | ❌ hidden |
| Library, 0 outputs | ✅ shown | ✅ shown |
| Library, ≥1 output | ✅ shown | ❌ hidden |
| AiPrimitive | ❌ hidden | ✅ shown |

| Step | Do | Expect |
|---|---|---|
| **F1** | `Return` node in an **Instance** function graph | **No `Status` combo.** *(This answers your "what is the purpose of it?" — on an Instance graph it had none, so it is gone)* |
| **F2** | `Return` node in a **Library** function with **zero** outputs | `Status` **is** shown and editable, with a line saying why |
| **F3** | Add one output to that library function | `Status` **disappears**; Outputs stays |
| **F4** | Change `Status` where it is shown, then **Ctrl+Z** | One undo step, previous value restored ⇒ **tell me, this closes BP-14** |

## G · Known gap — confirm only, do not report

| Step | Do | Expect |
|---|---|---|
| **G1** | Add an output from the **`Graph Signature`** window (not the Return node), then **Ctrl+Z** | ❌ **Will not undo.** Registered as [BP-102](Blueprint_Issues_Detail.md#bp-102). Just confirm the asymmetry is still there |

---

## How to report

Give me the **step number** (`B5`, `C3`), what you saw, and the **exact diagnostic code** if there was
one. `BP5001` vs `BP1101` vs `BP1500` vs a `CS` number point at completely different halves of the
compiler. A screenshot for anything about wording or layout.

⚠ **You do not need to diagnose anything.** "B5 showed `P1?am0`" is a complete and useful report.
