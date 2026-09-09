<!--STATUS
state: LIVE
updated: 2026-09-09
current-answer: §2 is the test list, in VALUE order. §1 is setup. §4 is what to report back.
design-basis: docs/UX/UX_Feature_Tool_Model.md §4.8–§4.12 (the tool model and the picker protocol) ·
  docs/designs/gizmos-1/gizmo-input-focus-design.md §6.2a/§6.2b (the two-arbiter defect and the
  approved registry) · docs/blueprints/Architect_Question_68_Gizmo_Focus_Registry.md §6 (the ruling).
stale-below: nothing — this document is new.
known-rot: none yet.
known-conflict: none identified.
-->

# TEST PLAN — `UXI-07` by hand, on Windows

> **Branch `claude/axis-c-e2-asset-shell` — pull the latest; `CE-259k` (the six deaf gizmos) is required for `T1`.**
> The Windows session normally sits on `claude/reset-working-branch-qd1qpv`, so check out or merge this
> branch first. Nothing here is on `main`.

---

## 0. ⭐⭐⭐ Why this exists — **and why it is worth an hour**

📐 **Measured, this issue's own history:** the `~8 000`-rail suite caught **none** of the seven defects the
user found by opening the editor. Everything `UXI-07` shipped rests on rails, and the rails are honest
about their own blind spot — 📄 `UX_Feature_Tool_Model.md` §4.10 records the tool model as
**unverifiable headlessly**.

⚠⚠ **CORRECTED `2026-09-09` — the original framing of this section was WRONG.** It said today's shared
focus slot *"widened the gap on purpose"* and that the hour was needed to validate it. 📐 **Measured
since: every exclusive-focus arming path reachable from the editor already goes through `ToolController`,
so the registry changes the MECHANISM, not the BEHAVIOUR — there is nothing to see** *(§6.2b, "WHAT THE
REGISTRY IS NOT")*. ⭐ **The hour still paid, for a different reason: it found `CE-259k`, three dead
toolbar tools no rail could see.**

| ⭐ what this hour buys that no rail can | |
|---|---|
| ⭐⭐⭐ **the SUSPEND/RESUME capability** *(`T1`, step 4b)* | ⭐ a REAL behaviour change *(suspend vs destroy)*, and it has never once been run by a person |
| ⭐⭐⭐ **input actually reaching the gizmo at all** | 🔴 **this is what paid.** `CE-259k`: six gizmos declared exclusive focus and never asked for raw input ⇒ they DREW and ignored every click. Rails assert the routing DECISION; only a person sees a tool that renders and does nothing |
| ⭐⭐ **the absence of DOUBLE input** *(`T2b`)* | ✅ **answered `2026-09-09`** — rotate, drag, marquee and Escape all tracked 1:1, no doubling |
| ⛔ **68-A, the shared registry** | **NOTHING** — see the correction above |

---

## 1. Setup

```powershell
git fetch origin claude/axis-c-e2-asset-shell
git checkout claude/axis-c-e2-asset-shell     # or merge it into the Windows lane branch
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj
dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- --mode all
```

| | |
|---|---|
| ⭐ **`--mode all`** puts every subsystem in one process — the editor, IG, CGF and the replay browser | that is what lets one session cover `T1`–`T9` |
| ⭐ **`--mode editor`** alone is enough for `T1`–`T7` | faster to start if IG is not needed |
| ⚠ **the cluster boots PAUSED** | a stationary map is not a fault. None of these tests need the sim running |
| 🔒 **do NOT modify `scenarios/hill-attack/`** | it is the user's scenario — load it, never save over it |
| ⚠ **if nothing draws at all** | that is a *map* problem, not a *tool* problem — stop and report it as such, the tool tests will all read as false failures |

---

## 2. ⭐⭐ THE TESTS — in value order

> ⭐ **Run them in order and stop at the first hard failure**, reporting it. `T1` and `T2` cover what is
> genuinely new; `T3`–`T6` are regression checks on what shipped earlier this week; `T7`–`T9` are the
> long tail.

### ⭐⭐⭐ `T1` — **A pick over a half-drawn route (THE new capability)**

🔒 **This is the whole of step 4b and `Q27-F`.** Until `PushModal` existed, arming a picker called
`Activate`, which **destroyed** whatever was armed underneath.

| step | |
|---|---|
| **①** | Select an entity that has a **route**. Right-click it ▸ **"Edit Route"** |
| **②** | Click **two or three waypoints** on the map — a visibly half-drawn route |
| **③** | ⛔ **WITHOUT pressing Escape or finishing**, arm a PICK. ⚠ **Two routes, and the first is far easier** — 📐 measured `2026-09-09` after the operator reported *"don't see any Pick button anywhere"*:<br/>⭐⭐ **(a) the ENTITY INSPECTOR context menu** — right-click an entity that has `TargetMemory` ▸ **"Mark Target for N Units…"** *(`EditorSubsystem.cs:2325`)*. ⛔ The item only appears on an entity WITH `TargetMemory`. ⭐ Right-clicking inside a PANEL does not reach the map tool, so it will not cancel the tool underneath — which is exactly what this test needs.<br/>⚠ **(b) the `Pick` button** — it is **not a top-level control**: it lives in the **Details** window's **Mission** tab, drawn per TASK PROPERTY by `BehaviorUiCompiler.cs:175,206`, and only for a location/entity-typed property. ⇒ it needs an entity the mission service offers behaviours for, a task added, and that task to have such a property |
| **④** | Click a point on the map to complete the pick |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| the half-drawn route **stays visible on the map the whole time** — during the pick, not just after | 🔴 **the route DISAPPEARS when the pick arms** ⇒ `PushModal` fell back to `Activate` |
| after the pick lands, the route tool is **live again**: clicking the map adds the **next** waypoint | 🔴 clicking does nothing, or **starts a NEW route from scratch** ⇒ the resume failed / the tool was disposed |
| the picked coordinate **fills into the mission task field** | 🔴 field stays empty ⇒ the pick never completed |

⭐ **Why this one is first:** it is the only test here whose capability did not exist a week ago. 📐 The
rails prove the *decision* (`PushPickerSuspendsTheToolUnderneathRatherThanDestroyingIt`); only a person
can see the route still drawn on screen.

---

### ⭐⭐⭐ `T2` — **Two tools that both bypass the controller (TODAY's change)**

🔴 **This is the one thing that changed today.** Before, `GlobalGizmoManager` and `DataDrivenGizmoSystem`
held **independent** focus slots, so two "exclusive" tools could hold input at once whenever neither went
through `ToolController`.

#### ⛔⛔ `T2a` — **WITHDRAWN `2026-09-09`. It could not have worked, and it was not the operator's fault.**

⚠ **As written it said:** arm Measure, click once, then *"without cancelling it, right-click an entity ▸
Rotate."* 🔴 **`MeasureGizmo`'s own state machine cancels on RIGHT-PRESS** — and the context menu needs a
right-click. ⇒ **the only gesture that opens the menu is the one that ends the measurement**, so the
two-tools-at-once condition is unreachable by that route. 📌 The operator reported exactly this:
*"right click cancels the measure and activated context menu where i selected rotate and it worked."*
⭐ **That is CORRECT behaviour, not a failure.**

⛔⛔ **And the deeper reason it is withdrawn rather than rewritten:** 📐 measured — **every exclusive-focus
arming path reachable from the editor already goes through `ToolController`**, so `CancelOtherArbiter`
enforces exclusivity on all of them. ⇒ ⭐⭐ **the shared registry changes the MECHANISM, not the
BEHAVIOUR, and NO editor gesture distinguishes before from after.**
📄 [`gizmo-input-focus-design.md` §6.2b](../designs/gizmos-1/gizmo-input-focus-design.md), *"WHAT THE
REGISTRY IS NOT"*, carries the enumeration and the correction.

🔒 **The lesson for this document:** *"this change needs a manual test"* is a claim like any other and
owes a claim table. **It was asserted, not measured.**

#### ⭐⭐ `T2b` — **NO DOUBLE INPUT** *(the hazard the shared slot creates)*

| step | |
|---|---|
| **①** | With any single tool armed *(Measure is easiest to see)*, **drag slowly across the map** |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| the gizmo tracks the cursor **1:1** | 🔴 **it moves twice as far as the cursor**, jitters, or a single click registers as two ⇒ both arbiters are delivering the same event. This is exactly what the `owner` parameter guards, and it is the most likely way today's change goes wrong |

⭐ **Watch for this in EVERY later test too** — it would show up anywhere, and it is subtle enough to
dismiss as "feels laggy".

---

### ⭐⭐ `T3` — **The Measure checkbox is not a dead toggle** *(IG — `CE-259e`)*

📌 **The defect:** IG's Measure checkbox drove its own copy of the tool. Arming any other tool cancelled
Measure, but the **checkbox stayed ticked over a tool that was gone**, so the operator had to cycle it off
and on to get it back.

| step | |
|---|---|
| **①** | Switch to the **IG** perspective. In the gizmo settings panel tick **`MeasureTool.Active`** |
| **②** | Confirm the measure tool responds on the canvas |
| **③** | Arm **any other tool** — Spawn, Edit, Rotate |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| the checkbox **unticks itself** the moment the other tool arms | 🔴 stays ticked ⇒ the dead toggle is back |
| re-ticking it **works first time** | 🔴 needs an off→on cycle ⇒ the same defect, one layer down |

---

### ⭐⭐ `T4` — **Re-targeting: Edit on entity A, then entity B**

📌 **A rail caught this AFTER the deletion had shipped** — `PushModal` had no re-target rule, so a second
push stacked instead of replacing. Worth a human check because the rail tests the controller, not the map.

| step | |
|---|---|
| **①** | Right-click entity **A** with a shape ▸ **"Edit Shape"**. Confirm its vertex handles appear |
| **②** | Right-click entity **B** ▸ **"Edit Shape"** |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| **B**'s handles appear and **A**'s are gone | 🔴 **both sets of handles are live** ⇒ the modal stacked instead of re-targeting |
| pressing **Escape once** leaves *no* tool armed | 🔴 Escape has to be pressed **twice** ⇒ a stale entry is still on the stack |

---

### ⭐ `T5` — **`Select` is a real state, not a dead button**

🔒 `Q27` ruling: the null modal tool is a **state**, and it supersedes `UXI-02`'s proposal to delete the
button.

| step | |
|---|---|
| **①** | Arm **Edit** or **Route** on an entity |
| **②** | Press the **Select** toolbar button |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| the armed tool **disarms**, and Select reads as the active tool | 🔴 the button does nothing ⇒ still a dead button |
| clicking entities **selects** them again normally | 🔴 clicks still go to the old tool |

---

### ⭐ `T6` — **Escape cancels everything modal** *(`R-125`)*

🔒 *"Every modal closes on `ESC`"* — a general ruling, not a fix to one dialog.

| step | |
|---|---|
| **①** | For each of **Measure · Edit · Route · Rotate · a `Pick`** — arm it, then press **Escape** |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| every one disarms, and the map returns to plain selection | 🔴 any tool that survives Escape — **name which one** |
| a cancelled `Pick` leaves the **tool underneath still armed** *(if there was one)* | 🔴 Escape kills **both** ⇒ the pop is sweeping instead of popping |

---

### ⭐ `T7` — **Entity placement**

| step | |
|---|---|
| **①** | Open the **Spawner** panel, choose a type, press its place/spawn control *(or the toolbar's **Place Entity**)* |
| **②** | Click the map |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| the entity appears **where clicked**, with the properties chosen in the panel | 🔴 appears at `(0,0)`, or with **default** properties ⇒ the pending-properties hand-off broke |
| placement mode ends after one placement *(or continues, if that is the panel's stated mode — just note which)* | ⚠ **report the observed behaviour** rather than judging it; the intended mode is not settled in the design |

---

### ⭐ `T8` — **Zone obstacle placement**

| step | |
|---|---|
| **①** | Open zone authoring, start **obstacle placement**, click the map |

| ✅ expected | ⛔ FAILURE SIGNAL |
|---|---|
| the obstacle is placed, and arming another tool **cancels** placement cleanly | 🔴 placement survives another tool arming ⇒ this adapter is still bypassing the arbiter |

---

### ⚠ `T9` — **Area / route authoring driven from ExCon — EXPECT THE OLD BEHAVIOUR**

⛔⛔ **NOT a failure if this one behaves like it always did.** `IgApplication._activeSequenceGizmo` is
**deliberately not converted** — it is fire-and-forget (`Activate`-shaped, not a picker), 8+ call sites,
and it is explicitly out of scope in 📄 `Architect_Question_68` §6.

| step | |
|---|---|
| **①** | Trigger a remote area or route creation from **ExCon** and complete it |

| ✅ expected | worth reporting |
|---|---|
| it works exactly as before | ⭐ **anything that got WORSE** — that would mean the shared slot reached a path nobody expected |

---

## 3. ⛔ What is NOT being tested here, so nobody reads too much into a green

| | |
|---|---|
| ⛔ **cross-node gizmo interaction** | `CE-259h` — the pick token's `AnchorId` is documented network-stable and carries an ECS entity index. **A latent contract violation, not a demonstrated defect**, and it needs two nodes plus its own measurement |
| ⛔ **the SPATIAL routing path** | only the RAW-INPUT path was compared between the two arbiters. `T2b` would catch the worst case; a subtler spatial mis-route would not show up here |
| ⛔ **anything under load or at speed** | these are single-operator, paused-sim checks |

---

## 4. ⭐⭐ What to report back

⭐ **Per test: the id, and PASS / FAIL / NOT-REACHED.** ⛔ For a FAIL, the three things that make it
actionable and that a screenshot alone does not give:

1. ⭐⭐ **which of the two failure signals** in that test's table you saw — they distinguish different bugs
2. ⭐ **what was armed at the time**, and in which perspective (editor / IG / CGF)
3. ⭐⭐ **whether it reproduces** — a tool defect that fires once in five is a *different* problem from one
   that fires every time, and this repo has both

⚠ **"NOT REACHED" is a genuinely useful answer** — if a panel or button cannot be found, that is a finding
about the surface, not a failed test. Say so plainly rather than substituting a different path.

🔒 **And the most valuable single line you can send back is a `T2b` observation either way** — *"the drag
tracked the cursor exactly"* is real evidence that today's shared slot did not introduce double delivery,
and nothing in the 392-rail gate can produce it.
