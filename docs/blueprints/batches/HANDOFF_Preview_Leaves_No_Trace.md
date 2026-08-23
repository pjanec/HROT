<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for the preview-rewind gap — the network-id counter survives a preview,
  so preview N+1 does not repeat preview N's ids. ⛔ Carries no design: see
  DESIGN_Deterministic_Network_Ids.md, which holds the requirement, the seam gap and the UML.
known-conflict: none. ⚠ Touches EditorSubsystem.cs and Hrot.Network.Orchestration; the net's part-C
  batch owns Hrot.SystemTests goldens and the gizmo batch owns Hrot.IG — no overlap (§3).
-->
# HANDOFF — **a preview must leave no trace** *(the network-id counter, and item ⓪)*

> 📌 **Dispatched at `<STAMP>`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`HN-`**, tracker **Area J** — 📐 the series stands at **`HN-011`**, so start at
> `HN-012`. ⚠ **Coordinate with part C, which also allocates `HN-`** — ⭐ take a block from `HN-020`.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Deterministic_Network_Ids.md`](../../DESIGN_Deterministic_Network_Ids.md)** —
`READY-TO-BUILD`. ⭐ **§1 is the requirement in the user's words; §2 is the measured mechanism; §3 the seam
gap; §4 the five items; §5 the UML; §6 the rails.** ⭐ Report per obligation ③ and ⭐⭐ **fold deviations
into the design** *(obligation ⑤)*.

⚠⚠ **Read §8/§9 before you read anything else about this area.** ⛔ **This file has been wrong TWICE**
*(coordinator, both times)*: v1 named the wrong seam, v2 concluded *"not needed at all"* from measuring
one workflow. 📌 **Charter `D6` still names `RegisterWorldResetObserver` — 📐 that is measurably NOT on
the preview path.** ⛔ **Do not build against `D6`'s seam.**

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design | gate |
|---|---|---|---|
| 🔴🔴 **⓪** | ⭐⭐⭐ **ENUMERATE WHAT ELSE A PREVIEW FAILS TO REWIND — BEFORE FIXING THE ALLOCATOR.** 📐 `PreviewClusterOpHandler` rewinds **only `_liveRepo`**; `Initialize` builds **two** mutable non-ECS things and hands both to `NetworkSpawningSystem` — `idAllocator` *(`:1101`, confirmed broken)* and **`NetworkEntityMap`** *(`:895`, **unknown**)*. ⇒ ⭐⭐ **report the list.** ⛔ **A one-thing fix to a class-of-bug is a finding, not a fix** | **§2**, §4 ⓪ | ⭐ **the list, with a verdict per item** — ⛔ *"I only looked at the allocator"* is not one |
| 🔴🔴 **①** | ⭐⭐⭐ **SAVE / RESTORE the counter around the preview bracket** — capture on `TriggerLoadingPreview`, restore on `TriggerUnloadingPreview`. ⛔ **NOT reset-to-a-constant** *(a fixed constant can collide with authored ids; the pre-preview value cannot)* | §4 ①/④/⑤ | ⭐⭐ **two consecutive previews in ONE process produce IDENTICAL ids** — ⛔ **and it must be seen to FAIL first**; 📐 it fails today |
| ⭐⭐ **②** | **Add a READ member to `INetworkIdAllocator`** — 📐 the interface is **two methods** and neither can read the counter *(§3)*. ⚠ **5+ implementations** | §3, §4 ② | ⭐ every implementation compiles and is covered by ③'s rail |
| 🔴🔴 **③** | ⭐⭐⭐ **PIN WHAT THE COUNTER MEANS — THE TWO IMPLEMENTATIONS DISAGREE.** 📐 `Hrot.Core.Network`: `_next = 1`, `Interlocked.Increment` ⇒ **pre**-increment, `_next` = **last issued**. 📐 The editor's **private nested** one: `_next = 1000`, `_next++` ⇒ **post**-increment, `_next` = **next to issue** | §4 ③ | ⭐⭐⭐ **`Reset(Read())` is an IDENTITY, asserted on EVERY implementation** *(parameterised)* — ⛔ a comment will not hold this |

⚠ **`⓪` FIRST.** ⭐ If it finds `NetworkEntityMap` is also stale after a preview, **say so and stop at the
report** rather than fixing two subsystems under one item — ⛔ that is a second batch, and it may want a
small *"what preview saves"* list *(§4 ⑤ — ⭐ **two participants justify one; one does not**)*.

## 2. ⚠ WHAT WILL BITE

| ⚠ | |
|---|---|
| ⭐⭐ **THREE types named `SequentialIdAllocator`** | `Hrot.Core.Network` *(real)* · **`EditorSubsystem`'s `private sealed` nested one** *(`:557` — ⭐ **this is the one the editor and the harness actually run**)* · `EditorHarness`'s test nested one. ⛔ **Fixing the first does not touch the second** |
| ⭐⭐ **`PreviewClusterOpHandler` lives in `Hrot.Network.Orchestration` and knows only `_liveRepo`** | ⇒ it must be **given** what to save. ⭐ **Keep the list in ONE place** — ⛔ not half in `ExitPreviewMode` |
| ⭐ **preview brackets the CLOCK too** | `EnterPreviewMode` → `SwitchToContinuous()`; `ExitPreviewMode` → `SwitchToDeterministic(new HashSet<int>())`. ⚠ **Do not disturb that** — ⭐ it is the one-node-cluster trick the debug pause also uses |
| ⭐ **the authored-load path must NOT regress** | 📐 `HN-010` pins ids `1000`–`1007` **from `scenarios/hill-attack/scenario.json`** — ⭐ authored entities never touch the allocator, and that must stay true |
| ⚠ **`HrotEditLoadHandler` has NO production construction site** *(tests only)* | ⭐ noted so you do not assume the cluster edit-load path is live. ⛔ **Not yours to chase** — ⚠ it wants a design-corpus check first *(unreferenced ≠ unintentional)* |

## 3. ⛔ LANE & SCOPE

⭐ **Yours:** `Hrot.Network.Orchestration/Handlers/PreviewClusterOpHandler.cs` ·
`Fdp.Toolkits/NetworkSpawning/Abstractions/INetworkIdAllocator.cs` + its implementations ·
`Hrot.Editor/EditorSubsystem.cs` *(the nested allocator + the preview controller)* · your new rails.

⚠ **TWO OTHER BATCHES MAY BE LIVE:** 📄 `HANDOFF_Regression_Net_Part_C.md` *(`HN-`/`MX-`, **Area J** —
⭐ **shares your id series, take `HN-020+`**; owns `Hrot.SystemTests` goldens) · 📄 `HANDOFF_Gizmo_Schema.md`
*(`ST-`, Area I — owns `Hrot.IG`)*. ⛔ **No file overlap with either.** ⭐ **Rule 4: pull the coordinator
branch before your final commit.**

⛔ **Not this batch:** the **cluster** reset *(📄 design §7 — `mgmt-1` §5.7's master-owned broadcast is
**built**; ⛔ do not touch it, and ⛔ **do not unify the two call sites** — they reset in opposite
directions)* · collapsing the duplicate nested allocator *(⭐ real, ruling 9, ⛔ its own change)* ·
`HN-011`'s loader leak.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the ids you allocated**.
⭐ **File every id in the same commit that uses it** — 📌 part B's `HN-011` lived only in a commit message.

⭐⭐ **Row 8 — the integration invariant.** ⭐ This changes a **preview/orchestration** path, so name and run
`bash scripts/run-system-tests.sh` *(⭐ it now covers `SystemSmoke` **and** `SystemModes` — `HN-009`)*,
`Hrot.SimHost.Tests` *(⭐ `PreviewClusterOpHandlerTests` lives there)* and `Hrot.Editor.Tests`.
📐 **Baseline: `Hrot.SystemTests` `57 / 57`.**

⚠ **Known baseline quirks:** `tracker-counts.py --check` is blind to `HN-` rows. `Fdp.Presentation.Tests`
crashes ~18–20 cases in *(`BP-419`, `R-131`)*. `tools/ai-debug-mcp` `verify.mjs` fails pre-existing.
`rulings-check.py` emits **2 staleness WARNs** — ⭐ already named, **not yours**.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Deterministic_Network_Ids.md`](../../DESIGN_Deterministic_Network_Ids.md)** —
§4's items as built, **§5's diagrams made true**, and ⭐⭐ **`⓪`'s enumeration written into §2**, which is
the fact the next person needs and the one this file has twice failed to state correctly.
