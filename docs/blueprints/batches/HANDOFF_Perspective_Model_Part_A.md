<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for Part A of the perspective model — the unknown-id refusal, the
  Editor→Scenario rename, and the two phantom perspectives the visual check exposed. ⛔ This file carries
  NO design: every item cites a section of DESIGN_Perspective_Unification.md.
known-conflict: none.
-->
# HANDOFF — **perspective model, Part A**

> 📌 **Dispatched at `<STAMP>`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push the started-marker BEFORE any
> code.** ⛔ **No PR.** ⭐ **You allocate the ids** *(rule 3)* — `A0…A7` below are **placeholders**.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`docs/DESIGN_Perspective_Unification.md`](../../DESIGN_Perspective_Unification.md)** is THE perspective
design doc. ⭐ **It carries the model, the target subsystem→perspective table, the "Global is not a
perspective" rule, and the three architectural diagrams** *(window-visibility resolution · the class view ·
the switch sequence)*.

⛔⛔ **Read §1, §1b, §1c, §1d and §5 before writing code**, and report per obligation ③: *"the design carries
N classes and M sequences; what I built matches / deviates HERE and why."*
⭐⭐ **A deviation is a FINDING you argue in the report AND fold back into the design** *(obligation ⑤)* —
the report is ephemeral, the design is not.

📄 Context, not scope: [`PROGRAMME_Unification_And_Harness.md`](../../PROGRAMME_Unification_And_Harness.md)
*(the charter — this is its **step 1**)*.

## 1. ⭐⭐⭐ THE ITEMS — **in this order**

| # | task | design basis | gate |
|---|---|---|---|
| 🔴🔴 **A0** | ⛔⛔ **FIRST, AND NOTHING ELSE IS SAFE WITHOUT IT.** `WindowManager.SwitchPerspective` must **refuse an unknown perspective**: log and no-op. ⭐ Also make the layout-restore path fall back to a valid perspective instead of trusting the stored string | **§3 A0** · `UX/UX_Feature_Perspective_Restore.md` §3 *(specified there, never built)* | a rail: switching to `"NoSuchPerspective"` leaves `CurrentPerspective` **unchanged**, logs once, and every window still draws |
| ⭐⭐ **A6** | **Make `owningPerspective` REQUIRED on `FindResultsWindow`** — delete the `?? "Authoring"` default | **§1c** *"the LATENT generator"* | it does not compile without an explicit perspective ⇒ a phantom perspective is **unconstructible** |
| 🔴 **A5** | **Kill the phantom `Global` perspective.** The asset-browser Find-Results window must be `WindowScope.Global` with an **EMPTY** `OwningPerspective`. ⚠ Needs a **scope parameter** on `FindResultsWindow` | **§1c** *(the ruling, the two bugs, and the Orchestrator pattern to copy)* | ⭐ **two assertions**: `GetPerspectives()` does **not** contain `"Global"`, **and** that window is visible from a different perspective *(it was not before — that is bug ②)* |
| ⭐⭐ **A1** | **Rename the editor's perspective id `Editor` → `Scenario`** at the **8** window-registration sites | **§3 A1** · charter **D2** | `GetPerspectives()` returns `Scenario · BTree · HSM · Blueprint` for `--mode editor` |
| **A2** | Keep the display label correct — the id and the label now agree, so drop the redundant alias if one remains | **§3 A2** | the toolbar/menu still reads "Scenario" |
| **A3** | ⚠ **Touch `layout/default/*` ONLY if the shipped default should open on Scenario** | **§3 A3** — 📐 `ActivePerspective` is currently `"Blueprint"` ⇒ **no migration is required.** ⛔ Do not invent one | the stale-layout rail stays green |
| **A4** | Follow the rename through the **44** test occurrences | **§3 A4** | ⛔ **a test asserting a perspective COUNT must be corrected to the measured set, not deleted** |
| **A7** | ⭐ **Delete the dead `Authoring` and `Analysis` perspectives** + the two `Authoring` windows. ⛔ **HOLD the two COMPARISON panels** — the user has not confirmed deleting that feature | **§3 A7** · **§8-E** | `GetPerspectives()` is unchanged by this *(neither was ever live — §1)* |

⛔⛔ **A0 before everything. A6 before A5** *(remove the generator, then fix the instance)*.

## 2. ⚠ WHAT WILL BITE — measured, so you do not re-derive it

| ⚠ | |
|---|---|
| ⛔ **`"Editor"` is not always the perspective** | 📐 **33** non-test occurrences of the literal, of which **8** are perspective registrations. The rest are subsystem names, mode tokens, type names. ⭐ **Per-site judgement — ⛔ not `sed`** |
| ⭐ **`FindResultsWindow` hard-codes `WindowScope.PerspectiveBound`** | ⇒ A5 cannot be done by changing an argument; the class needs the scope parameter |
| ⭐ **The icon comes from the LIST** | `PerspectiveToolbarSection.cs:92` iterates `GetPerspectives()` and draws one icon per entry ⇒ ⭐ **removing the phantom from the list is what removes the icon.** ⛔ Do not special-case the icon renderer |
| ✅ **The Windows menu's `"Global"` group is CORRECT** | `WindowManager.cs:787-798` groups `WindowScope.Global` windows under that label. ⛔ **Do not "fix" it** — §1c says why it is right |
| ⚠ **`StrideMock` is a real perspective** | it is in `perspectiveMap` and `stridemock` is a valid mode ⇒ ⛔ do not treat it as phantom |

## 3. ⛔ LANE & SCOPE

⭐ **Your surface:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/` *(A0, and the `FindResultsWindow`
scope parameter)* · `Hrot.Editor.AiShared/Windows/FindResultsWindow.cs` · `EditorSubsystem`'s registration
sites · the layout defaults · the affected tests.

⚠⚠ **This touches `Hrot.Editor.AiShared`, which is the FROZEN variable-model area *(`R-128`)*.** ⭐ **A5/A6
are sanctioned here** because they are **window-registration plumbing, not the variable model** — ⛔ **but if
an item turns into editing the variable model, the Details panel, or the blackboard surface, STOP and
report.**

⛔ **Not this batch:** Part B *(CGF growing the asset perspectives)* — it needs the freeze decision in
**§8 51b-C**. ⛔ Nothing about the harness, goldens or conformance.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** · goldens as a **diff
shape** · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the ids you
allocated** *(rule 5)*.

⭐⭐ **Row 8 — the integration invariant.** This changes **window registration and perspective resolution**,
which is cross-cutting UI. ⇒ name and run the suites that would break if it broke:
`Hrot.Blueprints.Tests ~Hrot.Blueprints.Tests.Editor` *(the gate for anything touching `EditorSubsystem`)* ·
`Hrot.Editor.Tests` · the `Fdp.Presentation` window-manager rails · **and the stale-layout rail**
*(`TheDefaultLayoutIsNotStaleTests`)*, which is the one that catches a layout/id mismatch.

⚠ **Known baseline quirks — do not re-derive:** `tracker-counts.py --check` counts only `**BP-` rows.
`Fdp.Presentation.Tests` crashes ~18–20 cases in *(`BP-419`, pre-existing, `R-131` owns it)* — ⛔ **report it
as pre-existing with the base sha; do not fix it here.**

⭐ **Rule 4/7:** re-sync and **pull the coordinator branch again before your final commit.**

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Perspective_Unification.md`](../../DESIGN_Perspective_Unification.md)**
— §3's step table and §1c's defect description, marking anything superseded. ⛔ **Do not leave the design
describing a state the code has left**, and ⛔ do not put design content in your report.
