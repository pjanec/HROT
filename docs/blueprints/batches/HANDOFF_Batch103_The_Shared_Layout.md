<!--STATUS
state: LIVE
build-state: READY-TO-BUILD
updated: 2026-08-21
current-answer: this whole file — the Batch 103 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: the shipped default has ActivePerspective="Blueprint", which
  UX_Feature_Perspective_Restore.md rules is document-driven and must NEVER be restored.
  Item 103a names it and asks for the measured behaviour rather than assuming.
-->
# HANDOFF — Batch 103: **one shared layout**

> 📌 **Dispatched at `d3c370ffb`.** ⭐ Branch from it *(rule 7)*. ⛔ **Scope FROZEN at this sha.**
> ⭐ **Rule 3: your own ids.** ⭐ **Rule 1b: push `chore: started batch 103 at d3c370ffb` FIRST.**
> ⭐⭐ **`R-106`: a blocked item stops THAT ITEM, never the batch. Four verdicts.**
> ⭐ **`quick-check.sh` while working; the FULL gate table ONCE, at the end** *(`M-37`)*.

> ## ⭐⭐⭐ THE DESIGN IS ALREADY WRITTEN — **⛔ do not re-derive it**
> 📄 **[`docs/UX/UX_Feature_Layout_Defaults.md`](../UX/UX_Feature_Layout_Defaults.md)** — from the UX
> design session, **merged into this branch on `2026-08-21`.** ⭐ It carries the user's own ruling, the
> prior-art pass and the file layout. ⚠ **They are a DESIGN-ONLY session, now on hold** — ⭐ detailing
> their designs is our job, ⛔ but the decisions in it are the user's and stand.
> ⚠ **It predates `R-121`** *(the perspective-key rename)* — §4 below.

> ## ⭐⭐ WHY NOW
> ⭐ The user has a **fine-tuned layout in the Windows session** and does not want to lose it.
> ⭐⭐ **It is already committed** as `layout/default/` *(this dispatch's parent)* — **55 window entries
> and the full docking tree.** ⇒ ⛔ this batch does not author a layout; **it wires the one that exists.**
> ⭐⭐⭐ **And it is a precondition for the UI rails** *(`M-39`)*: driving gestures is only reproducible
> when the docking state is known.

---

## 1. ⭐⭐⭐ `103a` — **the layout is ONE UNIT, in TWO places**

📌 **The finding that widens the issue** *(their §"layout state lives in two files, in two roots")*:

| file | holds | root **today** |
|---|---|---|
| `imgui.ini` | docking geometry, positions, sizes | `%LocalAppData%\HROT\` *(`RaylibPresentationShell:128-131`)* |
| `fdp_windows.json` | **open/closed, active perspective, UI scale** | ⚠ **next to the exe** *(`WindowManager:437-438`)* |

⇒ ⛔ **resetting one without the other gives a HALF-reset.** 🔒 **User's ruling:** *"The json must live
next to the `imgui.ini` — both places (user and default)."*

### ⭐ What to build — **their design, in their order**

| # | | ⭐ note |
|---|---|---|
| **①** | `layout/default/{imgui.ini,fdp_windows.json}` → **copied to output** | ⭐ **the files are ALREADY COMMITTED** — add the `CopyToOutputDirectory` rule only |
| **②** | **one USER directory holds both** | ⭐⭐ **the seam already exists and is unused**: `SaveSettings(string?)` / `LoadSettings(string?)` both take a path and **nobody passes one** *(`LocalWindowController:75,94` call them bare)*. ⇒ ⛔ **no `WindowManager` change** — pass the path at the two call sites |
| **③** | **dev default ON: force-copy the defaults over the user pair on each run**, then load unchanged | 🔒 the user's own words. ⭐ **The reset is a file copy BEFORE load** ⇒ ⛔ the load path and ImGui's own persistence are untouched |
| **④** | **exit saves to the user location**, as today | ⛔ unchanged |
| **⑤** | **`File ▸ Layout ▸ Save current as default`** — copies user pair → source tree | ⚠ only valid from the repo; ⭐ locate it the way `ResolveAiBehaviorsDir` already does *(`EditorSubsystem:693-708`)*. ⛔ **Outside a repo: DISABLED WITH A REASON, not hidden** |
| **⑥** | **one-time migration** — if the new path has no json and the exe-adjacent one exists, copy it | ⭐ stops existing users silently losing their arrangement |
| **⑦** | ⛔ **DELETE the tracked root `imgui.ini`** | 📐 283 lines, **also `.gitignore`d**, no build-copy rule, **and the app never reads it** ⇒ ⭐ *"a tracked-but-ignored file that looks like a default is worse than none."* ⚠ `.gitignore` now carries a **negation for `layout/default/imgui.ini`** — ⛔ keep it |

### ⚠⚠ THREE THINGS TO MEASURE, NOT ASSUME

| ⚠ | |
|---|---|
| ⛔⛔ **`ActivePerspective` is `"Blueprint"`** | 📄 `UX_Feature_Perspective_Restore.md` rules `BTree`/`HSM`/`Blueprint` **document-driven — never restored**, because no document survives a restart. ⇒ ⭐ **REPORT what actually happens on a cold start with this default**: if it falls back to `Editor` that is *correct*; ⛔ **if it lands in an empty graph workspace, that is a defect and it is the ruling's own stated reason.** ⚠ **Do NOT edit the user's file to dodge the question** |
| ⚠ **the ini path is computed TWICE** | `RaylibPresentationShell:131` **and** `FdpApplication:93`, independently — ⭐ *"two apps, one convention, no shared helper."* ⇒ ⛔ **one helper, or say why not** |
| ⛔ **`%LocalAppData%` is Windows-only** | ⭐⭐ **the frame rails run on LINUX under Xvfb** *(`R-124`)* ⇒ the user directory must resolve on both. ⚠ **This is not in their design** — it predates the rails |

---

## 2. ⭐⭐ `103b` — **a rail that the default layout is not STALE**

⭐⭐⭐ **The failure this prevents is certain, not hypothetical:** the json names **55 window ids**. ⛔ Rename
or retire a window and its entry **silently orphans** — the layout still loads, that window just never
appears, and nothing says so.

| ⭐ assert | |
|---|---|
| **①** | ⭐⭐ **every window id in `layout/default/fdp_windows.json` resolves to a window the production registrars actually create** — ⛔ and every id they create is either IN the file or explicitly exempt |
| **②** | the file parses and the docking `[Docking][Data]` block is present |
| ⚠ **on failure, name the ids** | ⭐ *"`ai_details_hsm` is in the layout and no window claims it"* — ⛔ not `Assert.Equal failed` |

⚠ **Expect this rail to be RED or noisy on the first run** — ⭐ **that is information**: the layout is a
snapshot of one session's windows and some entries may already be stale *(e.g. `Entity Blueprints`, which
is a TITLE-shaped id among otherwise snake_case ones)*. ⛔ **Report the list; do not prune the user's file
to make it green.**

---

## 3. ⭐ `103c` — **only if `103a`+`103b` land early**

⭐ `BP-385` — the panels sync run state **from inside `Draw`**, which is why a headless reader sees
`Planning` *(the smoke suite found it)*. ⚠ **Measure the blast radius across table hosts, then decide** —
⛔ do not rush a change that touches every host.

---

## 4. ⛔⛔ THE CONFLICT WITH `R-121` — **state it, do not resolve it here**

📌 **`R-121`** renames the perspective key **`"Editor"` → `"Scenario"`** *(with a layout migration,
because `OwningPerspective` and `CurrentPerspective` are persisted)*.
⇒ ⚠ **the default layout shipped here uses the CURRENT keys.** ⭐ When the rename lands, **this file must
migrate with it** — ⛔ or the shipped default silently stops matching.
⭐ **Note it in the report; the rename is not this batch.**

---

## 5. ⛔ NOT IN THIS BATCH

the `R-121` perspective rename · the icon-atlas fix and the synthetic-input rail *(`M-39`)* · the
`ClusterState` run-state work *(`M-38` — an architect question first)* · reviving the 174 *(`S1″`)* ·
anything from `DESIGN_Details_Panel_View_Switching.md` *(`R-27`)* · **any edit to the user's layout files
beyond what `103a` requires**.

---

## 6. ⭐ GATES — **ONCE, at the end**

⭐ Baseline = Batch 102's table, base **`d3c370ffb`**. ⚠ **State the environment** *(Xvfb or not)* — 📌
Blueprints is **3870 / 0 / 10** with a display and **3862 / 0 / 18** without.
⭐ **Extra rows:** the layout rail's result **with the orphan list if any** · the measured cold-start
perspective *(§1)* · `Hrot.Smoke.Tests`.
⛔ `Hrot.ClusterRunner.Integration.Tests` stays out *(`BP-378`)*.
