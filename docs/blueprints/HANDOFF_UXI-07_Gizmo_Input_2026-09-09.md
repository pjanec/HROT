<!--STATUS
state: LIVE
updated: 2026-09-09
current-answer: §1 (what to do next) — read that FIRST. §2 is what shipped, §3 the open items,
  §4 the traps that cost time today.
design-basis: docs/designs/gizmos-1/gizmo-input-focus-design.md §6.2a/§6.2b/§6.2c (the arbiter and
  the input contract) · docs/UX/UX_Feature_Tool_Model.md §4.7e/§4.7f/§4.7g (the three defects found
  by running the editor) · docs/blueprints/Architect_Question_68_Gizmo_Focus_Registry.md §6 (the
  approved ruling) · docs/blueprints/RULINGS.md R-144.
stale-below: nothing — this document is new.
known-rot: none.
known-conflict: none identified.
-->

# HANDOFF — `UXI-07` gizmo input, `2026-09-09`

> **Branch: `claude/axis-c-e2-asset-shell`.** Everything below is pushed there. Nothing is on `main`.
> **Written because the operator may not be reachable tomorrow.** If you are a fresh session picking
> this up, read §1, then §4 — §4 is where the time went.

---

## 0. ⭐⭐ What this day was, in one paragraph

The `Q68` ruling *(one shared gizmo focus registry)* was approved and built. **Then the operator ran the
editor**, and that hour found **four production defects that ~8 000 green rails could not see** — three
of them pre-existing and one a genuine design gap. ⭐⭐ **The registry was the smaller half of the day.**
🔒 The larger half is a measured lesson: **this subsystem's defects are invisible to rails by
construction**, because a broken gizmo still *draws*.

---

## 1. ⭐⭐⭐ WHAT TO DO NEXT — **in order**

| # | | why |
|---|---|---|
| **①** | 🔴 **RE-TEST `T1` IN THE EDITOR.** 📄 [`TESTPLAN_UXI-07_Manual_Windows.md`](../UX/TESTPLAN_UXI-07_Manual_Windows.md) | ⛔ **`CE-259p` (the entity hit-test) is BUILT AND GATED BUT NEVER RUN IN THE PRODUCT.** The operator's session ended first. ⚠ Every other fix today was confirmed by a human; this one is not |
| **②** | ⭐ The exact steps: Edit Shape on a polyline entity ▸ handles appear ▸ **without finishing**, right-click a `TargetMemory` entity in the ENTITY INSPECTOR ▸ *"Mark Target for N Units…"* ▸ click an entity on the map | ✅ **expected now:** handles survive, stay drawn while the picker is armed, the click PICKS and closes the picker, handles live again. ⛔ A right-click should cancel cleanly with **no** *"A task was cancelled"* |
| **③** | ⚠ **If the pick still does not land**, the next suspect is `EntityPickerGizmo._hoveredValid` — it is set **only** in `OnDragUpdate`, which fires on mouse MOVEMENT. A click with no prior movement over the entity would leave it stale | 📐 not measured; stated as the next hypothesis, **not** a finding |
| **④** | ⭐ Then `CE-259l` — *should `InputCaptureBinding`'s `wantsRawInput` bit exist at all?* | 🔒 it is the ROOT of `CE-259k`, it has a design basis pointing one way, and it is the last structural item here |
| **⑤** | ⛔ **Do NOT write a manual test for 68-A.** There is nothing to see | §2.1 — measured |

---

## 2. ✅ WHAT SHIPPED — **seven commits, all on `claude/axis-c-e2-asset-shell`**

### 2.1 `Q68` / `CE-259c` — the shared focus registry *(the approved ruling)*

🔒 **User: *"68 most recent leans approved"*** ⇒ 68-A **YES** · 68-B **A** · 68-C **C2** · 68-D **now**.
📄 [`Architect_Question_68` §6](Architect_Question_68_Gizmo_Focus_Registry.md) · `R-144`.

⭐ `GizmoFocusRegistry` in `Fdp.Toolkits`; **ONE INSTANCE** created at `MapInteractionPack.cs:99` and
passed to both arbiters. `_focusedGizmo` went **14 + 47 occurrences → 0**.

⛔⛔ **AND THE HONEST CAVEAT, which a fresh session must not lose:** 📐 measured — **every
exclusive-focus arming path reachable from the editor already goes through `ToolController`**, so
`CancelOtherArbiter` enforced exclusivity everywhere already. ⇒ ⭐ **the registry changes the MECHANISM,
not the BEHAVIOUR. There is NO editor gesture that distinguishes before from after.** Its value is a
structural guarantee against a future bypass, plus deleting a duplicated three-method implementation.
📄 `gizmo-input-focus-design.md` §6.2b, *"WHAT THE REGISTRY IS NOT"*.

### 2.2 The four operator-found defects

| id | what | state |
|---|---|---|
| ⭐⭐⭐ **`CE-259k`** | **Six exclusive-focus gizmos never asked for raw input, so they were DEAF** — they drew and ignored every click. `MeasureGizmo`, `PointSequenceGizmo` *(Draw Area + Draw Route)*, `ObstaclePlacementGizmo`, `ModalBoxSelectionGizmo`, `FdpLocationPickerGizmo`, `EntityPickerGizmo` | ✅ fixed · ✅ **CONFIRMED BY THE OPERATOR** — three dead toolbar tools came back |
| ⭐⭐ **`CE-259n`** | **A raw RELEASE was delivered without its PRESS** ⇒ right-clicking an ImGui PANEL destroyed a map gizmo | ✅ fixed *(`RawButtonGate`)* · ✅ **CONFIRMED** — handles survived |
| ⭐ **`CE-259o`** | **Cancelling a pick threw *"A task was cancelled"*** at the top level — `async void` had no caller to observe the `OperationCanceledException` | ✅ fixed · ⚠ not re-tested |
| 🔴 **`CE-259p`** | **`MapCanvas.PickTopmostEntity` returned `null` in EVERY production host** ⇒ the picker could never pick | ✅ fixed · ⛔⛔ **NOT VERIFIED IN THE PRODUCT — this is item ① above** |

### 2.3 ✅ What the operator CONFIRMED works

⭐⭐⭐ **Step 4b's suspend/resume — `PushModal` — WORKS.** *"handles survived the rightclick, gizmo stayed
while picker armed."* 🔒 **The first human confirmation of a capability the design specified years ago and
nobody had built.**
⭐ Also confirmed: Draw Area / Draw Route connect clicked points · Measure renders its rubber line ·
rotate, drag, marquee and Escape all track **1:1 with no doubling** *(which answers `T2b` — the shared
slot did not introduce double delivery)*.

---

## 3. ⛔ OPEN — **what is NOT done**

| id | | why it was left |
|---|---|---|
| ⭐⭐ **`CE-259l`** | **Should `InputCaptureBinding`'s `wantsRawInput` bit exist?** 🔒 §5.1 of the design gives the primitive **ONE** flag *(`1 = Exclusive, 0 = Shared`)* and defines the primitive ITSELF as the request for raw input. ⛔ The second bit is an as-built addition **with no design record**, and it now gates the very thing the primitive exists to declare — that is what made `CE-259k` possible | it changes the **terminal contract** for every gizmo in FDP. `CE-259k`'s per-gizmo declaration is correct under either answer, so nothing is blocked |
| ⚠ **`CE-259m`** | **A toolbar button is always enabled even when it cannot apply**, so its refusal reads as a dead button. ⭐ The context menu is component-aware *(`if (hasPolyline)`, and it selects first)*; the toolbar row is hand-coded and reads no state | **this is step 5**, and step 5 is unbuilt: `ShowOnToolbar` is set on **6** descriptors and read by **0** consumers; nothing binds `ActiveModalChanged`. Same root as *"there is no Select button"* — it exists, it works, it never lights up |
| ⚠ **`CE-259h`** | The pick token's `AnchorId` is documented network-stable and is stamped with an ECS `Entity.Index` | latent contract violation, needs two live nodes |
| ⚠ **`CE-259l` corroboration** | `DebugGizmoLayerCaptureTests.SC_B28_4` + `SC_B28_6` are **STANDING REDS** — deterministic 3/3 at HEAD, 2/2 on base ⇒ pre-existing. They assert exactly the capture behaviour `CE-259l` is about | ⛔ `R-131`: a red being tolerated is a defect to resolve, not a filter to keep |
| ⚠ | `IgApplication._activeSequenceGizmo` — IG's remote area/route authoring, 8+ sites | fire-and-forget, needs `Activate` not `PushModal`; explicitly out of `Q68`'s scope |
| ⛔ | **The SPATIAL routing path was never compared between the two arbiters** | only the RAW-INPUT path was. This is the row that would flip 68-A if the two turn out to be legitimately different |
| ⚠ | `UXI-07` steps **5** and **6** *(toolbar binds `ActiveModalChanged`; central Escape)* | unbuilt. Step 6 is why Escape does not cancel from anywhere |

---

## 4. 🔴🔴 THE TRAPS — **read this before touching anything here**

### ⭐⭐⭐ ① A BROKEN GIZMO STILL DRAWS — **which is why rails cannot see these defects**

📐 `UpdateAndDraw` is unconditional; input delivery is not. ⇒ **every defect today presented as *"the tool
renders and ignores my clicks"***, never as a crash or a log line. ⛔ **A green rail suite is not evidence
that this subsystem works.** 🔒 Four production defects, ~8 000 rails green, found in one hour by a person.

### ⭐⭐⭐ ② TWO ENTITY HIT-TESTS, AND THE DEAD ONE LOOKS FINE

| | |
|---|---|
| ⛔ **dead** | `IMapLayer.PickEntity` — **was** `=> null` in all 7 production layers |
| ✅ **live** | the terminal's `FindTopmostInteractivePrimitive`, reached via `GizmoInteractionStartedEvent.Token.Target` — **this is why SELECTION works** |

⚠ **`CE-259p` routed the first to the second.** ⛔ Do not add a third.

### ⭐⭐ ③ THE `--no-build` STALE-BINARY TRAP BIT ME TODAY

📌 I restored a file and re-ran with `--no-build`, and the test ran the **inverse-edited binary** and
reported a clean-looking result. ⇒ ⭐ **gate every run on `0 Error(s)` from a real build.**
`scripts/quick-check.sh` refuses to test a failed build; a bare `dotnet test --no-build` does not.

### ⚠ ④ `Fdp.Presentation.Tests` IS UNSTABLE — **filter narrowly**

📐 A broad filter **crashes the test host** *(pre-existing; it crashes on the base tree too)*. ⭐ Run
`--filter` per class. The two `DebugGizmoLayerCaptureTests` reds are **pre-existing and deterministic** —
verified byte-identical on base with the changes stashed.

### ⭐⭐ ⑤ THE PROCESS MISTAKES I MADE — **so you do not repeat them**

| | |
|---|---|
| 🔴 **I asserted a manual test was NEEDED without measuring it** | I told the operator `CE-259k` was required *"to test the registry"*. One grep over the arming paths would have shown the registry has **no** observable behaviour change. ⇒ ⭐ *"this needs a manual test"* owes a claim table like any other lean |
| 🔴 **I wrote a test plan citing UI I never verified existed** | `T1` said *"click the Pick button in the Mission panel"*. There is no such top-level control — it is drawn per task property inside a Details tab. ⇒ ⭐ **check the affordance is reachable before writing the step** |
| 🔴 **`T2a` was unreachable by construction** | it required a right-click to open a context menu while a tool was armed — and that gizmo cancels on right-press. ⇒ **withdrawn**, not rewritten |
| ⚠ **I nearly shipped a duplicate surface** | I measured *"the editor passes no report sink"* and concluded refusals were invisible. ⭐ One step further showed `FdpLog` already lands in the **Message Log** window's *"NLog (Global)"* tab. ⇒ ⛔ **do not build a sink; the mechanism works** |

### ⭐ ⑥ WHERE A TOOL REFUSAL SURFACES *(you will need this)*

`ToolReport` → no sink in `EditorSubsystem` → `FdpLog` → **Message Log window, "NLog (Global)" tab**, with
a status-bar badge: `[Tools] tool 'Edit' did nothing — <reason>.` 📄 §4.7e.

---

## 5. 📐 The gate set for this area — **what a batch here should run**

```bash
bash scripts/quick-check.sh FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj "FullyQualifiedName~Diagnostics.Gizmos"        # 204
bash scripts/quick-check.sh Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj "FullyQualifiedName~Adapter|FullyQualifiedName~ViewportInteraction"   # 70
bash scripts/quick-check.sh Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj "FullyQualifiedName~Tools|FullyQualifiedName~Gizmos"          # 65
bash scripts/quick-check.sh Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj "FullyQualifiedName~Gizmo"                                                     # 54
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --no-build \
  --filter "FullyQualifiedName~RawButtonGateTests|FullyQualifiedName~GizmoLayerEntityHitTestTests"                                                            # 12
python3 scripts/tracker-counts.py --check && python3 scripts/design-digest.py --check && python3 scripts/rulings-check.py
```

⚠ **Builds to keep green:** `GizmoMap.Presentation` · `Fdp.Presentation` · `Fdp.Toolkits` ·
`Hrot.Presentation` · `Hrot.Editor` · `Hrot.IG` · `Hrot.CGF` · `Hrot.SimHost`.

---

## 6. ⭐ The new rails, and what each one exists to catch

| rail | catches |
|---|---|
| `GizmoFocusRegistryTests` *(14)* | the shared slot's semantics — exclusivity ACROSS arbiters, the steal, suspend-without-dispose, **`RecipientFor`'s ORDER** *(holder FIRST — a first draft of the ruling had it backwards)*, and no double delivery |
| `NoExclusiveFocusGizmoForgetsToAskForRawInput` | ⭐⭐ a **seventh** deaf gizmo. A source scan over every `*Gizmo.cs`, with an explicit `spatiallyRouted` allow-list |
| `ThePackGivesBothArbitersTheSameFocusSlot` | the forwarding rail — one registry EACH would satisfy every signature and fix nothing |
| `TwoAdaptersBypassingTheControllerCannotBothHoldFocus` | exclusivity without the controller convention |
| `RawButtonGateTests` *(6)* | the unpaired release — ⭐ **and 2 of them guard the STUCK-DRAG case**, so the fix cannot be "fixed" back into a hang |
| `GizmoLayerEntityHitTestTests` *(6)* | the entity hit-test, incl. *"a capture binding must not blind it"* |

🔒 **Every one of these was inverse-edit red-proofed**, and in each case the rails that STAYED green are
named in the commit — that is what distinguishes a real rail from a vacuous one.
