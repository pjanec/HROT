<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: §1 (headline), §4 (the 2d redesign), §6 (probes), §7 (gates)
stale-below: nothing
known-rot: none
known-conflict: none
-->

# REPORT — Batch 87: ⭐⭐⭐ **the Details panel obeys the SURFACE, not the node**

> 📌 **Dispatch `55bf833`, scope frozen at `0477bb98e`** · **started at `f0d0ea8`** *(rule 1b, pushed
> before any code)* · **rule 7 ff-merge clean.**
> ⭐ **IDs allocated: NONE.** This batch **closes** `BP-327` and `BP-330`.
> ⭐ **`DEBT-AIB` partitions touched: none.**
> ⚠ **2e NOT STARTED** — the handoff marks it last and stoppable. §8.

---

## 1. ⭐⭐⭐ THE HEADLINE

| | |
|---|---|
| ⭐⭐ **gate 8 — the enumeration** | 🔴 **FOUR table hosts, not the three the handoff knew.** §3 |
| ⭐⭐⭐ **2d was redesigned, not built as specified** | the handoff's fix was **measurably impossible**; ⭐ **the user supplied the right frame** and approved building it. §4 |
| ⭐ **2a `BP-327`** | the dialog is drawn — reusing the shared drawer, ⛔ not the shared *window* |
| ⭐ **2b** | one attach path over an interface; the omission is now inexpressible |
| ⭐ **2c `B3`** | the control finally ASKS about selection |
| ⭐⭐ **probes** | 3 applied, **and one exposed a defect in my own rail.** §6 |

---

## 2. ⭐⭐ **2a — `BP-327`: the surface, and only the surface**

⭐ Batch 84 built the whole path — gesture → launcher → session → `Accept` → the run-state arm → the
declaration. ⛔ **`Open` returned an `IEditSession` and nothing drew it.**

| ⭐ decision | why |
|---|---|
| **reuse `ComponentEditDrawer`** | ruling 9 — the same drawer `InspectorWindow` and `ComponentEditWindow` already use |
| ⛔ **do NOT reuse `ComponentEditWindow`** | it is **entity-and-component shaped**: commits through `IInspectableSession.SetComponent` and self-terminates when the target ENTITY dies. ⚠ A variable has **no entity** at authoring time and commits to the **declaration**, chosen by `VariableEditCommit`. ⇒ ⭐ **share the drawer, not the window** |
| ⭐⭐ **every decision is a headless property** | `CanCommit`, `CommitRefusalReason`, `RefusalMessage`, `Ok`, `Cancel`. `Draw` only calls them — ⛔ a decision taken inline in an ImGui call is one no rail can reach, which is how a surface ships invisible |

### ⭐ Refusals, and why the two are timed differently

| outcome | when it surfaces |
|---|---|
| `RefusedRunning` | ⭐ **BEFORE the click** — OK is greyed with a tooltip. 📌 User, `2026-08-17`: *"showing explanatory tooltip would be better than allowing user to click the button and then saying that it is not possible."* |
| `LiveWriteUnavailable` | ⚠ **AFTER the attempt** — the run state ALLOWED the write and the mechanism did not arrive. ⛔ Greying up front would claim knowledge the dialog cannot have until it tries |

---

## 3. 🔴 **2b + GATE 8 — the enumeration found a FOURTH host**

```
search_graph  name_pattern=".*VariableTableControl.*"        → total 4
query_graph   MATCH (n)-[r]->(c:Class {name:'VariableTableControl'})
```

| # | host | before |
|---|---|---|
| 1 | `AiVariablesWindow` | ✅ the only one bound |
| 2 | `AiWatchWindow` | ⛔ `BP-330` — table private, no accessor |
| 3 | `VariableDetailsSection` | ⛔ the visual check's `D2`/`D3`/`D11` |
| 4 | 🔴 **`WatchPanelWindow`** | ⛔ **not in the handoff's list** |

⭐⭐ **The handoff said: *"if the graph finds a fourth, that is a finding — report it."*** 📌 `R-74`
again — **only the graph enumerates**; a grep for the two known hosts would have confirmed the guess
and missed this one.

⭐⭐⭐ **The fix is structural, not three more `Attach` lines.** Hosts declare `IVariableTableHost`;
the registrar binds them through ONE `AttachEditGestures`; `BoundTables` records what was actually
attached. ⇒ a **fifth** host cannot be lost to someone not remembering a fourth call.

---

## 4. ⭐⭐⭐ **2d — the handoff's fix was impossible, and the user supplied the right one**

### 🔴 What I measured, and reported instead of working around

The handoff prescribed *"a shared ORDERING TOKEN both arms bump."* ⛔ **The node arm has no discrete
event to bump on**, at four layers:

| layer | measurement |
|---|---|
| **`CanvasInput`** | `if (!ctrl && !shift && !Selection.Contains(node)) Selection.ReplaceWith(node);` ⇒ 🔴 **clicking an already-selected node is a DELIBERATE no-op**, so dragging a multi-selection does not collapse it. **Correct behaviour** — and it kills the signal at birth |
| **`SelectionState`** | a plain set: no version, no event |
| **the bridge** | `AiGraphCanvasWindow:371` invokes `AfterDraw` **per frame**; the bridge assigns `ActiveSubSelection` **unconditionally** |
| **`EditorSelectionStore`** | `if (Equals(current, value)) return;` |

⇒ bump-on-every-assignment ticks every frame *(the variables table could never appear)*;
bump-only-on-change **is** the failing equality test. ⭐ **I stopped and reported rather than inventing
a design.**

### ⭐⭐⭐ The user's reframing — and it dissolves the problem

> 📌 **User, `2026-08-18`:** *"it's not the selection what changes but actually the focus to different
> part of the UI (from MyBlueprint to graph canvas)… the editor selection cache should contain what the
> selected UI item comes from (what panel etc.). Otherwise we would need to report and handle the click
> to every possible UI component."*
> 📌 **And on scope:** *"no architect question needed, it is obvious that this behavior must be shared
> across blueprint/btree/hsm, just pls implement it."*

⭐⭐ **The competing arms are SURFACES, not payloads.** Comparing node identity was asking *"did the
node change?"* when the intent is *"which surface am I working in?"*

| new, in `AiShared` beside the store | |
|---|---|
| `SelectionOrigin` | `Unknown` / `GraphCanvas` / `VariableOutline` |
| `IDetailsSurfaceClaimant` | ⭐ **opt-in** — only surfaces that DRIVE the panel |
| `EditorSelectionStore.FocusedSurface` | the **latch** |
| `EditorSelectionStore.ActiveSubSelectionOrigin` | who **owns** the selection |
| `NotifySurfaceFocused` | de-duplicating, safe per frame |

| ⭐ design point | |
|---|---|
| ⭐⭐ **a LEVEL, not an edge** | the canvas's existing focus gate is edge-triggered *(`doc == _lastActivatedDoc` returns early)*, and an edge is exactly what cannot see this bug — **re-entering a surface with an unchanged selection IS the failing gesture** |
| ⭐⭐ **a LATCH, not a live read** | clicking INTO Details to edit takes focus from **both** contributors; a live read would answer "neither" and flip the panel mid-edit |
| ⭐ **claimants are explicit** | ⛔ the Watch, the Inspector and Details itself do **not** claim, or a window that does not drive the panel would steal it |
| ⭐ **the value test SURVIVES as secondary** | a selection can move without focus moving *(hotkey, programmatic)*. ⛔ Replacing rather than layering would trade one blind spot for another |

⭐ **No `NodeEditor.Core` change was needed** — which was the whole point of the user's framing.

---

## 5. ⭐ **2c — `B3`: the control asks**

📐 The chain was wired end to end and ⛔ **`VariableTableControl` never called `IsSelected`. Zero
references.** ⚠ **An INVERTED instance of the recurring pattern** — usually nothing constructs the
thing; here everything constructs and routes it and the last consumer never asks.

⇒ ⛔ **a rail on `IsSelected` proves nothing** *(it returned `true` throughout the defect)*. ⭐ The
rails ask `VisualStateOf`, which `DrawRows`/`DrawCell` read **and read nothing else from**. Selection
rides `Selectable`'s selected flag; change/pending ride the row background ⇒ **a row that is both shows
both**.

---

## 6. ⭐⭐⭐ **REVERT PROBES — and one caught a defect in MY OWN RAIL**

| probe | breaks | result |
|---|---|---|
| **A** — remove the `RegisterExtraWindow` attach | 2b | 🔴 `AHostRegisteredAsAnExtraIsBound` RED |
| **B** — `Selected: false` in `VisualStateOf` | 2c | 🔴 **3 rails RED** |
| **C** — the latch takes only its first claim | 2d | 🔴 `ReturningToTheSameSurfaceReclaimsIt` RED |

### ⚠⚠ **Probe A initially did NOT go red — and that was the rail's fault, not the probe's**

⭐ The first version of `AHostRegisteredAsAnExtraIsBound` called the internal bind helper **directly**,
so it stayed green while the production `RegisterExtraWindow` line was commented out. ⇒ 🔴 **`R-67` in
miniature, inside the rail written to catch `R-67`.** ⭐ **Rewritten to go through
`RegisterExtraWindow` with a real `WindowManager`**, and it reddens.

⭐ **A second rail corrected its own author**: `AnAcceptedCommitClosesTheDialog` went red as
`RefusedReadOnly` because my harness supplied no asset — there was nowhere to write the declaration.
⇒ the harness now supplies the **same** fake the row-59 rails use, so OK genuinely lands.

---

## 7. ⭐⭐ **GATES**

**Base commit for every RED below: `0477bb98e`.** ⭐ **Working tree CLEAN after every suite run.**

| # | gate | `--no-build`? | result | Δ vs baseline |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln` | n/a | ✅ **0 errors** | — |
| 2 | `dotnet test Hrot.Editor.AiShared.Tests` | ✅ | ✅ **1424 / 1424 / 0 skipped** | ⭐ **+27 — the new rails** |
| 3 | `dotnet test Hrot.Blueprints.Tests` | ✅ | ✅ **3767 / 3777 / 10 skipped** | **0** |
| 4 | `dotnet test Hrot.BTree.Editor.Tests` | ✅ | ✅ **615 / 615** | **0** |
| 5 | `dotnet test Hrot.Hsm.Editor.Tests` | ✅ | ✅ **551 / 551** | **0** |
| 6 | `dotnet test Hrot.Editor.Tests` | ✅ | ✅ **194 / 194** | **0** |
| 7 | `dotnet test Hrot.Diagnostics.Breakpoints.Tests` | ✅ | ✅ **143 / 143** | **0** |
| 8 | `tracker-counts.py --check` | n/a | ✅ **open 66 / done 201** | ⭐ **−2 / +2: `BP-327`, `BP-330`** |
| 9 | `rulings-check.py` | n/a | ✅ **59 / 59** | **0** |
| 10 | `design-digest.py --check` | n/a | ✅ **48 documents** | **0** |

⚠ **The three gates that take NO `--no-build`** *(out of solution — a stale bin lies)*:
`NodeEditor.Core.Tests`, `NodeEditor.UI.Tests`, `Fhsm.Tests`. ⭐ **Nothing here touches them** — the
diff is confined to `Hrot.Editor.AiShared`, `Hrot.Blueprints.Editor` and their tests.
⛔ **No golden moved** — this batch has no emit or persistence surface.
⭐ **Quarantine unchanged: 10 skipped in `Hrot.Blueprints.Tests`.** ⛔ **No new skip.**

### ⭐⭐ Gate 9 — **what each rail ASKS** *(the handoff's own requirement)*

| rail | asks |
|---|---|
| **2b** | the **CONSTRUCTED** `VariableTableControl.HasEditGestures`, reached through the **production** `RegisterExtraWindow` |
| **2c** | `VariableTableControl.VisualStateOf` — the method `DrawRows`/`DrawCell` read and read nothing else from |
| **2d** | `EditorSelectionStore.FocusedSurface`, driven by the claim sequence a designer produces |
| **2a** | the modal's own decision properties, which `Draw` only calls |

⚠ **What NONE of them can prove**, stated rather than implied: **that ImGui paints anything.** They
prove the code ASKS, CARRIES and ROUTES — which is the half that was missing in all four defects.

---

## 8. ⚠ **2e — NOT STARTED**

📐 `EditorSubsystem` passes a live-value provider for BTree *(`:2178`)* and HSM *(`:2188`)* and **none
for Blueprint**; **zero `ILiveBlackboardValueProvider` implementations exist under `Hrot.Blueprints.*`**.
⭐ The handoff marks it **last and stoppable**, and 2d's redesign consumed the batch's remaining room.
⛔ **Nothing half-built** — the tree contains no partial provider.

---

## 9. ⭐ What this batch says about the process

| ⭐ | |
|---|---|
| ⭐⭐⭐ **A measured impossibility is worth more than a delivered workaround** | 2d's handoff fix could not work. ⛔ Building *something* would have shipped a second broken panel-router; ⭐ reporting it got the RIGHT model from the user in one exchange |
| ⭐⭐ **`R-74` earned its keep again** | the handoff's "known: three" was wrong, and only the graph said so |
| ⚠ **A rail can carry the very defect it hunts** | probe A found `R-67` inside the anti-`R-67` rail. ⭐ **The probe is the only reason that was caught** — the rail was green and wrong |
