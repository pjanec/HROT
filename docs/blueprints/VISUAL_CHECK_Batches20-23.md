# 👀 Visual check — batches 20 · 21 · 22 · 23

> **Self-contained. Rewritten 2026-08-09 against the shipped post-Batch-23 code.**
> Supersedes the *"🎯 Batches 20+21 — DO THIS FIRST"* section of
> [Blueprint_Gaps_Programme_RESUME.md](Blueprint_Gaps_Programme_RESUME.md).
>
> ⚠ **This guide has gone stale under a batch twice** (A4's expectation was inverted by BP-103; D's
> Status combo was hidden by BP-105). Both would have produced **false regression reports**. Every row
> below is re-derived from the code as it stands after Batch 23.

**Budget ~35 min.** Sections **A** and **B** are ✅ already done — start at **G**, then C–F.

---

## 🛑 Read these three warnings first

| ⚠ | |
|---|---|
| **1. Do NOT touch the Type combo on an existing parameter.** | **[BP-114](Blueprint_Issues_Detail.md#bp-114), found last night, not yet fixed.** The combo matches by exact string, but most shipped assets store the canonical FQN (`System.Int32`) while the list offers the alias (`int`). Neither matches ⇒ the combo falls back and **displays `bool` for an `int` parameter**. It is **mis-display only** *until you change it* — and "correcting" the visibly-wrong entry **silently retypes the parameter for real**. ⇒ **Expect wrong type names on existing parameters. Do not fix them.** New parameters you add yourself are fine. |
| **2. Delete scratch `.bp.json` before you finish** | Anything under `Assets/Blueprints` is a generator `AdditionalFiles` entry, so a broken one **breaks the solution build for everyone who pulls**. `Recipes/Blueprints` is `Content` and cannot. ✅ The `ushort` half of this trap is fixed (BP-87) — but delete your scratch assets anyway. |
| **3. `FuncLib1.bp.json`** | If your old one is still in `Assets/`, delete it. It should now build clean (BP-87 + BP-112 both landed), but it is scratch. |

---

## ✅ Already confirmed — do not redo

| | |
|---|---|
| **A1–A3** | `Function Library` template exists; created + opened without throwing ⇒ BP-103 fixed, BP-92 re-tickable |
| **A4** 🔴→ | full build failed with `CS9191` ⇒ **BP-112 — fixed last night, re-check in G1** |
| **B1** 🔴→ | `ushort` gave `BP1500` ⇒ **BP-87 — fixed last night, re-check in G3** |

---

## G. ⭐ The Batch 22 + 23 fixes — **start here** (~12 min)

Everything in this section is code that shipped in the last two batches and **has never been seen by a
human**.

### G1 · BP-112 — a Function Library no longer breaks the build

| # | Do | Expect |
|---|---|---|
| G1a | **New Blueprint → `Function Library`**, accept defaults, **do not edit it** | Opens clean |
| G1b | ⭐ **Full `dotnet build` of the solution** | ✅ **0 errors.** ⚠ This is the exact case that failed for you with `CS9191` — the generated adapter emitted `ref` where `MemoryMarshal.Write` wants `in` |
| G1c | Add a function with **2 outputs**, wire both, build | ✅ Clean — the multi-output adapter walk |

💡 There is now a shipped demo asset covering all three adapter shapes:
`Assets/Blueprints/LibraryFunctionsDemo.bp.json` (1 output · 2 outputs · zero-output `NodeStatus`).
**It is the first Library asset the real generator has ever compiled.** Worth opening to see a correct
library.

### G2 · BP-113 — a peer call finally shows **all** its outputs

This is the one you reported: *"CallPeerBlueprint keeps showing just a single output data pin."*

| # | Do | Expect |
|---|---|---|
| G2a | In an **Instance** blueprint, add **CallPeerBlueprint** → pick your Library → pick a **2-output** function | ⭐ **TWO data-out pins**, named after the declared outputs (`Lo`, `Hi`, …) — not one pin called `Return` |
| G2b | Wire both to something and **compile** | ✅ Clean, and **both values arrive** — the compiler used to collapse them to one a layer below the editor |
| G2c | Point it at a **zero-output** function | Single `Return` pin, `System.Object` — the historic shape, deliberately kept |
| G2d | Open an asset saved **before** last night that has a 1-output peer call | Still binds. The pin was renamed, so it re-binds positionally — worth one look that nothing came unwired |

### G3 · BP-87 — the type picker no longer lies

Previously the dropdown offered 15 types and the compiler could resolve **7**.

| # | Do | Expect |
|---|---|---|
| G3a | Add a parameter → open the **Type** dropdown | ⭐ **`FixedString32` / `FixedString64` are now offered** — the string support you asked about |
| G3b | Pick **`ushort`**, then **`uint`**, then **`Vector3`**, compile each | ✅ **No `BP1500`.** All four unsigned types and all four vector types now resolve |
| G3c | ⭐ Wire a **`ushort`** output pin into an **`int`** input pin | ✅ **Connects.** This was your condition for keeping the unsigned types — the coercion table had 8 entries, all signed; it now states C#'s own widening ladder (35 rungs) |
| G3d | Try wiring **`int` → `uint`**, or **`long` → `int`** | ❌ **Refused, correctly.** C# itself requires a cast there; a silent lossy coercion in a graph is a wrong-values bug you cannot see |
| G3e | ⚠ Look at an **existing** parameter's type | **Probably shows the wrong name — that is BP-114. Do not touch it.** See warning 1 |

### G4 · BP-109 / BP-110 — the end-to-end sample data

Three openable recipe assets shipped in Batch 22: **`SmokePatrol`** · **`SmokeGuard`** (Instance) ·
**`SmokeMathLib`** (Library). They are the *same files* a gate test runs, so what you see is what CI
checks.

| # | Do | Expect |
|---|---|---|
| G4a | Open all three from the recipe picker | They open; `SmokeMathLib` shows `Dispatch: Library` |
| G4b | Look at how `SmokePatrol` calls `SmokeMathLib` | A working `CallPeerBlueprint` — ⭐ **before Batch 22 this had never compiled at all**, in any asset, ever |

---

## C. Outputs on the Return node (BP-89) — ~4 min

| # | Do | Expect |
|---|---|---|
| C1 | Function graph in an **Instance** blueprint → select **Return** | An **Outputs** section, headed *"Outputs — one data-in pin on this Return node, and one data-out pin on every call site."* |
| C2 | With none declared | *"This function declares no outputs. Add one to return a value."* |
| C3 | Click **`+`** | Row appears; Return grows a data-in pin immediately |
| C4 | Rename it **shorter** than the default | ⚠ **BP-86 guard** — exact name, no `?`, no leftover tail. *(This is the `P1?am0` bug you hit.)* |
| C5 | **Ctrl+Z** after each of add / remove / rename / retype | ⭐ **One** undo step each, exact prior state |
| C6 | Add a third output → Ctrl+Z ×2 → Ctrl+Y ×2 | Stable. Batch 20 found and fixed an aliasing bug here — **most worth stressing** |

## D. `Status` visibility (BP-105 + BP-14) — ~3 min

BP-105 made `Status` and `Outputs` appear **only where the compiler actually reads them**:

| Asset dispatch | Outputs section | Status combo |
|---|---|---|
| **Instance** | ✅ shown | ❌ **hidden** |
| **Library**, 0 outputs | ✅ shown | ✅ shown |
| **Library**, ≥1 output | ✅ shown | ❌ hidden |
| **AiPrimitive** | ❌ hidden | ✅ shown |

| # | Do | Expect |
|---|---|---|
| D1 | Return node in an **Instance** function graph | **No Status combo.** ⚠ An older guide said to expect an editable one — correct before BP-105, wrong now. *This answers your question "what is the purpose of it?" — on an Instance graph it had none, which is why it is gone* |
| D2 | Return node in a **Library** function with **zero** outputs | Status **is** shown, editable, with a line saying why |
| D3 | Add an output to that library function | Status **disappears**; Outputs remains |
| D4 | Change Status where shown, then **Ctrl+Z** | One undo step ⇒ **BP-14 closes — tell me** |

## E. ⚠ Known gap — confirm, do **not** report as new

| # | Do | Expect |
|---|---|---|
| E1 | Add an output from the **Graph Signature window** (not the Return node), then **Ctrl+Z** | ❌ **Will NOT undo.** That is [BP-102](Blueprint_Issues_Detail.md#bp-102), already registered. Just confirm the asymmetry still exists |

## F. ⭐ The T-series — blocked **six** batches, still never run

Use section C's `+` to add three outputs to an **Instance** function graph, then run `T1`–`T7` from
[Blueprint_Gaps_Programme_RESUME.md](Blueprint_Gaps_Programme_RESUME.md).

⚠ **T7 is the one to watch:** a second output must **compile cleanly**. Any surviving *"multiple outputs
not supported"* message is a leftover defect — an older revision of that row wrongly told you to
*expect* it.

---

## Reporting

Send the **section id** (G1b, G3c, D1, T7…), what you saw vs expected, and the **exact diagnostic code**.
`BP5001` vs `BP1101` vs `BP1500` vs a Roslyn `CS` number point at completely different halves.
Screenshots for anything about wording or layout — precisely what 4,900 green tests cannot see.

⚠ **Two things I would especially like an answer on:**
1. **G2a** — does the peer call really show N named pins now? That is the fix for the defect you
   reported, and no human has seen it.
2. **G3a** — are `FixedString32/64` actually usable end-to-end, or only *offered*? You asked why
   functions could not take strings; this is the first half of the answer.
