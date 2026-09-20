<!--STATUS
state: LIVE
updated: 2026-09-20
current-answer: ⭐⭐⭐ READ THE TOP OF THIS FILE — the "SESSION 2026-09-20 (i)" block (S-5) is the live
  state; (h) is S-4b, (g) is S-4, (f) is S-3e.
  ☑ UXI-11 slices S-1, S-2, S-3, S-3b, S-3c, S-3d (Windows-VERIFIED), S-3e AND S-4 ARE BUILT: one
  store, one request, one writer, one announcement on every node — ONE PLACE (MapInteractionPack) that
  builds them all — and right-click SELECTS on both inspector panels, with the DER inspector wired to
  the host selection by NETWORK id.
  ☑ S-5 IS BUILT TOO: losing the selection cancels that entity's edit (ruling ②).
  NEXT IS S-6 — the remote-map dispatcher becomes a requester; echo suppression moves to egress.
  ⛔⛔ S-5 CORRECTED ITS OWN OWNING DESIGN, red-proved: UX_Feature_Tool_Model.md §4.14 prescribed
  NotifyToolEnded, which deliberately does NOT tear the gizmo down and would have left it armed and
  drawing (the mirror of CE-259q). Cancel() is wrong the other way — it unwinds the whole stack. The
  member ruling ② needs is NEW: IToolController.CancelArmedOn(Entity). §4.14 now carries the correction.
  ☑ S-4b CLOSED S-4's map gap: the vendored terminal now tags Started with its button, so §2.3 is met
  on EVERY surface — inside the selection nothing moves, outside it replaces, empty space clears.
  ⭐ NO NEW FIELD: the button rides in the interaction callback's existing actionId slot and in
  GizmoInteractionBatch.ActionId, both of which already carry a MapMouseButton for RawInput. Left is 0,
  so every un-migrated producer and any un-migrated sender on the wire keeps its old meaning exactly.
  ⭐⭐ The rule is BUTTON-SPECIFIC and that is the whole reason the button was needed: a LEFT-click
  inside a multi-selection must still narrow it to one. A shared guard would have removed that silently.
  ⚠ S-4b edits FDP/ExtDeps/GizmoMap (2 sites) — done on the user's explicit nod.
  ⚠ KNOWN LIMIT, pre-existing: the empty-space clear is LOCAL-ONLY (a canvas anchor does not survive
  the ingress resolve filter). ⛔ OUT OF SCOPE, per its own design: the map menu's CONTENTS.
  ⚠ Still openly unmet, each saying so: two synchronous editor facade seams (§2.7.7 deviation ③);
  panels PROJECT rather than subscribe (§2.7.8 deviation ②); ClearAll still has 0 callers.
  📄 The as-builts are UX_Feature_Selection.md §2.7.6 (S-1), §2.7.7 (S-2), §2.7.8 (S-3), §2.7.9 (S-3b),
  §2.7.10 (S-3c), §2.7.11a (S-3d, the Windows result), §2.7.12 (S-3e) and §2.7.13 (S-4) — read those,
  not the summaries here, before starting S-5.
  Branch: ui (the stable lane branch, R-148).
  ⛔ The 2026-09-15 block below (distributed persistence + ownership) is DONE and is now HISTORY —
  nothing from it is in flight. Older STRANDS below it are older history still; do NOT act on any of
  them unless explicitly told to continue one.

stale-below: ⛔ EVERYTHING below the "SESSION 2026-09-20 (i)" block is HISTORY, newest first —
  including the (d), (c), (b) and (a) blocks (S-3b, S-3, S-2, S-1) and the 2026-09-19/20 block, whose
  plans are now DONE. Do not quote any of it as current state.
known-rot: none open in this file.
related-designs:
  - docs/UX/UX_Feature_Selection.md — owns UXI-11; §2.7 is the target state, §2.7.5 the slice order.
  - docs/UX/PLAN_Interaction_UX_Backlog.md — the re-verified UX ledger (2026-09-19) and the sequence.
  - docs/UX/UX_Issues.md — the UXI register; note ✅ = designed, ☑ = done, 🟡 = partially built.
  - docs/SNAPSHOT_Map_Interaction_Architecture.md — measured snapshot of the map/interaction as built.
⚠ HOUSEKEEPING 2026-09-20: this STATUS block was 604 lines — the whole programme history lived inside
  the comment, so a post-compaction reader met ~590 lines of dead strands before the live answer.
  The dead text was MOVED VERBATIM to "## ⛔ HISTORY — older STATUS strands" at the foot of this file.
  Nothing was deleted.
-->
# ⭐⭐⭐ RESUME — **the UI / variable implementation lane**

## ✅ FIXED `2026-09-20` — **CGF BUILT ITS MAP LAYER WITH NO CAMERA; THE WHOLE 2-D MAP WAS DEAD**

🔒 **One missing named argument.** `CgfSubsystem.cs:1598`:

```csharp
_cgfGizmoLayer = new DebugGizmoLayer(31, _cgfGizmoBuffer, _cgfInteractionBus!);   // ⛔ no camera:
```

⇒ the layer's `Camera2D` stayed **`default`** — `zoom=0, offset=(0,0)` — and
`Raylib.GetScreenToWorld2D`, which is `(screen − Offset) / Zoom + Target`, **divided by zero.** Every
mouse position became `NaN`, every hit-test comparison `false`, every click fell through to the canvas.

⭐⭐ **The final diagnostic printed both instances side by side, which is what named it:**

```
frame=0   pickable=0   | camera zoom=1 target=(0,0) offset=(640,360)   ← camera fine, buffer empty
frame=763 pickable=708 | camera zoom=0 target=(0,0) offset=(0,0)       ← 708 pick boxes, NO camera
```

⛔ `zoom=0, offset=(0,0)` is not a corrupted camera — it is a **default-constructed struct**, i.e. one
that was never assigned. ⇒ two `DebugGizmoLayer` instances, and **the one holding the primitives had no
camera.**

| ⚠ why this hid for so long | |
|---|---|
| ⭐⭐ **DRAWING is unaffected** | the map looked perfect; only INPUT was dead. ⛔ There is no error anywhere on this path |
| ⭐ **the camera was TWO LINES ABOVE** | `_canvas = new MapCanvas()` at `:1550`, its offset set at `:1551`, the layer built at `:1598` and added to that very canvas at `:1599`. 🔒 The silent-default rule exactly: **a caller that HAD the dependency and did not pass it** |
| ⛔ **every rail stayed green** | nothing covered a production construction site's ARGUMENTS |

### ⭐ What landed

| | |
|---|---|
| ✅ **the fix** | CGF passes `camera: _canvas.Camera` |
| ⭐ **it reports itself now** | a layer with no camera WARNS once on its first `Update`: *"screen↔world is a divide by zero and NOTHING ON THIS MAP CAN BE CLICKED"*. ⛔ It still runs — drawing and headless rails are legitimate — it just stops failing invisibly |
| ⭐⭐ **`EveryProductionGizmoLayerIsGivenACamera`** *(new rail)* | every production construction site passes `camera:`. ⚠ Parses the **balanced argument list**, not one line — CGF's site was one line but the editor's and IG's span five, and a line-based scan would have called those clean. ⭐ **Red-proved against the real bug:** removing CGF's camera reddens it and names `CgfSubsystem.cs:1609` |
| ⭐ **camera hardening, kept** | the `NaN` guards and `Update`'s recovery stay — they were aimed at the wrong producer but the seam genuinely was unguarded, and `Zoom`'s setter had no check at all while `SetZoom` did |

⚠⚠ **A correction to my own diagnosis, worth keeping:** I read `zoom=0` as *"a `NaN` poisoned the
camera"* and built guards for that. ⛔ It was never poisoned — **it was never set.** ⭐ The guards are
still right, but the actual defect was a missing argument, and the two log lines printed side by side
are what made it obvious. 🔒 **Print the whole state, not the suspicious field.**

## ⛔ SUPERSEDED — **the NaN reading that led here** *(kept: the guards it produced are live)*

### 🔴 ROOT CAUSE NARROWED `2026-09-20` — **THE CAMERA'S SCREEN→WORLD TRANSFORM RETURNS `NaN`**

⭐⭐⭐ **The second `[SelDiag]` run named it outright:**

```
terminal canvas-fallback at (NaN,NaN) — frame=763 pickable=708
                                        nearest=#0 at 3.4e38 world units
```

| what it says | |
|---|---|
| `pickable=708` | ⭐ **the pick boxes are ALL THERE.** The projector, the buffer and the frame timing are fine |
| `at (NaN,NaN)` | 🔴 **the CLICK'S WORLD POSITION is `NaN`** |
| `nearest at 3.4e38` | that is `float.MaxValue` — every distance came out `NaN`, so `d < best` was never true. ⭐ Corroborates the `NaN` independently of the printed position |

📐 `worldPos = Raylib.GetScreenToWorld2D(screenPos, camera)` = **`(screen − Offset) / Zoom + Target`**.
⇒ 🔒 **one non-finite camera field makes every conversion `NaN`, every hit-test comparison `false`, and
every click fall through to the canvas — permanently, with no error anywhere.**

⭐ **It also explains ALL THREE reported symptoms with one cause**, which none of the seven earlier
hypotheses did: clicking never selects *(no hit)* · the marquee never shows *(its `Start`/`Current` are
`NaN`, so the band is degenerate)* · the free-space context menu still works *(it falls back to the
canvas anchor `-1` and never needs a world position)*.

⛔⛔ **AND IT IS NOTHING TO DO WITH `UXI-11`.** The first run already proved the selection chain
behaves correctly on the input it is given; this proves the INPUT is poison.

### ✅ FIXED AT THE SEAM — **a camera that cannot be poisoned**

📐 All three write seams were unguarded: `Zoom` set *(no check at all — `SetZoom` guarded `<= 0`, so the
guard was reachable through only one of two doors)* · `Target` set · `FocusOn(position)`, whose callers
pass **entity positions**, which is where a `NaN` most plausibly enters.

⭐ Each now **REJECTS and LOGS LOUDLY** rather than repairing silently — the camera stays usable and the
offending caller is named. 🔒 Same shape as `EmitPickBox`'s §6.8 `networkId == 0` guard: the rule lives
on the seam that owns the invariant, so every caller gets it.

⚠ **This is the CONTAINMENT, not the whole fix.** ⛔ It stops one bad write destroying the map, but the
**producer is still unknown** — the diagnostic now prints `camera zoom=… target=… offset=…` on every
canvas-fallback press, so the next run names the poisoned field, and the guard's warning names the
caller the moment it tries again.

## ⛔ SUPERSEDED — **the narrowing that led here** *(kept: it records what was ruled out)*

### 🔴 `2026-09-20`: NOTHING ON THE EDITOR'S 2-D MAP IS PICKABLE

⭐⭐⭐ **The `[SelDiag]` run settled it. Operator clicked ON an entity:**

```
[SelDiag] Started anchor=#0 button=Left resolved=NULL alive=False boxSelecting=False band=True
[SelDiag] serving mode=Clear   reason=Map.EmptyClick  count=0
[SelDiag] serving mode=Replace reason=Map.RubberBand  count=0
```

🔒 **`anchor=#0` is the CANVAS FALLBACK.** The terminal creates that token **only** when
`FindTopmostInteractivePrimitive` returns nothing *(`GizmoMap…/DebugGizmoLayer.cs:194`)*. ⇒ the click did
not miss a handler — **the hit-test found no pickable primitive under the cursor.**

⇒ ⭐⭐⭐ **`UXI-11` IS EXONERATED, and the log proves it POSITIVELY rather than by elimination:** every
stage downstream did exactly the right thing with the input it was given — an empty-space click cleared
*(`Map.EmptyClick`)*, and the tiny-drag commit ran a box select that legitimately matched nothing
*(`Map.RubberBand count=0`)*. ⛔ The selection machinery is not broken; it is being told "empty space",
correctly, because that is what the terminal saw.

⚠ `band=True` confirms the marquee state now exists on this host *(§2.7.16)*. The band does not SHOW for
a click because a click is not a drag — that is correct behaviour, not the reported defect.

### ⭐ What is left, and the instrument for it is already in

📐 A pick box is emitted by `EntityPresentationGizmoShared.EmitPickBox` and found by
`BoxAnchorId != 0` *(the hit-test's `:550` skip and `:563` identity)*. ⚠ And `EntityPresentationGizmo.cs:94`
returns early when `networkId == 0` — so an id-0 entity would be **invisible**, and the operator SEES
entities ⇒ they have ids. **Two possibilities remain:**

| the next run says | meaning |
|---|---|
| `pickable=0` | the pick boxes are **not in the frame the hit-test reads**. The projector is not running, or the buffer is empty at `canvas.Update()` time |
| `pickable=N nearest=#id at D` | they ARE there and the click missed by `D` world units ⇒ a geometry/size question *(the box is `8×8` WORLD units — at a zoomed-out view that is sub-pixel)*, not a wiring one |

⭐ `[SelDiag] terminal canvas-fallback …` now reports exactly that, on every canvas-fallback press.

## 🔴 OPEN — **OPERATOR REPORT `2026-09-20`: "mouse clicking does not select"**

> 🔒 **User, after the `S-4`/`S-4b` Windows pass:** *"Mouse clicking does not select, marquee rubber band
> not shown."* · then, to my question: *"Right click open context menu if free space clicked."*

⚠⚠ **HALF OF THIS IS ANSWERED, HALF IS NOT — do not close it.**

| symptom | status |
|---|---|
| **marquee not shown** | ✅ **EXPLAINED AND FIXED** — §2.7.16. The box-select logic ran on all five hosts; only the editor and ReplayBrowser ever registered a `RubberBandGizmo`, so on IG, SimHost and CGF the drag worked and was invisible. ⛔ **Not a regression from `S-4b`** |
| 🔴 **clicking does not select** | ⛔ **UNEXPLAINED.** The host was never established |

⭐ **What is RULED OUT, measured:** input reaches the frame *(the context menu opens)*, so this is not
§4.15's click-latch disease · the vendored left-press block is byte-identical to before `S-4b` · the
system's left branch is unchanged *(`isRight` is false for a left press)* · `ClearAll` is safe *(its
branch returns before the `Entities` read)* · `GizmoMap.Presentation.Tests` and the `SC_GZ025_*`
publication rails stayed green.

⭐⭐ **`2026-09-20`, LATER — THE BISECT AND A CORRECTION TO THIS FILE'S OWN EVIDENCE.**
📐 The operator built the commit **before `S-3e`** (`1476031a5`): **clicking and the rubber band fail
there too.** ⇒ ⛔ **not `S-3e`, not `S-4`, not `S-4b`, not `S-5`.** The gap is older than every slice.
🔒 **AND THE STRIDE DATAPOINT IS REAL — operator, from the screen:** *"both the rubberband and left
click was making the entities selected VISUALLY on the map."* ⇒ ⭐⭐ **the 2-D map click WORKED at
`S-3d`, on the Stride host.** ⛔ I briefly downgraded §2.7.11a check 4 to *"never demonstrated"* because
its LOG argument was unsound; that was an over-correction and is reverted — **a weak argument is not a
false result.**
⇒ ⭐⭐⭐ **THE WINDOW IS NARROW AND BOTH ENDS ARE NOW KNOWN:** it worked on **Stride at `S-3d`**, and it
fails on the **plain editor at `1476031a5`** *(a DOCS-ONLY commit on top of `S-3d`)*. ⇒ ⛔ **the
difference is very likely the HOST, not the commit** — the Stride window and the standalone editor run
the same `EditorSubsystem` behind **different frame loops** *(§4.15: `StrideInspectorWindow.PumpFrame()`
is a hand-written copy of clusterrunner's sequence)*. ⭐ **That is the next thing to measure.**

⭐ **Five mechanisms RULED OUT by measurement** *(so the next session does not re-walk them)*: the editor
does register the request/notify pair *(pack `:1886` < `Initialize()` `:2129`)* · it does tick the gesture
system *(`:2637`)* · the gizmo layer and the gesture system share one bus *(`:1929` → `:2549`)* · that bus
IS swapped *(`GizmoInteractionModule:73`)* · the editor does register that module *(`:2114`)*.

⭐⭐⭐ **THE INSTRUMENT IS NOW IN THE CODE** *(`2026-09-20`)* — `[SelDiag]`, two lines, the same technique
that settled the 3-D side at `S-3d`:
`SelectionInteractionSystem` logs every gesture it reads *(anchor, button, whether it RESOLVED, whether a
band state exists)*; `SelectionRequestSystem` logs every request it SERVES. Operator-paced, not
frame-paced, and deliberately **not behind a flag** — a diagnostic you must switch on is one the next
operator will not have switched on. ⭐ It reads three ways:
**no line at all** ⇒ the event never reaches the system *(upstream: hit-test, input gating, bus)* ·
**`resolved=NULL`** ⇒ the anchor resolves to nothing *(§6.8 "no id, no pick target" — the entity has no
usable `NetworkIdentity`)* · **`resolved=#n` but no `serving` line** ⇒ the request is never served
*(downstream: scheduling)*.

⭐ **The older open question, and the cheapest instrument:** **which host** — editor, Stride editor window, IG
or SimHost — and **does left-click select there on the commit BEFORE `c1f5487c0`?** ⛔ Until that is
known, attributing this to `S-4b` or `S-5` is a guess. ⚠ A suspect worth checking first:
`SimHostVisualization.cs:359` still has a `?? new SelectionInteractionSystem(...)` fallback — a host
reaching it would get an instance with **no selection view and no rubber band**, i.e. clicks that go
nowhere. `SimHostApp.cs:598` passes the pack's instance, so the fallback is unreachable **on that
path**; no other caller was enumerated.

## ⭐⭐⭐ SESSION `2026-09-20` (i) — **`S-5`: LOSING THE SELECTION CANCELS THAT ENTITY'S EDIT**

> 🔒 **User ruling ②, `2026-09-10`:** *"if entity becomes unselected, it should cancel any editing on the
> entity losing the selection"*.

☑ **Done.** 📄 As-built: [`UX_Feature_Selection.md` §2.7.15](../UX/UX_Feature_Selection.md).

### ⛔⛔⛔ It corrected its own owning design, and the correction was RED-PROVED

`UX_Feature_Tool_Model.md` §4.14 prescribed *"the tool armed on that entity ends via `NotifyToolEnded`"*.

| candidate | measured | verdict |
|---|---|---|
| `NotifyToolEnded` | **deliberately does not tear the gizmo down** — *"the gizmo ENDED ITSELF, so there is nothing of ours left to tear down"* | ⛔ would leave it **armed and drawing** — the mirror of `CE-259q` |
| `Cancel()` | unwinds the **whole** stack | ⛔ kills a tool on a different, still-selected entity |
| ⭐ **`CancelArmedOn(Entity)`** *(new)* | pops every entry on that entity **with** its `CancelFocused` teardown, resuming what each suspended | ✅ |

⭐ **Implementing it as §4.14 said reddens two rails** — that is how the design error was established
rather than argued. §4.14 now carries the correction and the proof *(obligation ⑤)*.

### ⭐ The shape

**EDGE** = `SelectionChangedNotification` *(its first real edge consumer — what §2.7.8 said it was for)*.
**PREDICATE** = `ISelectionState.IsSelected`, read **live** off the one store — ②'s own wording is a
per-entity question, ⛔ never a diff of two sets *(which would need a latch, `R-126`)*.
**HOME** = `SelectionNotificationSystem`, which already is *"what a selection change causes"* — ⛔ not a
parallel system *(ruling 9)*. **WIRING** = one constructor argument in `MapInteractionPack`, which builds
**both** the controller and the selection ⇒ all five hosts, one place.

⚠ `Entity.Null` is exempt by construction — a target-less tool is not editing an entity.

### 🔴 One defect found while wiring it

`SelectionNotificationSystem` opened with `if (inspector == null) return;` ⇒ a host with no inspector
context would have skipped the cancel **silently**. The early return is gone.

## ⭐⭐⭐ SESSION `2026-09-20` (h) — **`S-4b`: THE MAP'S RIGHT-CLICK. §2.3 IS MET EVERYWHERE**

> 🔒 **User:** *"the right click itself should deselect the entity unless the already selected group
> right clicked"* · *"include the empty-space clear"*.

☑ **Done.** 📄 As-built: [`UX_Feature_Selection.md` §2.7.14](../UX/UX_Feature_Selection.md).

### 🔴 The defect was one argument

`GizmoMap.Presentation/Layers/DebugGizmoLayer.cs:218` emitted `Started` for a right-release with
`actionId = 0`, and the proxy tool emits the **same kind** for a left-press ⇒ the two gestures were
indistinguishable downstream, so every press took the left branch and issued an unconditional
`Replace`.

### ⭐ The rule

| gesture | selection afterwards |
|---|---|
| right-press **in** the selection | **unchanged** — the group survives |
| right-press **not** in the selection | **Replace** — it deselects the others, as a left-click would |
| right-press on **empty space** | **Clear** |
| **left**-press | **Replace, unconditionally — UNCHANGED** |

⛔⛔ **That last row is why the button had to be carried.** A left-click on a member of a five-selection
must still narrow it to one; a guard applied to both buttons would have removed that silently, and §2.3
exempts the right-click only. `LeftClickingAnEntityInsideTheSelection_StillNarrowsTheSelectionToIt`
pins it.

### ⭐⭐ No new field, and wire-compatible by construction

The callback **already had** an `int actionId` that `Started` passed `0` into, and the `RawInput` path
in the same file already puts a `MapMouseButton` there; `GizmoInteractionBatch.ActionId` does the same
on the wire. ⭐ `MapMouseButton.Left == 0` and `ActionId` defaults to `0` ⇒ every un-migrated producer
and sender decodes as **Left**, which is what its `Started` always meant.

### ⚠ Two things measured while building, neither cosmetic

- **The modifier trap.** `MapMouseButton` is `[Flags]` with Shift/Ctrl/Alt in bits 28-30, so a plain
  `Button == Right` is **false for a shift-right-click**. Masked, and railed.
- **The exclusive-capture guard.** The new empty-space arm is skipped while a tool holds exclusive
  capture, mirroring the suppression the menu path already does — otherwise a right-click away from an
  armed tool would deselect the entity being edited, which 🔒 **§2.6 forbids**.

### ⛔ Limits, both stated in the design

**Empty-space clear is local-only** — a canvas anchor does not survive the ingress resolve filter; that
is **pre-existing** (no canvas interaction has ever crossed the wire) and relaxing the filter is
`R-144`'s business. **The map menu's CONTENTS** stay out of scope — `canvas-context-menu-design.md`
parks multi-entity menus; `S-4b` guarantees only the precondition they need.

## ⭐⭐⭐ SESSION `2026-09-20` (g) — **`S-4`: RIGHT-CLICK SELECTS ON THE PANELS; THE DER SEAM**

> 🔒 **User:** *"go ahead with S-4"*.

🟡 **Built, and deliberately partial.** 📄 As-built:
[`UX_Feature_Selection.md` §2.7.13](../UX/UX_Feature_Selection.md).

### ⭐⭐⭐ The finding: **the gesture decides the menu's subject**

§2.3 required the selection mutation to land *before* the menu is populated, **in the same frame**.
⛔ Since `S-3e` the write is a **request** served next frame, so that ordering is **unsatisfiable by
sequencing** — not merely unmet. ⭐ It does not need to be: *which row of §2.3 a right-click is* is
knowable **at the click**, so `_contextMenuUsesSelection` is fixed when the menu opens and
`ContextMenuSubjectCount` never re-reads the store. ⇒ the *"wrong exactly once"* menu is **dissolved**.
⚠ §2.3's ordering paragraph now carries a SUPERSEDED banner pointing here.

### ☑ What landed

| | |
|---|---|
| `EntityInspectorPanel` | right-click on an **unselected** entity ⇒ `Replace` request *(`"Inspector.RightClick"`)* + a ONE-entity menu; on a **selected** one ⇒ nothing moves, MULTI menu. Unbound hosts mutate locally, which is still correct (§2.7.8) |
| `DerEntityInspectorPanel` | `RequestSelectEntity` + `HostSelectedNetworkId`. ⭐⭐ **The obstacle was the answer:** `IDerEntity.EntityId` **is** the network id `SelectEntityCommand` carries ⇒ **no new event**. Both-or-neither, so half-wiring is visible |
| `ExConSubsystem` / `IExConLogic` | wires the DER panel to `SendSetSelection` / `SelectedEntityId` (promoted onto the interface). 🔒 **Behaviour change, intended:** a DER row click now moves the **remote map's** selection — ruling ①. ⚠ It does **NOT** discharge [`CE-259t`](Blueprint_Issues_Tracker.md) *(ExCon's shared ORBAT adapter selects locally and never tells the cluster)* — different surface, untouched — ⭐ it follows the precedent that row names as correct (`SendSetSelection`, not `SelectEntity`) |
| rails | `TheInspectorPanelsRightClickSelectsTests` *(new, 8, in `Fdp.Presentation.Tests`)* — **red-proved: 3 of 8 redden** on the inverse edit. `RightClick`/`RequestSelect` were extracted from the draw so the semantics are railable headlessly |

### ⛔⛔ NOT BUILT — **the map's right-click, and it is a dependency change**

📐 Measured: `GizmoInteractionStartedEvent` carries **`Token` + `WorldPos` only — no mouse button**, and
`GizmoInteractionEventKind` has **no *"menu opened"*** kind *(`MenuAction` fires only when an item is
chosen)*. ⇒ `SelectionInteractionSystem` cannot tell a right-press from a left one and issues an
unconditional `Replace`, so **right-clicking one of five selected entities on the map still collapses
the selection** — §2.3 row 1, and the `2026-09-10` fan-out ruling depends on it.
⭐ **Two shapes, neither an `Fdp.Presentation` edit:** a `Button` field on the vendored terminal's
interaction record *(smallest; every downstream consumer gains the distinction)*, or a `MenuOpened`
kind *(narrower; adds a wire event for one consumer)*. **My lean is the `Button` field** — the
information is already in the terminal's hand and the second option encodes one consumer's need into
the protocol. ⚠ It needs a `GizmoMap` change, so it wants a nod before it is opened.

## ⭐⭐⭐ SESSION `2026-09-20` (f) — **`S-3e`: CGF'S MAP INPUT, AND A REGRESSION `S-3` SHIPPED**

> 🔒 **User:** *"Remaining half"*.

☑ **Done — and measuring it found something worse first.** 📄 As-built:
[`UX_Feature_Selection.md` §2.7.12](../UX/UX_Feature_Selection.md).

### 🔴 The regression `S-3` shipped, found by measuring CGF

📐 Every host used to hand-sync `IInspectorContext.SelectedEntity` off `OnSelectionChanged`. `S-3`
deleted those in favour of the notification — correctly, they fired for map clicks only. ⛔ **But
`SelectionInteractionSystem` wrote the component directly and published nothing**, so the replacement
never fired for a map click either. ⇒ the **details pane stopped following the map on four hosts**.

⚠⚠ **Why nothing caught it:** the entity-inspector PANEL projects from `ISelectionState` each draw, so
row highlighting kept working — ⭐ a partial symptom that reads as *"fine"*. And the gesture system's
rails asserted it wrote **two booleans**; nothing asserted anything downstream.

### ⭐ One change closed three things

**Every map gesture is now a `SelectionChangeRequest`** ⇒ the request system applies **and announces**.

| ☑ | |
|---|---|
| the regression | map clicks announce again, on every host |
| **`S-2` deviation ②** | `SelectionRequestSystem` is now **literally** the only writer. `S-2` deferred this for want of a request system on ReplayBrowser/SimHost — `S-3b` had already supplied it |
| **CGF's map-input path** | ⭐ **one line**, because `S-3c` made the pack build the gesture system for all five hosts |

📐 **CGF's shape was sharper than the filed text.** It was never missing a *selection* — it holds the
shared view and serves requests. The remote terminal's clicks were **already arriving** on its
interaction bus and **nothing consumed them**: a click selected nothing, silently. ⚠ The adapter it
needed was **IG-private**; it now lives beside the system.

### ⚠ Gates `2026-09-20` (f) — all six projects compiled first

| gate | result |
|---|---|
| `Hrot.Presentation.Tests` | ✅ **275/275** *(+2 new rails; the regression rail is **red-proved**)* |
| `Hrot.ReplayBrowser.Tests` | ✅ **30/30** |
| `Hrot.Editor.Tests` | ⚠ 418/420 — the known ALC flake |
| `Hrot.IG.Tests` · `Hrot.SimHost.Tests` · `Fdp.Presentation.Tests` | ⚠ same **7** / **3** / **8** pre-existing |
| `design-digest` · `rulings-check` **37/37** · `tracker-counts` · 6 mermaid | ✅ |

⭐ **`T-1` worked as designed this time:** 3 of the 8 `SelectionInteractionSystemTests` reddened on the
deferral and were **folded, not routed around** — they now pump the request system and so prove the
whole chain instead of two booleans.

### ⭐⭐ NEXT — **`S-4`**

Right-click selects on **every** surface *(§2.3 incl. row 1)*; the **DER inspector gains a seam**.

---

## ⛔⛔ STILL UNVERIFIED ON THIS LANE — **`S-3d` (Stride) — VERIFIED ON WINDOWS `2026-09-20`, see below**

⚠⚠ **Written, pushed, NOT COMPILED.** `HrotStrideApp.Game` is `net8.0-windows` and outside the root
solution — this lane can build neither it nor `HrotStrideApp.Game.Tests`. 📄 §2.7.11 of
[`UX_Feature_Selection.md`](../UX/UX_Feature_Selection.md) carries the reasoning and the six checks.

**What changed:** the Stride 3-D `EditorSelectionState` became a **view** of the 2-D selection
*(`BindTo` over the editor's existing `Selected2DEntity` / `SetSelection2D` / `Selection2DVersion`)*,
and **`SyncSelection2D3D` is deleted** with both anti-bounce trackers. ⚠ Deleting it is mandatory: with
one truth, each "push" bumps a version the other arm reads as a change ⇒ a bump every frame, forever.

🔴 **One defect was caught by audit before pushing:** the Stride rails drive the subsystem **headless**,
where the editor never builds its selection — binding unconditionally would have made `Select` a
**silent no-op** with every rail still green. ⇒ `BindTo` takes a fourth `available` delegate, backed by
the new (Linux-built, tested) `EditorSubsystem.Has2DSelection`.

### 🪟 PROMPT FOR THE WINDOWS SESSION

```
Branch `ui` at origin. Pull it.

UXI-11 S-3d was written on a Linux lane that cannot compile Stride. Verify it.
Read docs/UX/UX_Feature_Selection.md §2.7.11 first — it lists what to check and why.

1. Build Stride/HrotStrideApp.sln (or HrotStrideApp.Game.csproj). It has NEVER been
   compiled with these edits. Fix compile errors in place; the touched files are
   Stride/HrotStrideApp.Game/StrideInspectorWindow.cs (EditorSelectionState gained
   BindTo/IsBound and a bound mode) and EditorStrideSubsystem.cs (binds after
   `_editor = new EditorSubsystem()`, and SyncSelection2D3D + its two version
   trackers are deleted).

2. Run HrotStrideApp.Game.Tests, especially EditorSelectionStateTests (12 tests).
   They drive the UNBOUND path. If any fail, `available` is returning true when it
   should not — check EditorSubsystem.Has2DSelection.

3. Run the editor with the Stride 3-D view. Confirm, in this order:
   a. click an entity in the 3-D view -> the 2-D map ring moves, same frame;
   b. click an entity on the 2-D map -> the 3-D highlight follows;
   c. NO per-frame churn: no selection flicker, and [SelDiag] must not log a
      change every second with no input. That churn is the failure mode the
      deleted bridge would have caused, so it is the thing most worth watching.

4. Report back: did it compile, did the 12 rails pass, and did 3a-3c behave.
   If 3c churns, say so immediately — that means something still writes the
   selection on both sides.

Do not "fix" a failure by restoring SyncSelection2D3D; with one selection there is
nothing to sync, and restoring it recreates the churn. Fix the binding instead.
```

---

## ⭐⭐⭐ SESSION `2026-09-20` (e) — **`S-3c`: THE BOOTSTRAP IS SHARED; NEXT IS `S-4`**

> 🔒 **User, `2026-09-20`:** *"we want to unify across host also the bootstrap code as far as possible,
> including this entity selection stuff, pls check if unifieable and unify if possible."*

☑ **It was unifiable, and the seam already existed.** 📄 As-built:
[`UX_Feature_Selection.md` §2.7.10](../UX/UX_Feature_Selection.md).

⭐ **`MapInteractionPack.Build` is called by all five hosts** and was written for exactly this disease
*("five hosts built the same buffer, the same two registries … by hand, in five composition roots")*.
🔴 Its header listed **`"selection systems"` among what is deliberately NOT here** — written when
selection *was* host-shaped. `S-1`…`S-3b` made the wiring identical, so that exclusion had become the
duplication. **Amended in the pack's own header, with the reason.**

| now built once, in the pack | was |
|---|---|
| `EcsSelectionState` · `SelectionInteractionSystem` | **5** composition roots each |
| `SelectionRequestSystem` + `SelectionNotificationSystem` | **3** places — and `ScenarioEditorModule` built its own, so the editor and CGF had **different instances** from the rest |
| the `SelectedEntitiesOnly` predicate | the same 4 lines hand-written in **3** hosts |

🔒 **The `2026-08-28` ruling is untouched — the pack CONSTRUCTS, the host SCHEDULES.** Scheduling stays
five-way *(module · kernel · kernel · kernel · direct tick)*, and that fan-out is a property of the
hosts: ReplayBrowser alone has no kernel.

⛔ **Checked and NOT unified, deliberately:** `IsSelectedPredicate`. IG and CGF pass `null`, and `null`
is a **documented policy** — *"an IG draws handles on everything, an editor draws them only on the
selection"* — so defaulting it would silently change what IG draws. ⭐ Only the copy-paste was removed.

### ⛔⛔ TWO PROCESS MISSES IN THIS SLICE — **both nearly shipped**

| | |
|---|---|
| 🔴 **a stale-binary false green** | `Hrot.Editor.Tests` **failed to compile** *(the module's parameter changed)* and still reported **419 passed** — from the previous binary. ⇒ ⭐ **every test project's build is now checked, and its error count printed, BEFORE any result is read** |
| 🔴 **a rail that encoded the WIRING, not the INVARIANT** | the `S-3b` rail asserted *"every host that runs the interaction system also holds the shared view"* — true for the world where each host wired its own, and **false on the very commit that unified them**. ⇒ 🔒 replaced by *"nobody wires their own"*, which survives the refactor. **Red-proved** |

### ⚠ Gates as measured `2026-09-20` (e) — **all six projects compiled first**

| gate | result |
|---|---|
| `Hrot.Presentation.Tests` | ✅ **273/273** |
| `Hrot.Editor.Tests` | ✅ **419/420**, 1 skip, 0 fail |
| `Hrot.ReplayBrowser.Tests` | ✅ **30/30** |
| `Hrot.SimHost.Tests` | ⚠ **1001/1007** — the same 3 pre-existing |
| `Hrot.IG.Tests` · `Fdp.Presentation.Tests` | ⚠ same **7** and **8** pre-existing |
| `design-digest --check` · `rulings-check` **37/37** · `tracker-counts` · 6 mermaid | ✅ |

---

## ⛔ HISTORY — SESSION `2026-09-20` (d) — **`S-3b`**

> 🔒 **User ruling, `2026-09-20`:** *"we shoulf unify, simhost is not special in how it should handle the
> UI; lets make the nodes use same (best shared) stuff in the same way."*

☑ **`S-3b` shipped.** 📄 As-built: [`UX_Feature_Selection.md` §2.7.9](../UX/UX_Feature_Selection.md).

| host | what changed |
|---|---|
| **SimHost** | 🔴 had **three** stores — the component, a `SimHostSelectionManager` `HashSet` behind a `SimHostInspectorAdapter`, and `_fdpInspectorState` — bridged by a callback that fired **for map clicks only**. ⇒ now `EcsSelectionState` + the shared pair; ⛔ **both types DELETED** |
| **ReplayBrowser** | 🔴 same split *(it runs `SelectionInteractionSystem` over a real repository)*, plus history navigation writing the inspector behind the map's back. ⇒ same view, same pair; history **publishes a request** |

🔴 **`S-3` had justified leaving ReplayBrowser out with a claim that was false** — *"it inspects a
recording, there is no global selection for it to agree with."* 📐 It holds a real `EntityRepository`
and runs the interaction system. ⭐ The tell was one grep, and it is **now a rail**
*(`EveryHostThatRunsTheInteractionSystemAlsoHoldsTheSharedView`, with anti-vacuity naming the four
hosts)* rather than something to remember.

☑ **The *"4 stores become 1 + views"* gate is MET** — `SimHostSelectionManager` was the last one, and
`Hrot.Editor.AiShared/Shell/IEntitySelectionSource.cs` had named its adapter *"the defect — a second,
parallel in-memory store"* in its own header. That note is now marked discharged.

⚠ **Behaviour change:** on SimHost and ReplayBrowser, selecting from the inspector list, the context
menu, or replay history now moves the **map ring** too. It did not before.

⚠⚠ **`T-1` miss, again:** I changed both hosts before running either host's own suite. ⛔ And the first
run said *"The argument …dll is invalid"*, which reads like a broken suite — 📐 it was the
**un-restored project** trap, identical at base. 🔒 `dotnet restore <tests.csproj>` costs 2 s and is
the first thing to try before calling a suite un-gateable.

### ⚠ Gates as measured `2026-09-20` (d)

| gate | result |
|---|---|
| `Hrot.Presentation.Tests` | ✅ **273/273** *(incl. the new unification rail)* |
| `Hrot.SimHost.Tests` | ⚠ **1001/1007** — the **3 reds are PRE-EXISTING**, identical at base. ⭐ Re-run after moving the pair onto the kernel: **same 3, no new throw**, and `SimHostVisualizationTests` calls `Initialize(repo, kernel, …)` with a real kernel |
| `Hrot.ReplayBrowser.Tests` | ✅ **30/30** |
| `Hrot.Editor.Tests` | ✅ **418/420**, 1 skip — the one red is the known ALC flake |
| `Hrot.IG.Tests` · `Fdp.Presentation.Tests` | ⚠ same **7** and **8** pre-existing reds |
| `design-digest --check` · `rulings-check` **37/37** · `tracker-counts` · 5 mermaid | ✅ |

### 🔴 TWO CORRECTIONS THE USER CAUGHT — **read these before trusting the block above**

| | |
|---|---|
| 🔴 *"SimHost runs no `ModuleHostKernel`"* | **FALSE.** `SimHostCapabilities.cs:67·79·100·116` register modules and global systems on `context.Kernel`; `StrideNodeBootstrapper.cs:199` drives `Context.Kernel.Update()`. ⇒ SimHost's pair is now **on the kernel**, like IG's. ⚠ The mechanism: ReplayBrowser's no-kernel fact **is** cited and true, and I generalised it onto the host named beside it in the same sentence. ⭐ Safe either way — `RegisterGlobalSystem` **throws** after `Initialize()`, so a wrong ordering dies loudly |
| 🔴 **the Stride host was never counted** | it **is** an ECS node *(composes SimHost + IG systems, `RegisterAll`, `Kernel.Update()`)* and holds its **own** `EditorSelectionState` — **two instances** — reaching the 2-D selection only by **version polling** in `SyncSelection2D3D`. ⇒ the count is **six** surfaces, not five |

### ⭐⭐ NEXT — **`S-4`**, and one open decision

`S-4`: right-click selects on **every** surface *(§2.3 incl. row 1)*; the **DER inspector gains a seam**.

⚠ **OPEN, awaiting the user:** unify the **Stride host** — replace `EditorSelectionState` with
`EcsSelectionState` + the shared pair, which **deletes `SyncSelection2D3D` outright**. 🔒 **Lean: do
it** *(largest remaining win, mechanical edit)* — ⛔ but `HrotStrideApp.Game` is `net8.0-windows` and
outside the root solution, so **this lane can build neither it nor its tests**. 📄 §2.7.9.

⚠ Separately open, and it is `UXI-11`'s other remaining half: **CGF runs no
`SelectionInteractionSystem`**, so it has no map-input path at all.

---

## ⛔ HISTORY — SESSION `2026-09-20` (c) — **`UXI-11` `S-3`**

☑ **`S-3` shipped** *(the notification + panels off their own selection)*. 📄 As-built, with four argued
deviations, in [`UX_Feature_Selection.md` §2.7.8](../UX/UX_Feature_Selection.md).

| what | |
|---|---|
| `SelectionChangedNotification` *(NEW)* | a plain managed FDP record carrying the **whole** selection + primary. 🔒 `R-134` met — ⛔ neither `SelectionChangedEvent` (`[DdsTopic]`) nor `SelectionChangedEventDto` is touched |
| `SelectionNotificationSystem` *(NEW)* | points `IInspectorContext` at the new primary. 🔴 It replaces hand-syncs that hung off `SelectionInteractionSystem.OnSelectionChanged` — i.e. **fired for a map click and nothing else**, so an inspector click, a context menu or `CMD_SET_SELECTION` never moved the inspector |
| `EntityInspectorPanel` | stops owning a selection: its set is a **projection** of `ISelectionState`, its clicks **publish requests** *(ctrl→Add/Remove, shift→Add, plain→Replace)* |
| ⛔ **`ChainToMap` RETIRED** | with its operator toggle. 🔒 Ruling ① — selection is global on every host. 📐 It defaulted to OFF and exactly **one** production host set it true, which is why an editor inspector click never reached the map |
| IG | its map→inspector change-detector **and** `_fdpLastMapSelection` are deleted |

🔴 **A layering finding: `S-2` had put the request event in the wrong assembly.** `Fdp.Presentation`
cannot reference `Hrot.Core`, so a request type in `Hrot.Common.Events` made §2.7.3 rule 2 *("every
surface is a requester")* **unbuildable for the ImGui panels** — half the surfaces. ⇒ both events now
live in **`Fdp.Toolkits`**, the one layer the panels and `Hrot.Core`'s registry can both see.

🔴 **The rails caught a real defect of mine:** `Apply`'s `Clear` branch returned **before** the
announcement ⇒ emptying the selection told nobody. ⚠ The build was green and every other rail passed.

⚠⚠ **Panels PROJECT, they do not subscribe** *(deviation ②)* — a bus event is readable for exactly one
frame and an ImGui panel that is collapsed, on a hidden tab, or simply not drawn would miss it and stay
stale forever. Re-reading the view each draw cannot miss. ⭐ The notification is for consumers that need
an **edge**; `S-5` is the next one.

⚠ **Behaviour change, stated not buried:** clearing the selection now clears the inspector on IG. Its
retired detector deliberately refused to; ruling ① removes the premise.

🟡 **Stores: 3 of 4.** ❌ `SimHostSelectionManager` remains — SimHost has a world **and** runs
`SelectionInteractionSystem` **and** keeps a separate manager for its panels. 🔒 **Lean: give SimHost an
`EcsSelectionState` and retire the manager** *(⛔ `DdsBackedSelectionState` is for hosts with NO world)*.
Not built: it retires a type with its own `IInspectorContext` wiring and this lane cannot run SimHost.

### ⭐⭐ NEXT — **`S-4`**

Right-click selects on **every** surface *(§2.3 incl. row 1 — an already-selected entity keeps the whole
selection)*; the **DER inspector gains a seam** *(it speaks DER ids, not `Entity`, which is why `S-3`
left it alone)*.

### ⚠ Gates as measured `2026-09-20` (c)

| gate | result |
|---|---|
| `Hrot.Presentation.Tests` | ✅ **272/272** |
| `Hrot.Editor.Tests` | ✅ **419/420**, 1 skip — the only red across two runs was the `AiHotReload` ALC flake |
| `Hrot.IG.Tests` | ⚠ **427/435** — the **7 reds are the same PRE-EXISTING translator set** |
| `Fdp.Presentation.Tests` | ⚠ **541/550** — the same **8 pre-existing** |
| `design-digest --check` · `rulings-check` **37/37** · `tracker-counts` · 5 mermaid blocks | ✅ |

⛔ **Roslyn note for the next session:** `roslyn_apply_rename` **fails on this server** (errors with no
detail, green tree, fresh token, twice). `roslyn_preview_rename` works — apply its diff and verify.
⚠ And `RoslynMcp.dll` has **no query CLI**: its subcommands are `setup`/`hook`/`list`/`verify`/`update`
only, so the MCP stdio server IS the only tool path (unlike codebase-memory, which has `cli <tool>`).

---

## ⛔ HISTORY — SESSION `2026-09-20` (b) — **`UXI-11` `S-2`**

☑ **`S-2` shipped** *(the request event + the only writer)*. 📄 The as-built, with five argued
deviations, is [`UX_Feature_Selection.md` §2.7.7](../UX/UX_Feature_Selection.md) — read **that** before
starting `S-3`.

| what | |
|---|---|
| `SelectionChangeRequest` *(NEW)* | a **managed** FDP event: `Entities` + `Mode` *(replace·add·remove·clear)*. ⛔ Managed because a blittable struct cannot carry a set, and a rubber band selects N in one request |
| `SelectionRequestSystem` | the **Roslyn-renamed** `SelectEntitySystem`; consumes the new request **and** `SelectEntityCommand` *(the network-id boundary, kept on purpose — rule 7)* |
| ⭐ **registered on IG** | 📐 measured: `ScenarioEditorModule` is registered by the editor and CGF **only** ⇒ on IG `SelectEntityCommand` had **no consumer at all**. Publishing a request from IG without this would have recreated that silent no-op exactly |
| the hand-rolled writers | ☑ **all four gone** — `new SelectionState {` is now in **zero** production files outside `EcsSelectionState`. 🔴 The design's delete-list named **three**; `EditorSubsystem.SetSelection2D` was the missed fourth |
| ☑ **`CE-259s` discharged** | the drain was registered **before** the select system ⇒ once the write was deferred, *"select then arm Rotate"* armed on the **previous** selection. `ScenarioEditorModule` now registers the request system first |

⚠⚠ **NOT literally *"the only writer"*, and that is reported rather than claimed.**
`SelectionInteractionSystem` still writes through the shared view *(deviation ②: converting it needs the
request system on **ReplayBrowser and SimHost** and a rework of `OnSelectionChanged`'s immediate
callback — `S-4` touches that path anyway)*, and two editor facade seams stay synchronous
*(deviation ③: `SetSelection2D`'s only caller reads `Selection2DVersion` back **in the same frame**, and
`HrotStrideApp.Game` is `net8.0-windows` — unbuildable on this lane)*.

⭐⭐ **The `T-1` payoff, and my miss:** `Hrot.IG.Tests/CommandHandling/SetSelectionCommandTests` —
**IG's own selection rail** — **caught the deferral**. ⛔ I had not opened it on the first sweep even
though my own file search listed it. Fixed by pumping one kernel frame, which makes it stronger: it now
proves request → bus → *the system being registered on IG at all* → view.

⚠ **A finding filed, not fixed** — `FdpConfig.EnforceExplicitEventRegistration` is a **process-global**
flipped by one rail while other xUnit collections run in parallel; whichever class publishes a managed
event in that window throws. Documented `2026-09-09`; `S-2`'s added tests changed the scheduling and it
surfaced on two more classes. ⭐ Lean: `DisableTestParallelization` on `Hrot.Editor.Tests`, or an
`AsyncLocal` override on `FdpConfig`. ⛔ Suite-wide policy — not `S-2`'s call.
*(⚠ And `FdpEventBus.HasEvent`/`HasManagedEvent` are **not** an is-registered check — they answer
*"was one published this frame"*. I nearly rewrote the rail on that misreading.)*

### ⭐⭐ NEXT — **`S-3`**

The **notification** event *(new, FDP-internal — 🔒 `R-134`: no DDS type in the internal path)*; panels
subscribe and their own selection fields become view state. ⭐ That is also what collapses the last two
stores: `SimHostSelectionManager` and `SharedEntitySelection`.

### ⚠ Gates as measured `2026-09-20` (b)

| gate | result |
|---|---|
| `Hrot.Presentation.Tests` | ✅ **272/272** |
| `Hrot.Editor.Tests` | ✅ **414/415**, 1 skip, 0 fail *(the `AiHotReload` ALC flake did not fire this run)* |
| `Hrot.IG.Tests` | ⚠ **427/435** — the **7 reds are PRE-EXISTING**, confirmed by stashing and re-running at base: identical set *(`EntityDamage` · `EntityInfo` · `EntityMaster` translators)*. ⭐ My two reds — IG's selection rail — are **fixed**, not excused |
| `Fdp.Presentation.Tests` | ⚠ **541/550** — the same **8 pre-existing** ImGui-context reds |
| `design-digest --check` · `rulings-check` **37/37** · `tracker-counts` · 4 mermaid blocks | ✅ |
| ⛔ `HrotStrideApp.Game` | still `net8.0-windows`, unbuildable here. No Stride file was edited |

---

## ⛔ HISTORY — SESSION `2026-09-20` (a) — **`UXI-11` SLICE `S-1`**

☑ **`S-1` shipped** *(selection unification, slice 1)*. 📄 The as-built is
[`UX_Feature_Selection.md` §2.7.6](../UX/UX_Feature_Selection.md) — read **that**, not this summary,
before starting `S-2`.

| what | |
|---|---|
| `ISelectionState` | gained `Add` · `Remove` · `SetMultiple` · `Clear` · **`Version`** |
| `EcsSelectionState` *(NEW)* | `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Selection/` — a read-through view over the `SelectionState` **component**. The editor and CGF hold it instead of a parallel `HashSet` |
| `SelectionInteractionSystem` | **delegates** its component writes to the same view ⇒ one implementation of *"what selected looks like"* |
| `DefaultSelectionState` | **kept but demoted** to world-less hosts + tests; a source-scan rail fails if any host under `Hrot/`/`Stride/` constructs one |

⚠⚠ **THE GATE WAS NOT FULLY MET, AND THAT IS REPORTED, NOT HIDDEN.** `S-1`'s gate is *"the 4 stores
become 1 + views"*; **2 of 4** collapsed. `SimHostSelectionManager` and `SharedEntitySelection`
*(`Hrot.Editor.AiShared`)* stay separate until the **notification event** exists ⇒ 🔒 **the gate really
lands at `S-3`.** ⛔ Do not read `S-1` as having met it.

⭐ **Free win worth knowing:** `Version` is derived from the observed ECS truth, so a **map click now
reaches the Stride 3-D view** through `EditorStrideSubsystem.SyncSelection2D3D` — it could not before,
because the hash set only bumped when editor code assigned through it.

⚠ **Four deviations from the design, all argued in §2.7.6** — `Version` added to the interface ·
`DefaultSelectionState` kept · **writes go direct, not through a command buffer** *(§2.5 carries the
as-built note; `S-2` retires the question)* · `SelectionInteractionSystem` delegates.

### ⭐⭐ NEXT — **`S-2`**

The request event gains a **set + mode** *(replace · add · remove · clear)*; `SelectionRequestSystem`
becomes the **only** writer; the **3 hand-rolled `SetSelected`** are deleted
*(`SelectionInteractionSystem` · `EditorSubsystem` · `IgApplication.SelectEntityOnMap:1596-1614`)*.
⭐ `S-2` is also what makes §2.5's command-buffer question disappear.

### ⚠ Gates as measured `2026-09-20`

| gate | result |
|---|---|
| `Hrot.Presentation.Tests` *(the feature's own suite + the new rails)* | ✅ **271/271** |
| `Hrot.Editor.Tests` | ⚠ **409/410**, and the one red is a **flake**: `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` — an ALC-collection/GC timing test, **green 3/3 in isolation**, green on one full run and red on the next **with the same binary**. ⛔ Unrelated to selection |
| `Fdp.Presentation.Tests` | ⚠ **541/550** — the **8 reds are PRE-EXISTING**, confirmed by stashing the change and re-running: identical 8 at base *(ImGui-context panel/perspective tests)* |
| `design-digest --check` · `rulings-check` **37/37** · `tracker-counts` · 3 mermaid blocks | ✅ |
| ⛔ `HrotStrideApp.Windows` / `HrotStrideApp.Game` | **not buildable on this Linux host** *(`net8.0-windows`)*. ⭐ No Stride file was edited and `Selection2DVersion` kept its type, so it is source-compatible — ⚠ **reasoned, not measured** |

---

## ⛔ HISTORY — SESSION `2026-09-19/20` — **UX DOCS RE-VERIFIED; the plan that produced the above**

**Branch `ui`** *(the stable role-named lane branch — `R-148`, `2026-09-19`)*, **HEAD `79e9a449d`**, tree
clean, nothing unpushed. Gates green: `design-digest --check` · `rulings-check` **37/37** ·
`tracker-counts` · **13 mermaid blocks** across the three touched diagram files.

⚠ **Branch note, checked so the next session does not re-check it:** `ui` **contains** the old UI-lane head
`4b0cb5285` and everything on `origin/coordinator` *(0 behind both, 110 ahead)*. ⛔ The older `claude/…`
lane names in this file are historical — the lanes are now `coordinator` · `ui` · `backend` · `behaviors`.

### ⭐⭐⭐ THE PLAN — **start `UXI-11` (selection unification), slice `S-1`**

📄 **The owning design is [`docs/UX/UX_Feature_Selection.md`](../UX/UX_Feature_Selection.md) and it is NOT
stale** — re-verified `2026-09-10` with graph + grep + coverage. ⭐ **Read its §2.7** *(consolidated TARGET
STATE: the class diagram, the request/notify sequence, the delete-list)* and **§2.7.5** *(the slice order)*.
⛔ §2.1 is its older sketch; §2.7 supersedes it where they differ.

| slice | what | gate |
|---|---|---|
| ⭐⭐⭐ **`S-1` — START HERE** | `ISelectionState` gains `Add`/`Remove`/`SetMultiple`/`Clear`; **`EcsSelectionState` replaces `DefaultSelectionState`** | the 4 stores become 1 + views |
| `S-2` | the request event gains a **set + mode** *(replace·add·remove·clear)*; `SelectionRequestSystem` becomes the ONLY writer | the 3 hand-rolled writers are deleted |
| `S-3` | the **notification** event *(new, FDP-internal)*; panels subscribe | ⛔ `R-134` — no DDS type in the internal path |
| `S-4` | right-click selects on every surface; DER inspector gains a seam | resolves `CE-259s`'s ordering |
| `S-5` | losing selection cancels that entity's edit | needs `S-4` |
| `S-6` | the remote-map dispatcher becomes a requester | 📄 `DESIGN_Remote_Map_Control.md` |

⭐ **`UXI-24` (multi-select) rides on `S-1`+`S-2`** — its additive map click needs the mutators and the mode.

#### 📐 What `2026-09-19` MEASURED about `UXI-11` — **it is SMALLER than the issue text says**

| claim | measured on the GRAPH |
|---|---|
| ⛔ *"CGF has no selection at all"* — the filed headline | 🔴 **FALSE.** CGF holds **two** stores: `_selectionState` **and** `_sharedEntitySelection` *(the shared `Hrot.Editor.AiShared` one)*. ⇒ CGF has **adopted the shared store already** |
| what CGF actually lacks | ⭐ **a SYSTEM writing them from map input.** `SelectionInteractionSystem` is constructed in **4** hosts — IG · ReplayBrowser · SimHost · Editor — **not** CGF |
| the size of the seam | ⭐⭐ **2 interfaces**: `ISelectionState` *(**2** production impls — `DefaultSelectionState`, `SimHostInspectorAdapter`; +1 Examples, +1 test fake)* and `IEntitySelectionSource` |
| ⛔⛔ **THE TRAP** | `.*Selection.*` matches **67 classes**. ⛔ **~60 are BTree/HSM/Blueprint NODE selections — a DIFFERENT CONCEPT.** Do not fold them in |

⇒ 🔒 **Re-word the `UXI-11` framing before building:** the CGF half is *"the input path is missing"*, **not**
*"selection is missing"*.

### ✅ What shipped this session — **docs only, no code**

📄 Commit **`79e9a449d`** — *"docs(UX): re-verify the interaction backlog against code, with the graph"*.

| doc | what was wrong, and is now right |
|---|---|
| [`UX/PLAN_Interaction_UX_Backlog.md`](../UX/PLAN_Interaction_UX_Backlog.md) | its *"verified ledger"* was dated `2026-08-28` and **wrong on 5 of 19**. New counts **6 DONE · 8 PARTIAL · 6 NOT-BUILT**; new **§2.1** names the five that moved; dependency graph marks discharged nodes; phase table carries measured state; the old ledger is in a `⛔ HISTORY` section |
| [`UX/UX_Issues.md`](../UX/UX_Issues.md) | `UXI-10` and `UXI-11` claimed **☑ done**; `UXI-07` claimed **✅ designed**. All three corrected with measured notes. ⭐ The `🟡` partially-built marker was **already in use on `UXI-35` and missing from the legend** — now documented, not invented |
| [`DESIGN_Map_Rendering_And_Interaction.md`](../DESIGN_Map_Rendering_And_Interaction.md) | the two rot rows it recorded **against itself** on `2026-09-10`, unrepaired since: §3.2's sequence diagram, §4.2's *"NOT-BUILT"* table. ⭐ Plus a **third** instance the rot list had missed — §1.3's *"backend stack does not exist"*. `known-rot` now reads **none open** |
| `SNAPSHOT_Map_Interaction_Architecture.md` · `UX/UX_Feature_Selection.md` | both said `ISelectionState` has **3 production impls**; measured by `IMPLEMENTS` edges it is **2** *(the third was the Examples adapter)* |

### ⭐⭐ THE FIVE VERDICTS THAT MOVED — **do not re-derive these**

| item | verdict `2026-09-19` |
|---|---|
| **`UXI-23`** map parity | ✅ **DONE** — `MapInteractionPack.Build` called by **all five** hosts |
| **`UXI-09`** map viewport | ✅ **DONE** — `new MapCamera(` down to **2** production sites from the filed 5 |
| **`UXI-07`** tool model | 🟡 **steps 1–4b BUILT**, operator-confirmed. ⛔ **Open: steps 5–6** — `ShowOnToolbar` has **6 writers, 0 readers**; nothing binds `ActiveModalChanged` *(`CE-259m`)* |
| **`UXI-10`** symbology | 🟡 **PARTIAL.** `ResolvedStyle` has **8 production consumers and NOT ONE is a gizmo or map layer**; none of the **8** presentation gizmos reads it; `EntityPresentationGizmoShared.cs:236` paints **every** entity `Rgba32(100,220,255)`. 🔴 *"friend and hostile indistinguishable"* **still holds** |
| **`UXI-11`** selection | 🟡 **PARTIAL, smaller than filed** — see the measurement table above |

### ⛔⛔ THE PROCESS LESSON — **the reason the ledger drifted, and it will drift again without this**

🔴 **The `2026-08-28` scan was grep-shaped**, and **four of the five wrong rows were set- or absence-shaped
claims** — *"who builds the pack"* · *"how many camera copies"* · *"who reads `ResolvedStyle`"* · *"does CGF
have selection"*. ⛔ **grep can confirm a guess; it cannot enumerate a set or prove an absence.**

⚠⚠ **And I repeated the mistake on `2026-09-19` before the user caught it** *(user: "pls use codebase memory
as claude.md is saying, not just grep")*. ⭐ **Redoing it on the graph changed one verdict outright** *(the
CGF selection claim above)* **and properly founded two absence claims.** ⇒ 🔒 **for `UXI-11`, whose whole
subject is "how many stores and who writes them", `search_graph` / `query_graph` over `IMPLEMENTS` and
`USAGE` is the tool — not grep.**

⭐ **Working recipe when the MCP flaps** *(it did, repeatedly)* — the CLI is the same binary:
```bash
/opt/codebase-memory-mcp/codebase-memory-mcp cli search_graph \
  '{"project":"home-user-HROT","name_pattern":".*Foo.*","label":"Class","detail":"ids","limit":60}'
/opt/codebase-memory-mcp/codebase-memory-mcp cli query_graph \
  '{"project":"home-user-HROT","query":"MATCH (c)-[:IMPLEMENTS]->(i) WHERE i.name = \"IFoo\" RETURN c.qualified_name, c.file LIMIT 40"}'
```
⛔ **`check_index_coverage` is NOT in the CLI** — it needs the MCP. Coverage on `2026-09-19` was
`index_mode: full`, `recording_status: complete`, no parse gaps in `Hrot.CGF` · `ScenarioEditor` · `Vis2D`.

### ⚠ OPEN ITEMS parked, with leans already recorded — **not part of `UXI-11`**

| id | what | lean |
|---|---|---|
| `CE-259r` | the picker's hover hit-tests an **empty** frame *(reproduced headlessly)* | reorder the group |
| `CE-259s` | `ToolActivationDrainSystem` registered **before** `SelectEntitySystem` ⇒ menu arms on the pre-menu selection | ⭐ **resolves with `S-4`** |
| `CE-259t` | the shared orbat seam sets ExCon's selection locally only | one line |
| `CE-259m` | `UXI-07` steps 5–6 — the toolbar shows no state | — |
| `CE-259l` | should `InputCaptureBinding`'s `wantsRawInput` bit exist at all? | 📄 `Tool_Model` §4.7f |
| — | **design "A"** — no document owns *"the interaction surface"* as a thing | 📄 `SNAPSHOT…` §5.2; wants `S-1`'s store settled first |

### ⭐ Canon that changed under this lane on `2026-09-19` — **read before allocating an id**

| | |
|---|---|
| ⛔⛔ **`3a-id`: ids are PLAIN INCREMENTING NUMBERS — no letter suffixes** | 📐 63 rows were spelled `CE-259a…CE-259bm`. ⭐ **Allocate the next free plain number**; ⛔ never renumber existing ids *(they are cited from designs and commits)*; grouping goes in the row's PROSE |
| ⭐ **`R-148`: lanes run on stable role-named branches**, all derived from `coordinator` | ancestry still verifies, names do not |
| ⭐ **EXPLORE WIDE, DECIDE SHORT** · **RUN SLOW COMMANDS IN THE BACKGROUND** · **DIAGRAM FIRST** · module-relationship diagram is now obligation ①a · `related-designs` is mandatory in a STATUS block | all new in `CLAUDE.md` |

---


## ⭐⭐⭐ SESSION 2026-09-15 — DISTRIBUTED PERSISTENCE + OWNERSHIP: BUILT & LIVE-PROVEN

**Branch** `claude/reset-working-branch-qd1qpv`, **HEAD `4b0cb5285`**, tree clean. Gates green:
`design-digest --check`, `rulings-check` 34/34, `tracker-counts`, mermaid. ⚠ This session took the **MCP
lane's role** too (user: "MCP lane not active now, you take its role") — the ai-debug HTTP surface work is ours.

⚠ **Environment note:** the `hrot-ai-debug` MCP and (intermittently) `codebase-memory-mcp` were DOWN. All
live cluster testing was done over **raw HTTP** per `docs/RUNBOOK_Cluster_Debugging_Over_Http.md`. ⛔ RUNBOOK
trap that bit repeatedly: **do NOT combine `pkill` + `setsid nohup` launch + a foreground wait-loop in ONE
Bash call** — the tool-exit tears down the detached process (exit 144). Run cleanup, launch, and the HTTP-up
wait as **separate** Bash calls. `--mode all` node ids seen live: CGF=400 (Brain/Scenario), SimHost=1
(Muscle), IG=100, ExCon=200.

### What shipped this session (all committed + pushed)

| id | what | commits |
|---|---|---|
| **CE-280** | Load-side foreign round-trip (T-C ExCon): `PrefetchScenarioAsync` routes `foreign/node_<id>.json` to its origin node; `ExConScenarioLoadHandler` restores observer state. Live-proven (T-C/T-D over HTTP, tampered-file value proof). | `66b24d79b` |
| **CE-281** | Retire `NetworkOwnership`, merge into `NetworkAuthority` (byte-identical dup). Struct deleted (id 140 reserved), 2 readers repointed, 17 test files fixed. Full solution + affected suites green. | `7838c20c4` |
| **CE-276** | Entity ownership **transfer**, descriptor-level & NED-initiated (ruling: descriptors are a NED concept, use `EDescriptorType`, NOT ECS components). `OwnershipTransferInitiationSystem` (NED) + `TransferEntityOwnershipRequest`/`TransferScope`; ai-debug `GET/POST /entities/{id}/ownership[/transfer]` (503 off-NED); wired CGF/SimHost/IG. **Receive side unified** — `OwnershipIngressSystem` now role-independent so a MUSCLE can receive (was Brain/IG-only). Rails `OwnershipTransferInitiationTests` 4/4; live CGF→IG **and** CGF→SimHost(Muscle) transfers proven. | `95909583e`→`4b0cb5285` |

**Designs (authoritative, all folded to as-built):**
- `docs/DESIGN_Entity_Ownership_Transfer.md` (CE-276) — build-state BUILT; class+sequence+module+HTTP UML; §5a HTTP surface; the receive-side unification note.
- `docs/DESIGN_Distributed_Scenario_Persistence.md` — §5a (CE-280 as-built), §6c (receive side, now unified), §7 (CE-281 as-built). §6c's "replicate NetworkAuthority" idea is SUPERSEDED (PrimaryOwnerId is derived from the EntityMaster descriptor on both sides — do NOT replicate NetworkAuthority).
- Tracker rows CE-276/278/279/280/281 in `Blueprint_Issues_Tracker.md`.

**Live product check:** `hill-attack-close` on `--mode all` — both Hostile targets destroyed (HP 50→0 by sim~45s) AND platoon returned to the authored baseline (tanks settled x≈523–531 = baseline local 523–532, via `/world/geo-to-local`; firing line 580–582). Combat+mission loop intact after all ownership changes. `SplitAuthoritySpawnTests` 3/3.

### Deferred (not started) — the only open follow-ons
1. **Whole-entity-to-external multi-node orchestration** (CE-276 follow-on): consolidate an entity whose descriptors are split across Brain+Muscle onto ONE external all-roles node — each current owner transfers its share; needs ordering (EntityMaster last on publish / first on unpublish). The per-node primitive is done; this is the coordinator on top.
2. **CE-278** — retire legacy `SaveScenario`=2 op (design `DESIGN_SaveScenario_Legacy_Op_Retirement.md`, do NOT re-derive; prerequisite: re-home the `Orchestrator.json` sidecar write onto the Export path first).
3. **CE-279 A2** — full mega-registrar move-down (low value, high blast radius).
4. Cosmetic: ai-debug `GET /entities/{id}/ownership` reports component **ids** for translator-registered descriptors (type names only for manual `RegisterMapping`) — fine, noted.

---


> 🔒🔒 **Branch: `claude/reset-working-branch-qd1qpv`** *(re-pointed by the USER, `2026-08-23`)*. ⛔ Push
> nowhere else. ⭐ **CURRENT quest ids: `CE-` (next free `CE-110`)**; ⚠ `BP-` are this lane's HISTORICAL
> variable-model ids, tracker areas **`A`–`G`**.
> ⚠⚠ **This lane MOVED from `claude/hrot-implementation-j1jvin`** — ⛔ any document still naming `j1jvin`
> as this lane is stale; `.claude/CLAUDE.md`'s lane table *(`6b14d13fe`)* is authoritative.
> ⚠ **A third lane now exists:** `claude/blueprint-macro-feature-sdmspn` is the **BACKEND** lane
> *(ids `ST-`, tracker area `I` only)* — ⛔ the name is a historical reuse, not this lane.
> ⭐ **The coordinator is pushing to `claude/blueprint-authoring-status-6sr5ld`** — ⚠ CLAUDE.md's table
> still says `…-gm0akp`; **rule 7 syncs from wherever the live handoff is**, confirmed by ancestry.
> ⭐ **RELEARN** before acting on this file if the session is fresh or just compacted.

---

# ⭐⭐⭐ §0 — THE CURRENT QUEST: **subsystem-composition unification (`AQ63`)**

> 📄 **READ FIRST, IN THIS ORDER:**
> **⓿** ⭐⭐⭐ [`../DESIGN_Subsystem_Composition_Unification.md`](../DESIGN_Subsystem_Composition_Unification.md)
> — **THE STANDING DESIGN: the approach, the constraints, the phase plan.** ⚠ Read it AFTER §0.0d — §0.0d says which of its sections are live *(§5c is phase 2)*.
> **①** [`Architect_Question_63_Unify_Subsystem_Composition.md`](Architect_Question_63_Unify_Subsystem_Composition.md)
> — ⭐⭐ **§9 and §10 are USER RULINGS (canon)**; ⭐⭐ **§12 is the phase-0 venue**; ⛔⛔ **§11 is SUPERSEDED — do not quote it.**
> **②** [`batches/HANDOFF_Cgf_Bootstrap_Unification.md`](batches/HANDOFF_Cgf_Bootstrap_Unification.md) — the dispatched FRAME. ⚠ **stale on two points**, see the STATUS block.
> **③** [`Architect_Question_62_Unify_The_Composition_Root.md`](Architect_Question_62_Unify_The_Composition_Root.md) — the predecessor; ⚠ AQ63 §3 supersedes its SHAPE and STAGING.
>
> 🔒 **Branch `claude/reset-working-branch-qd1qpv`** · dispatch sha **`fd8da0967`** · rule-1b started-marker pushed (`1c4325ac5`; phase 0's own at `830fd32c7`). ⭐ ids **`CE-`**, next free **`CE-110`**.
> ⭐ **RELEARN** before acting on this file.

## ✅✅✅ 0.0 — **PHASE 0 IS DONE** *(`2026-08-27`, head `9bff523c7`)*
📄 **[`batches/REPORT_Composition_Phase0.md`](batches/REPORT_Composition_Phase0.md)** · as-built folded into
the design's **§5.6 / §5.7 / §5.8**.

| ⭐ what a next session must know, and must NOT re-derive | |
|---|---|
| ⭐⭐⭐ **The rail found a REAL CRASH on its first real run** — `CE-065`. The `E3` slice routed *"center on entity"* onto a shared system but left its **event registration** in `EditorSubsystem`, and `ClusterRunner/Program.cs:52` turns strict mode on **process-wide** ⇒ the publish threw out of CGF's ImGui context menu and killed the process. ⭐ Fixed by putting the two events on `PresentationComponentRegistry`'s ONE list *(where `SelectEntityCommand` already was — which is exactly why the sibling menu item worked)* | §5.7 |
| ⛔⛔ **`--mode all` IS THE ONLY MODE WE RUN** 🔒 *(user, `2026-08-27`: "we never use '--mode cgf'")* — and **`--mode cgf` alone CANNOT BOOT anyway.** `DdsIdAllocator` waits 30 s for `Hrot.Orchestrator` then throws; **exit 134** before `/status`. ⇒ **exercise CGF via `--mode all` + the `Scenario` perspective.** ⚠ *"the `--mode cgf` symptoms"* is shorthand for *"CGF's symptoms"* | §5.8 |
| ⭐⭐ **`BP-487` is HALF done.** The map FEED is reachable *(`GizmoBuffer` on `ISubsystemDebugProvider`, resolved per ACTIVE perspective)*; ⛔ `PanelSnapshot.ClearCaptured()` still has one production caller ⇒ that half is `MX-011`, **MCP lane** | §5.6 |
| ⛔ **`/missions` is STILL unclassified in `CapabilityManifest.CapabilityFor`** — **third report**, MCP lane. It makes `The_manifest_describes_this_host_truthfully` **red before its matrix loop**, so nothing new can be asserted there. ⭐ The `panels.gizmo` claim was moved to `TheMapsAgreeOnBothHostsRails`; move it back **and delete the copy** when `/missions` lands | §5.8 |
| ⚪ **The "map shows no entities" symptom does NOT reproduce** on `hill-attack` in `--mode all` — 📐 the cluster submits **739** primitives incl. **16 `SpatialAnchor`s naming ids 1000–1007**. ⛔ **NOT fixed, NOT closed** *(the user said "on some scenarios")*; the rail stands to catch it | §5.8 |
| ⭐ **Item ① needed no code** — all 8 drift instances were already railed by the preceding batch. ⛔ Do not rebuild them as a T3 comparison | §5.8 |
| ⚠⚠ **This batch TOUCHED MCP-LANE FILES** *(`DebugApiService.cs`, `DebugApiService.Panels.cs`, `CapabilityManifest.cs`)* — unavoidable for `BP-487`, declared in report §7 ②, **flagged for the coordinator** | report §7 |

## ✅✅✅ 0.0b — **PHASE 1's SEAM IS BUILT** *(`2026-08-27`, head `f7df23904`)*
📄 design **§5b** *(inventory + UML)* and **§5b.4** *(as-built, THREE argued deviations)*.

| ⭐ what a next session must know | |
|---|---|
| ⭐⭐⭐ **The seam existed already.** There were TWO interfaces named `IWindowRegistrar`: host-level *(`RegisterWindows`, 8 subsystems)* and **feature-level in `Hrot.Blueprints.Editor`, in-degree 24** — the bundle contract, unnamed. ⭐ `BlueprintWindowRegistrar` implements BOTH and is the working precedent. ⇒ phase 1 NAMED the shape | §5b.1 |
| ⭐⭐ **`IShellCommandRegistrar`** is the feature seam's new name *(`CE-068`)*; the ENGINE one keeps `IWindowRegistrar` | `CE-068` |
| ⭐⭐ **`IUiBundle`/`UiBundleContext`/`UiBundleHost`** in `Fdp.Presentation`; **`ShellCommandCoreBundle`** is adopter #1 and **both hosts compose it** | `CE-069` |
| ⛔⛔ **`SharedAiWindowRegistrar` was WITHDRAWN as first adopter** — 📐 of its 7 windows CGF constructs **0**, the editor **3**. Adopting it is *newly constructing seven windows on CGF* ⇒ **a question about CGF's ROLE**, not composition. ⭐ Answer that before touching it | §5b.4 |
| ⛔ **`DeclaredSystems()`/`ReportUnserviceable()` were NOT built** — no adopter needed them, and an unadopted member looks adopted. ⭐ They arrive with the first bundle that has something to declare | §5b.4 |
| ⭐⭐⭐ **The constraint is STRUCTURAL now:** `A_bundle_cannot_reach_the_run_set` asserts by reflection that `UiBundleContext` exposes only windows/menu/toolbar. ⚠ **If it fails, that is a DESIGN question, not a test to update** | §3.2 |
| 🔴 **`CE-067`: `Hrot.Blueprints.Tests` (3 983 tests) had NOT COMPILED**, and `--no-build` printed PASSED over the stale binary — the exact hazard CLAUDE.md's tier section names. ⭐ Now **3 965/0** and back in the gate set | `CE-067` |
| 📐 **Dead guard:** `WindowManager.MainToolbar` is NEVER null ⇒ every `MainToolbar != null` check was always true and its "toolbar-less host" comments described an impossible state | §5b.4 |

## ⭐⭐⭐ 0.0e — **`--mode all` VISUAL-CHECK CORRECTIVES + the `cgf==editor` TKB ruling.** ⭐ **the live section; its entry point is §0.0e.3d.** *(`2026-08-28`, head `7fbcf54e4`)*

> ⚠ **This supersedes §0.0d as the start-here section.** §0.0d's phase-2 plan is **DONE** *(slices ①②③, `J1`,
> `J2`, `J3` all closed — see §0.0d for its own record)*. ⛔ Do not restart phase 2 from it.

### 0.0e.1 🔒🔒 THE USER RULINGS THAT NOW BIND THIS WORK — **verbatim, newest first**

| # | ruling |
|---|---|
| 🔒🔒🔒 **`CE-109`** | *"shouldn't the TKb templates and scenario loading handlers be shared? the editor one's is very likely newer and better and the one to follow. there should be nothinkg like cluster tKB and editor TKB; we need cgf==editor"* ⇒ ⭐⭐ **where the hosts differ, the EDITOR is canonical and the cluster adopts it** — ⛔ never the reverse, ⛔ never a CGF-private variant |
| 🔒🔒 **the safety fence** | *"the scenario loading path was tested manually pretty well in the editor so pls be carefull with any 'fixes'"* ⇒ ⛔⛔ **do NOT touch the editor's scenario path.** ⭐ The cluster moves toward it |
| 🔒 **cross-lane** | *"feel free to make changes to other lane's files. No other lane is running. no collision risks."* ⇒ ⭐ TIME-lane / backend-lane files are editable; ⚠ still say which lane a change lands in |
| 🔒 **`CE-090`** | *"we are unifying the UI, so obviously the stuff should look same and they CAN'T look different by design if they are rendered by single shared code where host-type gates are undesired; no special boolean needed"* |
| 🔒 **`CE-086`** | *"Unify the internal window ids to snake, breaking layout is not an issue."* |
| 🔒 **`CE-093`** | *"system not deployed yet… We can and should use better stuff (resolveBase)."* |

### 0.0e.2 ✅ WHAT IS DONE — **do not redo any of this**

⭐ Phase 2 closed: slices ①②③ · `J2` *(`CE-091`)* · `J3` *(concluded not-worth-building, §5c.11)* · `J1` +
`J1-a` *(`CE-093`…`CE-100`, §5c.12–§5c.15)*.

⭐ Then the user ran `--mode all` **visually on Windows** and reported three defects. All were reproduced over
the debug API on a real boot in-container, and **four of the six filed items are fixed and gated**:

| id | state |
|---|---|
| ✅ **`CE-101`** | `--mode all` boots **PAUSED**. Root cause: `MasterSyncController`'s ctor published its t=0 baseline anchor with `TargetMode = Continuous`, and `ClusterTimeObservation.Apply` derives `PauseRequested` from that mode ⇒ **an anchor sent for a side effect was also a command**. Opt-in `startPaused` flag; anchor still broadcast. §5c.16 |
| ✅ **`CE-102`** *(= `HN-039`)* | CGF now registers the **shared** `HrotEditLoadHandler`. The blocker was one required arg — it threw on a null `IZoneManagerService`, which CGF composes none of; now optional **and reported**. entityCount 0→8. §5c.17 |
| ✅ **`CE-104`** | `/sim/pause`'s ack now means **applied** *(`AwaitPausedAsync`)*, not accepted |
| ✅ **`CE-105`** | `/sim/step {count:N}` honours `N` — the loop moved out of the single main-thread job to the HTTP handler, one gated step per frame. `count:60` → simTime exactly 1.0000000 |
| ⛔ **`CE-106`** | **REFUTED — my operator error.** `/logs` always had `level`/`max`; I passed `limit=400` |
| ✅ **`CE-107`** | the envelope's **success branch dropped `Hint` entirely** ⇒ the API could not say *"ok, but…"*. Fixed + `/logs` now names ignored filters |
| ⚠ **`CE-108`** | edit path never remaps behaviour-param entity ids — **on ANY host, editor included**. Filed, deliberately NOT fixed |
| 🔒 **`CE-103`** | **RULED §0.0e.3. ⭐ Baseline = `CE-113` (TKB-only), investigation = `CE-114`** |
| 🔒 **`CE-109`** | the ruling above. ⚠ **RE-SCOPED §0.0e.4** — a real duplicate, but NOT `CE-103`'s fix; priority dropped |

⭐ **The MCP SKILL sources carry this session's lessons** *(`CE-108` commit)* — §5b *"ok:true is not evidence"*,
§5c *"prove your instrument once"*, §5d the three localising reads, plus per-route notes on `/logs`,
`/sim/step`, `/sim/pause`, `/scenario/load/edit`, `get_entity`, `get_gizmo_frame`. ⛔ **`SKILL.md` is
GENERATED** — edit `DebugApiRouteDocs.cs` / `tools/ai-debug-mcp/skill-parts/`, then regenerate, and
⚠ **build the RUNNER first** *(`gen-catalog.mjs` shells out to `--mode dump-api`; otherwise it is a silent no-op)*.

### 0.0e.3 🔒🔒🔒 `CE-103` — **ROOT-CAUSED + the user has RULED the fix direction. Baseline = `CE-113`.**

📄 **[`Q64`](Architect_Question_64_Scenario_Component_Overrides_Across_The_Wire.md) — read §6 (the ruling)
FIRST, then §7 (the baseline), then §8 (the investigation).** ⛔ **§4's leans are SUPERSEDED.**

🔒 **The ruling, `2026-08-28`:** vehicle parameters live **only in the TKB**, loaded equally by every node.
Saving them to the scenario is **an error at this stage**. Overrides may come later, sent **from the loading
node over DDS** the way `SimTransform` already travels. ⛔⛔ **A receiving node must NOT read the scenario
file** — the loading node stays authoritative and sends everything but TKB material, *so that any
non-scenario entity can be created at runtime later.*

⛔⛔ **MY OWN LEAN WAS REJECTED, and the reason is worth carrying:** I recommended *"the receiving node reads
the scenario it already stages"* because it needed no wire change. ⭐⭐⭐ **A runtime-created entity has no
scenario file to read** ⇒ it would work for scenario load and fail for every other spawn. 📌 **I optimised
the COST axis and never checked the CAPABILITY axis.** ⚠ A cheap fix that forecloses a planned capability
is not cheap.

⭐⭐⭐ **THE BASELINE IS SMALL AND HALF-WRITTEN — `CE-113`.** `NedTkbBuilder.WithPhysics` receives a
`SimVehicleDef` carrying **`Height`, `TurnRate`, `Mobility`** and **drops all three**, commenting *"mapped
to VehicleParams by translator in Phase 6."* ⛔ **Phase 6 never happened** ⇒ the DTO has 6 fields and **the
TKB physically cannot express a Tank**, which is why every node derives `PersonalCar` / `AccelGain 0`.
⭐⭐ **`NedTkbBuilder.BuildVehicleParams` IS that missing mapping** and has **zero callers** ⇒ 🔒 **ROUTE it
into the translator; do not rewrite, do not delete.** ⚠ It is the function I matched and retracted twice —
never live, always intended; the scenario's stored block is a **fossil of its last run**.

⚠⚠ **`B4` BLOCKS the build:** **two** translators write `VehicleParams` *(`VehicleKinematicsTkbTranslator`
and `InfantryVehicleStateStripTkbTranslator`)*, both `!HasComponent`-guarded ⇒ first-writer-wins by
registration order. **Decide the owner first.**

### 0.0e.3b ✅✅✅ **CLOSED `2026-08-28` — TKB DEFAULTS ALWAYS. `CE-113` is the whole of the work.**

📄📄 **THE INTENT IS NOW A DESIGN: [`DESIGN_Entity_State_Sourcing.md`](../DESIGN_Entity_State_Sourcing.md)**
*(canon row **`R-136`**)* — read that to learn how entity state is sourced. ⛔ **`Q64` is the ARCHAEOLOGY**
*(four rejected designs); do not use it as the reference.* 🔒 **The user found the blocker in his own design
and it closes the question:**
*"NED concept requires each entity to be late-joinable just by listening to DDS and for the entity
descriptors… so each entity will be created from TKB defaults ALWAYS which is the original idea."*

⛔⛔⛔ **ALL FOUR transport designs are DEAD.** ⭐ Do not revive any of them:

| ⛔ dead design | why |
|---|---|
| component-id bitmask *(§12.4)* | leaked an FDP component id onto the wire ⇒ breaks `Q59` §7 |
| `uint64` descriptor mask *(§13)* | has a **ceiling**; the descriptor count will grow past 64 |
| nest overrides in the creating sample | ⛔ **impossible** — `CreateGhost` is called from ≥10 ingress translators ⇒ **first-touch** creation, no privileged sample |
| ⭐ wait-flag + aggregate bundle *(§14)* | 🔴🔴 **WORSE THAN NOTHING** — a late joiner reads `EntityMaster` from **TransientLocal** history and sees the wait bit, but the bundle was a one-shot **`Volatile`** command, long gone ⇒ **the ghost is stuck FOREVER.** 📌 **Any "flag + side-channel" scheme has this**: the flag is durable state, the channel is not |

✅ **Verified while closing:** the entity-state descriptors ARE **`Reliable` + `TransientLocal`**
*(`GenericDescriptors.cs:77/134/168` + all six in `MapDescriptors.cs`)*; the **command** messages are
`Volatile`. ⇒ ⭐⭐⭐ **STATE is TransientLocal, COMMANDS are Volatile — that split IS the architecture.**
⚠ My earlier *"Volatile defeats late joiners"* note was measured on the **commands** and wrongly
generalised to descriptors. ⭐ And `EntityDescriptorUnion` has **no `[DdsTopic]`** *(one topic per descriptor
type; the union is payload-only)*, so a type can exist as an `UpdateEntityDescriptorRequest` payload
**without** becoming published state.

🔒🔒🔒 **THE PRINCIPLE THAT NOW DECIDES EVERY CASE OF THIS SHAPE — the TKB is itself the late-join
mechanism for internal state:**

> ⭐⭐⭐ **Entity state must be reconstructible from (a) the TKB, or (b) published `TransientLocal`
> descriptors. Anything in NEITHER is unreconstructible by a late joiner and MUST NOT EXIST as durable
> state.**

⇒ ⛔ a side-channel override is exactly *"neither"* ⇒ **forbidden, not merely inelegant.** ⭐ That is why all
four designs failed: each tried to create a third source.

⭐ **`CE-114`'s filter is now sharp** — ⛔ not *"what does SimHost register"* *(that removed 1 of 23)* but
🔒 **"does this state need to survive a late join?"** ⇒ **yes ⇒ published descriptor · no ⇒ TKB. No third
answer.** ⚠ **Nothing is in scope today**: `VehicleParams` is ruled internal state ⇒ TKB-only.

⚠⚠ **THE ONE BOUNDARY:** a runtime parameter command has the **same hole, moved** — a node joining after it
holds TKB defaults while others hold the changed value. ⭐ Safe **only** as a transient/authoring action
with divergence knowingly accepted; ⛔ **never the general override mechanism.** 🔒 A parameter that must
differ from the TKB **durably** must be **reclassified** into a real published descriptor.



⭐⭐⭐ **Sequencing — the design question is CLOSED, so there is only one item: `CE-113`.**
⛔ `CE-116` **WITHDRAWN** · ⭐ `CE-114` re-scoped to *"promote to a published descriptor, or fix the TKB"*
with **nothing in scope today** · ⭐ `CE-115` *(per-translator mandatory declaration)* and the
`IDescriptorTranslator` naming reconciliation remain as small independent cleanups.
⭐⭐ **`CE-113` is the whole of the work and depends on none of it.**

⚠⚠ **PROCESS NOTE WORTH KEEPING:** I ran this as a code investigation and swept the design corpus only
after being told to. ⭐⭐ **The sweep changed the answer** — the design confirmed the user verbatim on two
points and revealed `CE-115`. 🔒 **`R-129`: read the owning design FIRST. This is its second occurrence.**

### 0.0e.3c-NEXT 🔴🔴🔴 **READ THIS FIRST — THE AGREED ORDER OF WORK ACROSS THE `2026-08-30` COMPACTION**

> 🔒 **User, `2026-08-30`, verbatim:** *"pls create the design and then we will need to do the compaction —
> so pls remember what we want do do after (the pack and then back to gizmos where we left them when
> diving into TKB and entity creation)."*

⛔⛔ **Both plans below are ALREADY DESIGNED, GATED AND APPROVED. Do not re-derive them, do not re-open the
decisions, and do not start a fresh investigation** — that is the exact cost this section exists to avoid.

#### ⭐ ① FIRST: finish the entity-creation unification

📄 **[`DESIGN_Entity_Creation_Unification.md`](../DESIGN_Entity_Creation_Unification.md)** — `READY-TO-BUILD`,
UML in §4. Tracker: **`CE-140`**.

| order | what | state |
|---|---|---|
| ✅ | steps **1 + 2** — `TkbTranslatorSet` is the one base list; all five spawning sites use it | **DONE `2026-08-30`** |
| ⭐⭐ **do first** | **step 4** — §3.3: move `RegisterUrbanCombatTkbTemplates` out of `Fdp.Examples.Scenarios` into `Hrot.Core` beside `NedTkbCatalog`, seed it from `HrotEnvironment.CreateTkb()`, leave a forwarder. 🔒 **User ruling:** *"if editor builds UrbanCombat stuff then everyone should, editor is the most advanced in that matter."* ⭐ Smaller, independent, and its dependency check is already measured as clean | **approved, NOT started** |
| ⭐⭐ **then** | **step 3** — `EntityCreationPack` per §3/§3.1/§3.2, adoption order in **§5.1** *(Stride node → SimHost → Editor → ⛔ **CGF LAST**, it is the spawning authority → then **IG**)*. ⭐⭐ **The pack has THREE halves (§2.3): origination · materialisation · ghost-projection** — ⛔ **IG DOES adopt**, taking origination + ghost-projection and opting out of materialisation only *(single spawn authority)*. ⚠ An earlier draft said "IG does not adopt"; the user refuted it and §2.3 carries the correction | **approved, NOT started** |
| ✅ **RESOLVED, no longer a blocker** | **[`Architect_Question_65`](Architect_Question_65_Entity_Genesis_Uniformity.md)** — 🔒 **the user was right: genesis is ALREADY peer-to-peer.** 📐 Verified: `CreateEntityRequestSystem.cs:151-156` processes a request **targeted at the local node regardless of `isDefaultProcessor`**, and the comment above the guard says so; `EntityMaster` has no owner field; ID allocation is a DDS service. ⇒ ⭐⭐ **`isDefaultProcessor` is a BROADCAST TIEBREAKER, not an authority gate — no contract change is needed.** ⛔⛔ **My original Q65-A was WRONG and is retracted** *(it routed orders through "the authority" and would have CREATED the CGF bottleneck the user rejected)*. ⭐ **Uniformity is a COMPOSITION problem, which is the pack's job** — every node registers `CreateEntityRequestSystem` + `NetworkSpawningSystem`, with `isBroadcastArbiter` the only differing value. ⚠⚠ **CORRECTED `2026-08-31`: the two GHOST systems are NOT the pack's** — `NedReplicationModule` already registers `GhostCreationSystem` for all roles *(`:252`)* and `GhostPromotionSystem` behind a **NodeRole** gate *(`:308` pure-IG, `:356` Muscle)*, so pure-Brain *(CGF)* is excluded **by construction, and correctly so today**. ⛔ Q65-B is therefore a **two-line gate widening in one file**, sequenced strictly AFTER Q65-A′ — ⛔ **not** "add promotion to every host". 📌 That claim was wrong twice *(first as a host list, then as CGF's missing `.WithReplication()` — CGF builds the module via `nodeFactory.CreateReplicationModule()` instead)*; Q65 §4's Q65-B keeps both retractions. ⛔ §2.3's role-selected HALVES are **SUPERSEDED**. ⚠ **The real first task is a MOVE:** `CreateEntityRequestSystem` lives in `Hrot.CGF/Systems/`, a host assembly, and only 3 hosts construct it. 📄 Q65 §5 lists four obstacles, §6 the sequencing | **resolved `2026-08-30`** |
| ⚠ **separately, NOT in the pack** | **`CE-141`** — IG registers six components *(`VehicleParams`, `PhysicsCollider`, `Health`, `WeaponState`, `PerceptionReceptor`, `TargetMemory`)* that its 2-entry translator list never fills on a ghost. ⛔ **Do not widen the list to "fix" it** — the wire may be the correct source. Needs a live `--mode all` comparison of an IG ghost against its SimHost original | **open, needs the live probe** |

#### ⭐ ② THEN: back to the gizmo / symbology work

📄 **[`UX_Feature_Entity_Symbology.md` §3.8](../UX/UX_Feature_Entity_Symbology.md)** — `READY-TO-BUILD`, UML,
settled with the user over four rounds of correction. ⭐⭐ **Its key property: the switch is EMIT-SIDE**, so
`FDP/ExtDeps/GizmoMap`'s renderer needs **no** change — `MilStd2525` is already a peer token with its own
renderer case. ⛔ **Two earlier drafts proposed a renderer seam; both are in that document's HISTORY and must
not be quoted.**

| row | what | notes |
|---|---|---|
| ⭐⭐⭐ **`CE-134`** | restore the graphical **health bar** *(deleted by `5ce023677`; recover `e726734cc`'s behaviour — three discrete colours, fill width proportional)* | ⭐ self-contained, ~15 lines, no new machinery. ⚠ *"a primitive was emitted"* is a **vacuous** assertion — the badge satisfies it |
| ⭐⭐ **`CE-133`** | the **emit-side path switch** + `map.symbology.path` config key | ⚠ open build-time call: CGF/Editor/ReplayBrowser have no `VisualData`, so their SIDC must be synthesised |
| ⭐ **`CE-135`** | IG's **movement trail** is dead by construction — `ShowHistory` never set at ingress | 🔒 user: *"Let's keep the history trail"* |
| ⚠ **`CE-136`** | a **third SIDC decoder** neutralises assumed-friend entities | ⛔ deliberately not bundled — it changes affiliation on every host |

✅ **Already done in the symbology lane, do not redo:** `SemanticShapeRenderer` deleted · the NATO affiliation
table corrected to the standard *(all 15 characters; neutral/unknown had been swapped)* · `CE-137` *(every
TKB-spawning host writes `VisualData`)* · `CE-138`/`CE-139` *(CGF and the Stride node were passing no
translators)*.

#### ⚠ Environment notes carried across the boundary

| | |
|---|---|
| ⛔ **`hrot-ai-debug` MCP was DOWN all session** | ⇒ **no live `--mode all` verification** was possible for anything after `S4`. `CE-138`'s runtime question and `§3.8`'s live check are both still unrun |
| ⚠ **`codebase-memory-mcp` MCP flaps** | ⭐ **but its CLI works** — `/opt/codebase-memory-mcp/codebase-memory-mcp cli <tool> '<json>'`; note `trace_path` wants `direction: "inbound"`, not `"callers"` |
| ⛔ **the `Stride/` tree cannot build on Linux** | `Microsoft.WindowsDesktop.App` unresolvable, pre-existing. Every Stride edit this session is **static-only** and needs a Windows build |
| ⚠ **`QA-012` is a standing red** | `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` fails in `Hrot.SimHost.Tests`. Pre-existing, backend lane's. ⛔ Do not chase it |

### 0.0e.3d ⭐⭐⭐ **START HERE — ⛔ `S5` IS BLOCKED; PICK FROM §THE AVAILABLE WORK** *(`2026-08-30`; `S1`–`S4` ①+③ are DONE)*

#### 📄 READ THESE TWO FIRST — ⛔ do not re-derive any of it

| doc | why |
|---|---|
| ⭐⭐⭐ **[`docs/DESIGN_Map_Rendering_And_Interaction.md`](../DESIGN_Map_Rendering_And_Interaction.md)** | **the standing architecture reference** — layer map, both gizmo kinds, the render frame, the interaction path, the tool path, the TO-BE, and an **8-item risk register where every item is SILENT when hit**. ⭐ §1 is the orientation |
| ⭐⭐ **[`docs/UX/UX_Feature_Map_Parity.md`](../UX/UX_Feature_Map_Parity.md)** | `UXI-23` itself — §3.9 the slices, §3.9a–i the measured detail, ⭐⭐ **§3.9j the `S2` design + AS-BUILT** |

#### ✅✅✅ `S1`, `S2a`, `S2b` AND `S3` ARE ALL DONE, PUSHED AND LIVE-VERIFIED

| `S2` item | state |
|---|---|
| ① pin `GizmoTypeId` | ✅ **measured NOT a prerequisite** — it lives only on `IGizmoDefinition` *(stateful)*; the merged classes were `IStatelessGizmo`. ⭐ Still owed to `UXI-07` |
| ② merge the three entity projectors | ✅ **DONE** *(`S2a`)* |
| ③ query is `SimTransform` + `NetworkIdentity` only | ✅ **DONE** |
| ④ ⭐⭐ **`MapInteractionPack.Build(ctx)`** | ✅ **DONE** *(`S2b`)* — all five hosts migrated |
| ⑤ fix `CE-126` inside the merge | ✅ **DONE** |
| ⑥ delete the per-host `gizmoGroup.Enabled` literals | ✅ **DONE** — replaced by `MapInteractionContext.StartEnabled` |

📐 **Live on `--mode all`, `S2a` frame → `S2b` frame:** Scenario `53 → 53`, SimHost `55 → 55`,
⭐ **IG `64 → 63`** *(a double registration removed — IG called both the reflection registrar AND the
source-generated one, and `CanvasContextMenuGizmo` carries `[GizmoProjector]`)*.

🔒 **`R-137` in `S2b`:** every per-host difference became a **named context input**, never erased —
`IsSelectedPredicate` *(handles only)* · `StartEnabled` · `Settings` · `BufferCapacity` *(IG's 4096)* ·
`BreakpointManager` · `ContributeExtras`.

#### 📐 WHAT `S1` + `S2a` DELIVERED — ⛔ do not rebuild it

📐 **`S2` merged the three host-private entity projectors into ONE `EntityPresentationGizmo`**
*(`Hrot.Presentation/ScenarioEditor/Gizmos/`)*, removed SimHost's stateless selection gate, and fixed
**`CE-126`** *(a·b·c)* by deleting the copy that carried them. **19 + 2 rails, four inverse-edit red-proofs.**

✅✅ **`CE-123` IS RESOLVED — measured live on `--mode all`, same-boot before/after:**

| perspective | before | after | ⭐ what the delta IS |
|---|---|---|---|
| 🎉 **SimHost** | 🔴 **3** non-`Line` | ⭐⭐⭐ **55** | the map appears. ⭐ `Arrow +12` · `Text +8` · `ContextMenuBinding +8` prove the gate was suppressing **routes and labels too** |
| Scenario | 69 | 53 | **−16 = the DUPLICATE removed** *(`SpatialAnchor`/`SemanticShape` `16→8`)* |
| IG | 80 | 64 | the same `−16` |

⇒ ⭐⭐ **Every perspective now emits ONE primitive set per entity: 8 anchors, 8 shapes, 8 pick boxes / 8 entities.**

#### 🔴🔴 THE ONE THING `S2` SURFACED THAT IS NOT FIXED — **`CE-131`, read it before touching culling**

📐 IG's `MapCullingSystem` marks **every entity invisible** — its viewport comes from projected screen
corners *(`IgApplication.cs:963`)*, degenerate without a real map view. ⚠⚠ **This was invisible for years
because IG's map was drawn by SimHost's and CGF's copies, which ignored culling.** ⇒ 🔒 culling is now the
opt-in setting **`map.entity.cullOffscreen`, default `false`** — ⛔ **do NOT "simplify" by deleting the
culling gate; that discards the capability (`R-137`).** Fix the INPUT, then a host can enable it.

#### ✅✅ `S3` IS DONE — **and its PREMISE was half wrong; read §3.2e before extending it**

🔴 §3.2a claimed declare-and-report *"would have caught"* `CE-123`. 📐 **Re-measured: FALSE.** SimHost HAD
scheduled the group *(`SimHostApp.cs:442`)*, all three systems were present and the gate was open — a
run-set check prints *"nothing unserviceable"* on that configuration. ⇒ `S3` shipped in **two halves**:

| half | what it catches | |
|---|---|---|
| `MapInteraction.RequiredSystems` + `Unserviceable(hostRunSet)` | a host that **never schedules** the map | ⭐ wired in all five hosts; ⛔ **cannot see `CE-123`**, and a rail asserts that on purpose |
| ⭐⭐ **`MapSelfCheckSystem`** | 🔒 **group enabled + eligible entities present + ZERO `SemanticShape`** — `CE-123`'s exact signature | ⭐ ships as the LAST member of the pack's group, so no host can forget it. Reports, never throws; latches; reports recovery |

✅ **Live proof, end to end:** healthy map ⇒ frames unchanged *(Scenario 53 · IG 63 · SimHost 55)* and
**zero** diagnostics. `CE-123` reintroduced ⇒ frame collapsed to the original **`605/3`** and the log
carried *"the map is RUNNING AND DRAWING NOTHING — … 8 entities … zero SemanticShape … after 120 frames"*.

⚠⚠ **A verification lesson worth keeping:** *"zero diagnostics"* is what a healthy map **and** a system
that never runs both look like. ⛔ Only the inverse-edit LIVE run distinguishes them — the unit rail alone
would have left that vacuity standing.

#### ✅✅ `S4` ①+③ ARE DONE — **culling is a POLICY now, and the seam's dead half is alive**

📐 `StatelessGizmoSystem` honours `IsEntityVisible` *(after the mask match; reference-compare fast path for
the `AlwaysVisiblePolicy` default)*. `RegisterAll` takes a `Func<Type, IGizmoVisibilityPolicy?>` resolver —
the missing answer to *how a reflection-discovered projector names a policy*. `S2a`'s inline culling moved
wholesale into `CullingStateVisibilityPolicy` **(ruling 9 — there was a second implementation of *"should
this entity draw?"*)**. ✅ **`CE-129` closed.** Live: frames identical to `S3`, zero diagnostics.

⛔⛔ **`CE-131` IS REFUTED — do NOT go looking for it.** 🔴 I filed it from ONE probe. 📐 Culling forced ON,
IG probed repeatedly: **`0` on probe 1 then `8 · 8 · 8`, IDENTICALLY with and without a fix to the culling
input.** ⇒ a settling artifact. ⭐ An unset-viewport guard IS in `MapCullingSystem` *(3 rails)* on its own
merits — for a genuinely headless node — ⛔ **not as a fix for any observed symptom.** 📄 §3.2g.

#### ⛔⛔ `S5` IS BLOCKED — **measured, `2026-08-30`**

📐 `UX_Feature_Tool_Model.md`'s STATUS block: **`build-state: NOT-BUILT`** — *"no `IToolController`/
`ToolDescriptor`/modal-stack in source, only fossil comments."* ⇒ 🔒 **`UXI-07`'s migration steps 3–4 do not
exist**, and §3.9h says `S5` **must** be sequenced after them or it re-implements the action→tool routing
that lane owns. ⛔ **Do not start `S5`.**

#### ⭐⭐⭐ THE AVAILABLE WORK — **pick one; ⚠ none is pre-approved, ask first**

| candidate | why | size |
|---|---|---|
| ✅ **`CE-137` — VisualData reaches every TKB-spawning host** *(DONE `2026-08-30`)* | 🔒 User: *"the more the subsystems are same, the better."* `PresentationTkbTranslator` added to the **Editor** and **Stride editor** — the same omission `S1` fixed on SimHost, surviving in two more lists. ⛔ CGF/ReplayBrowser are NOT omissions: no TKB spawn path at all. ⭐⭐ And `VisualData` is a **presence-decided optional read**, so there is no per-host decision. ⚠ Also corrected `S1`'s own comment: the `3 → 69` recovery was `MapDisplayComponent`, not this translator | `RW-S` |
| ⭐⭐⭐ **`CE-134` — restore the graphical HEALTH BAR** | 📐 **BUILT then DELETED.** `e726734cc` *(2026-04-22)* made it always-on; `5ce023677` *(GZ059, 2026-05-08)* deleted it with the legacy adapter stack. ⛔⛔ `HealthBarGizmo` never replaced it — it draws `DrawEntityBadge("87%")` and has read-and-DISCARDED `BarWidth`/`BarHeight` since its first commit. ⭐⭐ ~15 lines, no new machinery. ⚠ **A rail asserting "a primitive was emitted" is VACUOUS.** 📄 §3.8.5 | `RW-S` |
| ⭐⭐⭐ **`CE-133` — two symbol paths, switched EMIT-SIDE** | ⭐⭐ **DESIGN SETTLED AND GATED** — [`UX_Feature_Entity_Symbology.md` §3.8](../UX/UX_Feature_Entity_Symbology.md), `READY-TO-BUILD`. 🔒 **The ExtDeps renderer is NOT touched**: `MilStd2525` is already a peer token with its own renderer case, so the switch is control logic in `EntityPresentationGizmo`. ⛔ **Two earlier drafts proposed a renderer seam — both in HISTORY, do not quote them.** ✅ **Step 0 DONE**: `SemanticShapeRenderer` deleted, NATO palette corrected. ⚠ Open build-time call: CGF/Editor/ReplayBrowser have no `VisualData`, so their SIDC must be synthesised | `RW-M` |
| ⭐ **`CE-135` — IG's movement trail is dead by construction** | 📐 `ShowHistory` → `ShowTrail` → `HistoryRecordingSystem` → `HistoryTrail` is a four-link chain whose **first link is never set at ingress**. 🔒 User: *"Let's keep the history trail."* ⛔ This is why `ShowHistory` must not be deleted with the other unused `IgSymbolOverride` fields | `RW-S` |
| ⚠ **`CE-136` — a third SIDC decoder neutralises assumed-friend entities** | `PresentationTkbTranslator.DeriveForceId` maps only `F`/`H` and sends **everything else** to `Neutral` ⇒ an assumed-friend (`A`) or exercise-friend (`D`) platform renders neutral. ⚠ **Deliberately NOT bundled into `CE-133`** — it changes TKB-derived affiliation on every host | `RW-S` |
| ⭐⭐ **`CE-125` — the fixed cyan** | 🔒 [`UXI-10`](../UX/UX_Feature_Entity_Symbology.md) **defect A**, verbatim: *"Every entity is the same cyan … **friend and hostile are indistinguishable on the map** while the simulation itself distinguishes them"* — `EntityPresentationGizmoShared.cs:92`, a literal `Rgba32(100,220,255,255)`. ⭐⭐ **The user asked about this directly** *("will this map unification change the entity symbol colour which is now fixed to cyan?")*. ⚠ `UXI-10` §0 warns there are **two symbology pipelines, both built, not connected** — `StyleResolutionSystem` is the upstream one. 🔒 **Read §0 and §3 before touching anything** — ⭐ and §3.8, which now depends on this for its value | `RW-M` |
| ⭐ **`UXI-10` §3.5 — the `shapeName` half** | the actual filed issue behind `UXI-10`; `MapShapeName` is authored, translated into a component, and **never read** *(seam-law instance 11)* | `RW-M` |
| ⭐ **the `GizmoTypeId` pin** | 🔒 cheap, owed, and protects BOTH lanes — an explicit constant per `IGizmoDefinition`. ⚠ **JOINT**: tell `UXI-07`, since its migration renames these | `RW-S` |
| ⚠ **`S4` ②** | the rest of the configuration surface. ⛔ **Lower value than it looks** — the beachhead (`EntityPresentationGizmoSettings`, the resolver, six named context inputs) is built, and nothing is asking for more knobs yet | `RW-S` |

#### ⭐ THEN `S5`

| | |
|---|---|
| **`S5`** | 🔴 **JOINT with [`UXI-07`](../UX/UX_Feature_Tool_Model.md)** — sequence it AFTER that lane's migration steps 3–4, which own action→tool routing. ⚠ **Pin `GizmoTypeId` as an explicit constant on every `IGizmoDefinition` first** *(it is the FNV-1a hash of the type's FULL NAME and the DDS routing key — a rename silently breaks remote dragging while the handle still draws)*. 📌 `S2` did NOT need it: the merged classes were `IStatelessGizmo` and carry no wire id |

#### ⭐ VERIFY (the recipe that measured `S2`)

```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj --no-restore -v q --nologo
cd Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0
export HROT_DEBUG_API_PORT=8099 FDP_STAGING_ROOT=<a fresh dir>
nohup xvfb-run -a dotnet Hrot.ClusterRunner.dll --mode all > /tmp/run.log 2>&1 &
curl -s --noproxy '*' -m 180 -X POST http://localhost:8099/scenario/load/live \
     -H 'Content-Type: application/json' -d '{"name":"hill-attack","waitForReady":true}'
curl -s --noproxy '*' -X POST http://localhost:8099/perspective \
     -H 'Content-Type: application/json' -d '{"name":"SimHost"}'   # ⛔ NEVER ?perspective= (CE-112)
curl -s --noproxy '*' "http://localhost:8099/panels/_gizmo?max=4000" # ⛔ NOT /gizmo/frame — 404
```
🔒 **Check `ok` before reading `data`** *(`CE-120`)*; the schema is lowercase `primitives`, each with `shape`.
⚠⚠ **`GET /entities` does NOT read the world IG projects from** — it returned `0` under the IG perspective in
BOTH runs while IG's frame carried 16 anchors. ⛔ **Do not use it to explain an IG frame.**
⚠⚠ **A recorded baseline from an earlier session is NOT comparable** — re-measure the baseline on the SAME
BOOT by rebuilding the pre-change sources. 📌 That is what separated `S2`'s real deltas from run-to-run noise.


### 0.0e.3c ⭐⭐⭐ **THE BUILD PLAN — `CE-113`. THE ONLY WORK LEFT. ⭐⭐ BUILD THIS FIRST.**

⭐ **The bug:** on `--mode all` the tanks draw a path and do not move, because **SimHost** *(the muscle, which
runs `CarKinematicsSystem`)* builds its entity **from the TKB via ghost promotion** — and **the TKB cannot
express a Tank**, so it derives `PersonalCar` / `AccelGain 0` / `MaxSteerAngle 0` ⇒ zero acceleration, NaN
steer. 🔒 **TKB is ruled the source** *(§0.0e.3b · `R-136`)* ⇒ **make the TKB sufficient. Nothing else.**

#### ✅ `B4` — RESOLVED BY MEASUREMENT `2026-08-28`. **Not a blocker. Do not re-investigate.**

📐 I had filed *"two translators write `VehicleParams`, first-writer-wins, pick an owner"* as blocking.
**It is not:**

| | |
|---|---|
| `SimHostNodeBootstrapper.cs:146-155` — the cluster's translator list | `SpatialCore` · **`VehicleKinematics`** · `Behavior` · `Combat` · `Perception` · `AiDiagnostics` ⇒ ⭐ **`VehicleKinematicsTkbTranslator` is the ONLY `VehicleParams` writer on the cluster path** |
| `InfantryVehicleStateStripTkbTranslator` | 📐 registered **only** at `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs:1622` *(the Stride editor app, NOT the SimHost node)*, and its own comment says it **STRIPS** `VehicleState`/`VehicleParams` from capsule (infantry) entities ⇒ ⛔ **a remover on another host, not a competing writer** |

⇒ ⭐ **`VehicleKinematicsTkbTranslator` is the unambiguous owner. `B1`/`B2` are unblocked.**

#### ⭐⭐ THE THREE ITEMS, with every anchor needed

⚠⚠ **The file is `BdcTkbBuilder.cs` and the class inside it is `NedTkbBuilder`.** 📌 That mismatch cost me
three grep misses — ⛔ **search the METHOD name, never the file name.**

| # | item | anchors |
|---|---|---|
| **`B1`** | **Widen `VehicleParametersDto` by `Height`, `TurnRate`, `Mobility`** ⭐ **UNBLOCKED — format-safety measured, see below.** ⚠ **and the drop is FIVE fields, not three** *(+`FuelCapacity`/`FuelConsumption`, latent)* | `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/VehicleParametersDto.cs` — a `record` with `[TkbDescriptor("Gen.VehicleParameters")]`, **6 fields** *(Mass·Length·Width·MaxSpeedFwd·MaxSpeedRev·MaxAccel)*. ⭐ The source already HAS the three: `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/SimVehicleDef.cs` carries `Height`, `TurnRate`, `Mobility` *(+FuelCapacity/FuelConsumption)*, and `NedTkbBuilder.WithPhysics` *(`BdcTkbBuilder.cs:78`)* **drops them** under the comment *"Height, TurnRate, Mobility mapped to VehicleParams by translator in Phase 6."* ⛔ **Phase 6 never happened** |
| **`B2`** | **Route the already-written mapping into the translator** | ⭐⭐ `NedTkbBuilder.BuildVehicleParams(SimVehicleDef)` — `BdcTkbBuilder.cs:271`, **`private static`, ZERO callers**: maps `Mobility→VehicleClass` *(Tracked→Tank · Wheeled→Truck · Infantry→Pedestrian)*, bases on `VehiclePresets.GetPreset` *(`FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/VehicleClass.cs:75-91` is the Tank preset)*, overrides Length/`WheelBase=Length×0.6`/Width/MaxSpeedFwd/MaxSpeedRev/MaxAccel, and computes `MaxSteerRate = TurnRate × π/180`. 🔒 **ROUTE it, do NOT rewrite or delete** *(`CLAUDE.md`: unreferenced is not unintentional)*. Target: `FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs:33-41`, which today writes only 5 fields |
| **`B3`** | **Stop the scenario saving translator-derived components** *(start with `VehicleParams`)* ⭐⭐ **UNBLOCKED — and it is a ONE-ATTRIBUTE change** | 🔒 ruling ②: they are **stale TKB duplicates, not overrides**. 📐 `scenarios/hill-attack/scenario.json` stores a full 15-field `VehicleParams` on **6 of 8** entities. ✅ **MEASURED: add `[DataPolicy(DataPolicy.NoScenario)]` to `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/VehicleParams.cs`** — the save set is `repo.GetSaveableMask()`, so a component opts OUT by declaration. 🔒 **This does NOT touch the hand-tested scenario save path at all** *(the user's warning)*. Precedent: `UnitRoster.cs:26` |

#### ✅ WHAT I NEEDED TO KNOW — **ALL FOUR MEASURED `2026-08-28`. BOTH BLOCKERS CLEARED.**

| measured | verdict |
|---|---|
| ✅ **Does widening a `[TkbDescriptor]` `record` break the ZIP-loaded path?** | ⭐⭐ **NO — it is format-safe in BOTH directions, and `B1` is unblocked.** 📐 The generated thunk is `JsonSerializer.Deserialize<TDto>(jsonElement, FdpJsonOptionsRegistry.DefaultRelaxed)` — emitted by `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/TkbDescriptorGenerator.cs:137`, which **re-emits on every build**, so a widened record needs no hand edit. `UnmappedMemberHandling` is unset ⇒ default `Skip` ⇒ an OLD binary reading NEW json ignores the extra members; a NEW binary reading OLD json defaults them. ⚠⚠ **The real hazard is not a break, it is a SILENT ZERO:** a `Gen.VehicleParameters` block with no `Mobility` yields `Mobility = 0` = `TerrainMobility.Tracked`… which is *accidentally* right for tanks and wrong for everything else. ⛔ **`B1` must make absence recoverable, not silently `Tracked`.** ⚠ And `DefaultRelaxed` registers `StrictStringEnumConverter` ⇒ **an enum authored as an INTEGER in TKB json THROWS** — the widened `Mobility` must be authored as a string |
| ✅ **Where does the scenario SAVE path write `VehicleParams`?** | ⭐⭐⭐ **NOWHERE EXPLICITLY — and this makes `B3` a ONE-ATTRIBUTE change that does NOT touch the hand-tested path.** 📐 `ScenarioSerializer.SerializeEntity` *(`FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs:218`)* walks a **caller-supplied `BitMask512`** — `repo.GetSaveableMask()` *(`FDP/Engine/Fdp.Core/EntityRepository.Sync.cs:213`)* — and `FdpAutoSerializer` handles every remaining bit generically. ⇒ `VehicleParams` is saved **because it is registered and carries NO `[DataPolicy]`** *(`FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/VehicleParams.cs:11-13` — only `[StructLayout]` + `[ComponentId]`)*. ⇒ ⭐ **`B3` = add `[DataPolicy(DataPolicy.NoScenario)]`.** 🔒 **EXACT PRECEDENT ALREADY IN-TREE:** `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs:11,26` — *"not saved (`DataPolicy.NoScenario`) because it is entirely derived"* — **the same argument, already accepted** |
| ✅ **Does `Mobility` reach `WithPhysics` for tkbType 100?** | ⭐⭐⭐ **YES — all three dropped fields ARE authored.** 📐 `BdcTkbCatalog.cs:26-37`, inside `WithPhysics(TkbEntityTypes.Tank_M1Abrams, …)`: `p.Height = 2.44f` · `p.TurnRate = 15.0f` · `p.Mobility = TerrainMobility.Tracked`. And `TkbEntityTypes.cs:6` ⇒ `Tank_M1Abrams = 100`. ⇒ **the data exists at the source and `BdcTkbBuilder.cs:87-96` discards it one line later.** ⭐ `B1`+`B2` are a real fix, not a speculative one |
| ✅ **Which TKB source did the live run actually use?** *(NOT on the original list — it turned out to decide whether `B1`'s builder half fixes anything)* | ⭐⭐ **the code-built catalog.** 📐 **TWO sources exist:** ① `HrotEnvironment.CreateTkb()` → `NedTkbCatalog.RegisterAll` → `NedTkbBuilder` *(`HrotNodeBuilder.cs:197`, `HrotNodeBuilderReplicationExtensions.cs:115,178`)*; ② `TkbUnifiedLoader` — **exactly ONE production caller**, `Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs:96`, which **`_tkbDb.Clear()`s and REPLACES the code catalog** when the staged scenario names a `TkbName`. 🔴 **But `find` shows NO TKB `.zip` and NO TKB `.json` anywhere in the repo** ⇒ ② cannot have run ⇒ ① is live. ⭐ **So fixing `WithPhysics` fixes the running system** — ⚠ **and when a real TKB zip IS staged one day, the authored json must carry the three fields or the bug returns via `Mobility = 0`** |
| ⚠ **Are the other translator-derived components ALSO degraded?** | ⭐⭐ **MEASURED — 33 components across the 6 cluster translators, and the answer is bigger than `VehicleParams`: the "Phase 6" migration is UNFINISHED IN FIVE PLACES IN ONE FILE.** ⛔⛔ **DO NOT fold these into `CE-113`** — see the table below and `CE-117`/`CE-118` |

##### ⛔⛔ The wider finding — **`WithPhysics` is the only one of five that is even PARTLY wired**

📐 Measured on `BdcTkbBuilder.cs`; the giveaway is a *"will be applied by translator in Phase 6"* comment in
each. ⚠ **Every one of them takes a `configure` lambda the catalog fills in, and four never store the result.**

| builder method | authored input | reaches a DTO | verdict |
|---|---|---|---|
| **`WithPhysics`** `:78` | **11** `SimVehicleDef` fields | **6** | 🔴 **drops FIVE, not three** — `Height` · `TurnRate` · `Mobility` **+ `FuelCapacity` · `FuelConsumption`** *(the last two are latent: nothing consumes them yet)*. ⇒ **`CE-113`** |
| **`WithCombat`** `:103` | `SimCombatDef` | **4 DTOs** ✅ | ⭐ **the one that IS finished** — the model to copy |
| **`WithVisual`** `:65` | **5** `IgVisualDef` fields | 🔴 **ZERO** | ⛔⛔ **`configure` is NEVER INVOKED** — the whole catalog lambda *(`SymbolCode`, `ModelPath`, `ColorHex`, `Scale`, `ShowLabel`)* is dead code, and **`VisualDefinitionDto` has ZERO producers repo-wide.** ⇒ **`CE-118`** |
| **`WithFaction`** `:170` | `factionId` | 🔴 **ZERO** | ⛔ ignores its argument entirely, **and `WithBehavior` `:204` never sets `BehaviorProfileDto.Faction`** ⇒ `BehaviorTkbTranslator.cs:35` stamps `EntityInfo { ForceId = dto.Faction }` = **0 for every TKB entity**. ⇒ **`CE-117`** |
| **`WithHeavyMemory`** `:222` | — | 🔴 **ZERO** | `Blackboard1024` never added despite the doc-comment promising it |

⭐⭐ **The design sweep that must precede touching the visual half** *(`R-129`, and it changed the verdict)*:
📄 **[`docs/UX/UX_Feature_Entity_Symbology.md`](../UX/UX_Feature_Entity_Symbology.md)** §0 — *"HROT has two
symbology pipelines, fully built, that are not connected to each other"* — the upstream one is
`StyleResolutionSystem`, a **3-layer merge whose FIRST layer is the TKB default**. ⇒ ⛔⛔ **`WithVisual`'s
drop belongs to that LIVE design's lane (`UXI-10`, "ready to break into `UXT` tasks"), NOT to `CE-113`** —
fixing it here is exactly the *"fixing a surface the design already plans"* error.
⭐ **The faction half has no such owner:** 📄 `docs/projects/Hrot/Engine/Hrot.Core.md:743` documents the
intended chain **including `WithFaction(id, n)`** ⇒ its no-op is a **genuine defect, not a vestige**.
⚠ **`CE-117` still owes ONE measurement before it is called a live bug:** does
`EntityDataAttributeInstaller.cs:46` *(which sets `ForceId` from an attribute record)* **overwrite** the
zero on the cluster path? If it does, the drop is masked in practice.

#### ⭐ HOW TO VERIFY — **the exact probe that diagnosed it**

```
POST /scenario/load/live {"name":"hill-attack","waitForReady":true}
POST /perspective        {"name":"SimHost"}        # ⛔ NEVER ?perspective= — it is IGNORED (CE-112)
GET  /entities/1001                                # Components.VehicleParams
```
⭐ **Expect on SimHost:** `Class Tank` · `AccelGain 1.8` · `MaxSteerAngle 0.8` · `MaxSteerRate 0.2617994` ·
`WheelBase 4.758`. 📐 **Before the fix it is** `PersonalCar` / `0` / `0`.
⭐⭐ **Then prove MOTION:** a **position delta over a `simTime` delta** — ⛔ never wall-clock, and ⚠ **the
cluster boots PAUSED**, so `/sim/play` or step first. 📄 Boot recipe: **§0.0e.5**.
⭐ **Worth adding:** the brain-vs-muscle conformance rail — this defect class is **invisible to every unit
rail by construction**, because a unit rail builds one world.

#### ⛔ WHAT WE ARE **NOT** DOING — **all of this is settled; do not reopen**

⛔ no wire change · no new descriptor · no readiness gate · no scenario read on any receiver.
`CE-116` **WITHDRAWN** · `CE-114` **nothing in scope** · `CE-109` **deprioritised** *(a real ruling-9
duplicate, but it fixes nothing reported)* · `CE-115` a **small independent** cleanup *(per-translator
`MandatoryComponents` declaration)* · the `IDescriptorTranslator` naming reconciliation, also independent.

### 0.0e.4 ⭐⭐ `CE-109` — **RE-SCOPED: no longer `CE-103`'s fix, and its priority DROPS**

📐 Measured: both live handlers funnel into the **same** `_extractor.Extract(...)`; the only differences are
**zones** *(only the SimHost/editor handler loads them)* and a **`behaviorRemapper`** *(only CGF passes one)*.
⇒ ⭐ still a genuine ruling-9 duplicate worth collapsing, ⛔ **but it fixes nothing the user reported.**
🔒 The editor's scenario path is still not to be touched.

### 0.0e.4b ✅ DONE THIS SESSION — `CE-110` / `CE-111` *(the instrument, and CGF's missing singleton)*

⭐ **`CE-110`** — the cluster `/tkb/*` served a private empty `TkbDatabase`; **third instance of one defect at
`Program.cs:429`** after `BP-487` and `CE-066`. ⭐ Fixed on the **provider seam** *(`ISubsystemDebugProvider.TkbDb`
· `PerspectiveScopedDispatcher.TkbDb` · `DebugApiService._tkbDb` which now **throws** rather than substituting an
empty catalog · `SubsystemDebugProvider.TkbFrom(world)` · `DebugCapabilities.TkbRead`)*, because the TKB is
genuinely per-node.
⭐ **`CE-111`** — CGF never published `ITkbDatabase` as a world singleton *(SimHost and IG both do)*, so
`DisEntityTypeTranslator` and `EntityPresentationGizmoShared` degraded **silently**.
⭐⭐ **7 new facts, both inverse-edit red-proved.** Live: 10 templates *(was 0)*, `tkb.read` 3-of-4.

⛔⛔ **THE LESSON TO CARRY:** the rule *"a caller that HAS a dependency must PASS it"* did **not** stop instances
2 and 3. ⇒ ⭐⭐⭐ **a per-node dependency has no business being a service field** — put it on the provider seam and
the composition root **cannot** forget it. ⭐⭐ **And `?? new X()` for a per-node dependency is not a convenience —
it is a FABRICATED ANSWER**, which is exactly what made this instance expensive where the other two were cheap.

### 0.0e.5 ⭐ HOW TO BOOT AND DRIVE BOTH HOSTS — **worked out this session; do not re-derive**

```bash
# build the runner FIRST (also required before regenerating the MCP catalog)
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj --no-restore -v q --nologo
cd Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0
export HROT_DEBUG_API_PORT=8099 FDP_STAGING_ROOT=/tmp/.../staging   # per-boot dir
nohup xvfb-run -a dotnet Hrot.ClusterRunner.dll --mode all > /tmp/cluster.log 2>&1 &
# poll until it answers; ~4-13 s
curl -s --noproxy '*' -m 2 http://localhost:8099/status
```
⚠ **`--mode all` must run WINDOWED under Xvfb**, never headless. ⭐ Use a **second port** *(8098)* for a
simultaneous `--mode editor` so the A/B is one command apart. ⭐ `--mode all`'s perspectives:
`Blueprint, BTree, ExCon, HSM, IG, Scenario, SimHost` — **`Scenario` is CGF's**.

⛔⛔ **THREE SELF-INFLICTED TRAPS, all hit this session:**
1. ⛔ **`pkill -f Xvfb` KILLS YOUR OWN SHELL** — its command line contains the pattern. ⭐ Use
   `ps -eo pid,cmd | grep ClusterRunner | grep -v grep | awk '{print $1}' | xargs -r kill -9`.
2. ⛔ **`git commit -m "…"` with embedded quotes shreds into pathspec errors.** ⭐ Always `git commit -F -` + heredoc.
3. ⛔ **A grep pattern is a HYPOTHESIS** — three misses today, the worst being that `BdcTkbBuilder.cs` **contains
   class `NedTkbBuilder`**, so searching the filename "proved" it had no callers.

### 0.0e.6 ⚠⚠ INSTRUMENT RELIABILITY — **what a green does NOT mean here**

⭐ `CE-084`/`CE-088`'s family now confirmed in **four** assemblies. 📐 This session: `Hrot.Presentation.Tests`
red once then **3/3 green**, with the failing identity **ROTATING** *(`ScenarioFileServiceTests.SaveLoad_RoundTrip`,
then `EntityDragGizmoTests` / *"Component type ID 51 is not registered"*)*; `Hrot.SimHost.Tests` **2-red-identical-to-base**
*(⚠ CORRECTED `2026-08-28`: an earlier note here said "1 red"; re-measured over 3 base runs it is **2**, and the
SECOND IDENTITY ROTATES — `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState` ⇄
`EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule`, while
`FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` is red in every run. ⇒ this makes
`Hrot.SimHost.Tests` a **FIFTH** member of the rotating-flake family)*. ⛔ Both over **process-global registries**. ⇒ ⭐⭐ **always prove a red at base by stashing the
change and re-running**, and ⭐ **re-run a suspicious suite 3× before believing either colour**.
⚠ **`AssetRootsTestCollection`** *(`CE-099`)* now serialises everything touching `AssetRoots.ConfiguredRoot` —
🔒 **join it if you add such a test**; a filtered green is not evidence.

### 0.0e.7 ⭐ OPEN, carried
`CE-103` *(in flight)* · `CE-109` *(the live-path slice)* · `CE-108` *(edit-path remapping, low)* ·
`CE-087` *(profiler not in the default layout — needs a WINDOWED session: place it, File > Layout > Save current
as default; user: no issue while unshipped)* · `CE-073` *(tracker gate matches only `BP-` rows — it reported OK
for every `CE-` row this session)* · `CE-084`/`CE-088` *(above)* · older: `CE-055`, `CE-062`, `CE-063`,
`CE-047`, `CE-048`, `CE-050`, `MX-011`, `CE-074`, `CE-077`.
⚠ **No windowed/eyes verification** of any of this session's work — every gate was API-level or a suite.


## ⛔ 0.0d — **HISTORY: phase 2's plan and safety net** *(`2026-08-27`)* — ⚠⚠ **DONE (slices ①②③, `J1`, `J2`, `J3`). SUPERSEDED by §0.0e; do NOT start here.**

> 🔒🔒 **USER RULINGS, `2026-08-27` — canon for phase 2:**
> ① *"in the end there should be **one UI logic (no drifts, no duplications)**, instantiated by calling
> shared code from different subsystems."*
> ② *"we never use `--mode cgf`, it was **`--mode all`**."*
> ③ *"i want to be **compaction safe**"* ⇒ ⭐⭐ **this section is written to be self-sufficient: it repeats the
> numbers rather than pointing at them, so a fresh session needs no archaeology.**

### ⭐ WHAT PHASE 2 IS
📐 The two composition roots. **`EditorSubsystem.cs` 5 375 lines / `CgfSubsystem.cs` 2 693** — ⚠⚠ but
**~37 % of both is COMMENT** *(1 836 + 1 126)*, so real code is **4 290**, and the composition itself is
**~1 650**: `EditorSubsystem.RegisterWindows` *(2 110 lines / **1 156 code**)* and CGF's
`BuildAiShell`+`WireAssetCreation`+`RegisterWindows` *(**~500 code**)*.
⛔ **A line-count target is the WRONG goal** — that comment mass is how a post-compaction session learns why
a line exists. ⭐ The goal is **one implementation per concept**, not fewer lines.

### 🔴 THE SCOPE — **5 hosts, not 2. I had this WRONG until the user asked.**
| surface | who has it |
|---|---|
| menus · toolbar · perspectives | ⭐ **ONLY the editor and CGF.** 📐 IG *(~62 code lines)* · SimHost *(~127)* · ExCon *(~124)* · Orchestrator *(~100)* · EyesAndMuscle *(~1, empty)* register **windows only** ⇒ they are **panel hosts INSIDE the shell, not shells.** ⛔ Nothing to unify with them there |
| ⭐⭐⭐ **the diagnostics window group** | 🔴 **22 instantiation sites across 7 host files, ~112 lines of copy-paste** — `FdpEntityInspectorWindow` **(5 hosts)** · `FdpEventBrowserWindow` **(5)** · `ArchitectureDiagnosticsWindow` **(4)** · `SystemProfilerWindow` **(4)** · `FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu` **(4)** |

⭐⭐ **The 22 sites differ by exactly FIVE host values** — `idPrefix` · `titlePrefix` · `perspective` ·
kernel/repo accessor · `titleBarColor`:
```
"ig_system_profiler",      "IG System Profiler",      "IG",       () => _app.Kernel?…,      IgWindowColor.TitleBar
"simhost_system_profiler", "SimHost System Profiler", "SimHost",  () => _app?.Kernel?…,     SimHostWindowColor.TitleBar
"cgf_system_profiler",     "CGF System Profiler",     "Scenario", () => _context?.Kernel?…, TitleBarColor
"editor_system_profiler",  "Editor System Profiler",  "Scenario", () => _kernel?…,          EditorWindowColor.TitleBar
```
⇒ ⭐⭐⭐ **that is a `HostServices` record** *(the `CgfEditorShellToolbar.HostServices` pattern)*, so **22 sites
collapse to ONE implementation + 5 one-line compositions** — the user's sentence, currently achieved by
copy-paste 22 times. ⚠ 112 lines is small; ⛔ **22 sites is 22 chances to drift.**

### ✅ `D1`–`D4`, RESOLVED *(design §5c.4d)*
| | |
|---|---|
| **`D1`** | ✅ **services arrive as CTOR ARGS.** ⛔ `UiBundleContext` is NOT widened — a service locator there is `AQ62`'s superseded `ComposeEditorExperience(deps)` bag and would breach `A_bundle_cannot_reach_the_run_set` |
| **`D2`** | ✅ **done = EVERY host's copy DELETED.** ⚠ At 5 hosts, a bundle only 2 compose is **not** done — 📌 the `SharedAiWindowRegistrar` failure mode with more places to hide |
| **`D3`** | ✅ ⓪ the two LOGIC duplicates *(no seam needed)* → ① **the diagnostics group as bundle #1** *(22 sites, 5 hosts)* → ② the editor/CGF-only shell surfaces |
| **`D4`** | ✅ **BOTH decomposition and de-drifting** — the drift risk is 5-way, so de-duplicating achieves both. ⛔ My earlier *"decomposition, NOT de-drifting"* framing is superseded |

### ⛔⛔⛔ 0.0d-① THE SAFETY NET — **how we know nothing broke.** *(design §5c.4e)*
> 🔒 **User:** *"how will you check that the unification does not destroy what now works? will you use current
> editor as something that should not change?"*

⭐ **Yes, current behaviour is the reference — but the editor ALONE is the wrong one, and the rail we had is
blind to the failure unification causes.**

| axis | state | ⛔ blind to |
|---|---|---|
| **A · cross-host parity** *(editor vs `--mode all`)* | ✅ phase 0 | 🔴🔴 **a change that hits BOTH hosts identically.** Drop a window on all 5 at once ⇒ **parity stays GREEN.** ⚠ Unification is exactly that class of change ⇒ **7th rail-blindness instance if relied on** |
| ⭐⭐⭐ **B · before/after, per host** | 🛠 **BUILT `2026-08-27`** — `TheUiBaselineIsPinnedPerHostRails` | pixels, layout, rendering |

⛔ **Why the editor alone is insufficient:** the ids are **host-prefixed**, so the editor's baseline covers
**4 of the 22** sites and proves nothing about `ig_system_profiler` still claiming perspective `"IG"`.

⭐⭐ **THREE captures cover all 22 sites** — 📐 `HrotRunnerConfiguration:124` expands `all` to
**`orchestrator,simhost,ig,excon,cgf`**, and `:181` **forbids the editor coexisting with IG/ExCon**:
| mode | covers |
|---|---|
| `editor` | the editor's 4 sites |
| ⭐⭐ `all` | **SimHost · IG · ExCon · CGF · Orchestrator — five hosts in ONE process** |
| `replaybrowser` | ReplayBrowser's 2 |

🛠 **The rail:** `Hrot.SystemTests/Conformance/TheUiBaselineIsPinnedPerHostRails.cs`. It pins, per mode, the
**`(panelId, kind, perspectives[])`** set from `GET /panels` `registered[]` + `list_perspectives`, through the
existing `GoldenStore`. Goldens live at `Hrot.SystemTests/Goldens/ui-baseline-{editor,all,replaybrowser}/`.
⭐ **Re-capture:** `PANEL_GOLDEN_CAPTURE=1 dotnet test … --filter TheUiBaselineIsPinnedPerHostRails`
⛔⛔ **and a re-capture must be INSPECTED and committed in the SAME commit as the code change** — never
separately, or it blesses whatever happened.

⚠⚠ **FOUR LIMITS — do NOT over-claim this net. ⛔ Two were found by INSPECTING the first capture:**
| ⚠ | |
|---|---|
| **`GET /panels` reports the INSTRUMENTED set** | ⛔ a window that never calls `PanelSnapshot.DeclareInstrumented` is **invisible** to the baseline. ⭐ `The_instrumentation_gap_is_measured_not_assumed` prints the real counts per mode — ⚠ **read them; the gap size is otherwise UNMEASURED** |
| **ids are NOT pixels** | ⭐ catches a dropped, renamed or added window; ⛔ **not** a panel that renders wrong ⇒ a windowed eyes pass stays in acceptance |
| 🔴🔴 **PERSPECTIVE IS NOT COVERED** | 📐 **`registered[]` is PROCESS-WIDE, not perspective-scoped** — 54 of the editor's 55 windows listed all 4 perspectives, because the field recorded which perspectives the CAPTURE VISITED. ⇒ ⛔⛔ **it would NOT catch `CE-071`'s `B1`**, which is what I first claimed for it. ⭐ The field was **REMOVED, not shipped** — false confidence is worse than a named gap. ⭐⭐ Stable source = **`focus_panel`** *(per-panel `{perspective, isOpen, isPinned}`)*, ⛔ but it has **side effects** ⇒ own pass. **FILED** |
| ⚠ **`kind` removed too** | 📐 empty for **18 of 55** — inverted from `kinds{}` which derives from `captured[]` ⇒ **frame-dependent** ⇒ spurious reds later |

🔒 **THE METHOD LESSON:** `GoldenStore` demands a capture be INSPECTED before commit *("a capture run is green
by construction")*. 📐 That inspection found **two defects in the RAIL, none in the product** — ⛔ and both
would have shipped GREEN. ⇒ ⭐⭐ **a golden that has never been read is not a baseline, it is a rumour.**
📐 Also measured while inspecting: **ReplayBrowser names its two `rb_*`** *(`rb_inspector`, `rb_events`)*, not
`*_fdp_*` — so all 22 sites ARE covered; my first grep pattern was wrong, not the capture.

### ⭐ THE ORDER — ⛔ **the baseline is FIRST, before any registration moves**
| # | slice | why here |
|---|---|---|
| **⓪** | 🛠 **capture + commit the three goldens on TODAY'S code** | ⚠⚠ a golden taken after bundle #1 lands **enshrines whatever that bundle did** |
| **①** | ✅✅ **DONE `2026-08-27` — `CE-078`.** Shared `AiAssetSavers` + `AiAssetReload` in `Hrot.Editor.AiShared/Documents/`; both hosts call them; `_btreeQuickReloadTrigger`/`_hsmQuickReloadTrigger` DELETED. 📄 design §5c.6 *(+ §5c.6.7 as-built)* | ✅ 12-fact equivalence rail, **3 inverse-edit red-proofs**; T3 baseline 5/5 with goldens **unchanged** |
| **②** | ✅✅ **DONE `2026-08-27` — `CE-082`.** `DiagnosticsWindowsBundle` + `DiagnosticsHostServices` in `Hrot.Presentation/Windows/`; all FOUR hosts compose it; IG/SimHost gained their first `Compose` call. 📐 **20 sites / 4 hosts, NOT 22 / 5** *(see the estimates list)*. 📄 design §5c.7 *(+ §5c.7.6 as-built)* | ✅ 9-fact equivalence rail, **3 inverse-edit red-proofs (8/9 red)**; ⭐⭐⭐ **T3 baseline 5/5 with the three goldens UNCHANGED** — the load-bearing proof for a registration move |
| **③** | ✅✅ **DONE `2026-08-27` — `CE-089`, and it was ALREADY 90% SHARED.** 📐 Menus: **0 hand-written registrations in either host**; perspective buttons/icons/AI-debug commands: already one implementation each. ⭐ What remained: `ShellTimeControlToolbar` *(4 lines × 2, the separator now a NAMED PARAMETER)* + two dead CGF guards. ⛔ No bundle — ceremony over 4 lines. 📄 design §5c.8 | ✅ 6-fact rail, 3 red-proofs (4/6 red); toolbar rails + all three goldens **UNCHANGED** |
| ⭐⭐⭐ **PHASE 2's SLICE LIST IS EMPTY — and the CLOSING INVENTORY is MEASURED** | 🔒 ⓪①②③ all done. 📄 **design §5c.9 carries the numbers, the four remaining clusters `J1`–`J4`, the recommended ORDER and a STOP CONDITION** — ⛔ read it instead of re-deriving. 📐 **Headline:** the arena is **~1 540 code lines**; **106 identical lines remain (~7 %)**, of which ~26 braces, ~12 field decls and ~8 the shared calls' own invocations ⇒ **~60 meaningful**, and **~35 of those are merely duplicated ARGUMENT LISTS to already-shared classes** ⇒ ⭐ **~24 lines of VERBATIM logic** — ⚠⚠ **but §5c.9.3b CORRECTS this: `J1`'s catalog-construction block is **~45 code lines PER HOST** of same-logic-different-SPELLING duplication *(editor inline, CGF wrapped in `BuildAssetCatalog()`)*, which the verbatim scan reported as ~5.** ⚠⚠ **That 106 is a FLOOR, not a ceiling — the method finds VERBATIM duplication only, and slice ①'s save delegates were semantically-identical-but-DRIFTED, so they would NOT have appeared in it.** ✅✅ **`J2` DONE `2026-08-27` (`CE-091`)** — ⭐ built `RefreshJsonContributors`, the method the builder's own doc promised and nobody had built; the 6-line lambda is gone from both hosts. ⛔⛔ **Its `K2` half was WITHDRAWN after implementation:** 📐 **11 tests inject those four delegates to assert the create SEQUENCE** ⇒ 🔒 **a repeated ARGUMENT LIST can be a TEST SEAM, not accidental duplication** — the verbatim metric counted 5 lambdas as duplication and 4 were load-bearing. ✅ **`J3` CONCLUDED NOT WORTH BUILDING (`CE-092`)** — all three document factories are behind the cycle, and the identical parts are already shared calls. ⭐⭐⭐ **`J1` IS DESIGNED (§5c.12) AND WAITING ON A NOD — and its prize is NOT the ~45 lines:** 🔴 **`CE-093`, the editor cannot load its own BTree/HSM JSON assets on a DEPLOYED node** *(it resolves roots with `ResolveProjectDir` — walk-up only, null off-tree — where CGF uses ruling 67's `ResolveBase`)*. ⛔ **If that behaviour change is declined, CLOSE `J1`** rather than build it for tidiness. ⭐ Order from here: `J1`** *(cheapest — AiShared, no cycle, changes NO UI output)* **→ `J3`** *(same clean home, ⚠ GOLDEN-SENSITIVE)* **→ `J1`** *(⛔ DESIGN PASS FIRST: cycle-bound like slice ①, and the design must be allowed to conclude it is NOT worth unifying)* **→ `J4`** *(never alone)*. ⛔⛔ **STOP after `J2`+`J3`** — ~30 lines of argument lists is not a bundle. ⭐ Open questions meanwhile: `CE-087` *(profiler missing from the shipped layout — needs YOUR windowed re-save)*, `CE-086`, `CE-090`, `CE-073`, and the flaky-suite pair `CE-084`/`CE-088` |

⛔⛔ **EVERY slice carries an EQUIVALENCE rail** — 🔒 `CE-072`'s lesson: *a wrapper needs an equivalence rail
the day it is introduced*, because **when a wrapper becomes the only production path to tested code, the
existing tests stop covering production.** ⚠ At 5 hosts this matters more: each host's ids and perspective
must come out **byte-identical**, or someone's saved layout resets.

### ⛔⛔⛔ 0.0d-② THE CONSTRAINT THAT SHAPES EVERY REMAINING SLICE — **a reference CYCLE** *(measured `2026-08-27`, `CE-078`)*

📐 **`Hrot.BTree.Editor`, `Hrot.Hsm.Editor` AND `Hrot.Blueprints.Editor` ALL reference
`Hrot.Editor.AiShared`.** ⇒ ⛔⛔ **AiShared can NEVER name `BehaviorTreeAsset` / `HsmAsset` /
`BlueprintAsset`** — that is a **circular project reference**, not a style preference.
📐 **And the only NON-TEST projects that see all three are the two hosts themselves** ⇒ ⛔ **there is no
existing shared home** for logic that needs the concrete asset types, and a **new project is the wrong
price** for a few dozen lines in a 149-project solution.

⭐⭐⭐ **THE WAY THROUGH, and it generalises: `DTO`-in, not asset-in.** The DTOs
*(`BehaviorTreeAssetDto`/`HsmAssetDto`)* live in **`Hrot.AiEditor.Persistence`**, which AiShared **does**
reference, and every serialize/emit step already takes a DTO. ⇒ ⭐ **only `ToDto(asset)` and the compiler
adapter stay host-side; AiShared owns everything after the map.**

⭐ **This was ALREADY WRITTEN DOWN and I nearly re-derived it from scratch** —
`SaveAllAiDocumentsCommand.cs:10`: *"Kind-specific serialization is injected as delegates to avoid
circular assembly references … design §PU-602."* ⇒ 📌 **the seam law once more:** those delegate
parameters looked like a style choice and were load-bearing. ⚠ **Slice ③ (menus/toolbar/perspectives) will
meet the same wall** — ⭐ check the reference direction BEFORE choosing where shared code lives.

### ⛔⛔ 0.0d-③ A T3 RAIL MUST BE GATED THROUGH THE SCRIPT AT LEAST ONCE *(`CE-081`, `2026-08-27`)*

🔴 **`scripts/run-system-tests.sh` filters `(Category=SystemSmoke|Category=SystemModes)`.** 📐 `CE-075`'s
baseline rails declared only `[Trait("lane","T3")]` ⇒ the script printed **"No test matches the given
testcase filter"** and exited **`0`** — ⛔⛔ **a silent ZERO-TEST GREEN**, and the whole phase-2 safety net
was unreachable from the project's own entry point for a day. ✅ Fixed with
`[Trait("Category","SystemModes")]` *(the bucket `ModeStartupRails` uses)*; 📐 the same command now runs
**5/5**.
⇒ ⭐⭐ **`dotnet test --filter` BYPASSES the category filter**, which is exactly how it hid — so a new T3
rail is not gated until **`run-system-tests.sh <Name>` has printed a non-zero test count.**

### ⛔⛔ 0.0d-④ TWO GATING TRAPS FOUND WHILE BUILDING SLICE ② *(`2026-08-27`)*

| ⚠ | |
|---|---|
| 🔴🔴 **`Hrot.Presentation.Tests` IS FLAKY AND THE IDENTITY ROTATES** *(`CE-084`)* | 📐 **3 of 6 runs failed** with the new rail EXCLUDED, a **different test each time** — `EntityDragGizmoTests` · `RouteWaypointGizmoTests` · `TheDragCommitsThroughTheWriteRouterTests` ×2, all `Hrot.ScenarioEditor.Tests`, all gizmo/ECS-write, all **green in isolation**. ⇒ ⛔⛔ **neither a red nor a green from this suite is evidence** — `--filter` the classes you touched and SAY SO, exactly as the `Fdp.Toolkits.Tests` / `DEBT-AIB-030` rule already requires |
| ⛔⛔ **A FILTERED GREEN IS NOT EVIDENCE A NEW TEST CLASS IS SAFE** | 📐 the slice-② rail passed **9/9 filtered** and the rest of the assembly passed **140/140**, but together **the test host CRASHED** — registering real windows touches the process-global `PanelSnapshot` singleton and the class was running parallel to the four that serialise on it. ⭐ **The convention existed** *(`PanelSnapshotTestCollection`, mirrored in two other assemblies)* and the rail was written without it. ⇒ ⭐⭐ **run the WHOLE project suite before believing a new rail** |

### ⚠ FIVE SIZE ESTIMATES I GOT WRONG THIS SESSION — **measure before quoting**
⛔ *"a 24-site cross-assembly rename"* → 📐 **19 hits, 9 files, one tree** *(the 24 was the graph's DEGREE)*.
⛔ *"`SharedAiWindowRegistrar` is the cheapest adopter"* → 📐 **CGF constructs 0 of its 7 windows.**
⛔ *"`CE-018`: three copies of a `.csproj` walk-up, ~190 lines"* → 📐 **already FIXED**; the sizing counted the
**comment recording the fix**.
⛔ *"the save cluster is the biggest prize, ~426 lines"* → 📐 **`SaveAllAiDocumentsCommand` is already shared
and both hosts already call it**; the lines were comment + shared calls.
⛔ *"phase 2 is editor-vs-CGF"* → 📐 **22 sites across 5 hosts.**
⛔ *"the BTree/HSM save delegates are LINE-FOR-LINE duplicates"* *(this doc said so)* → 📐 **semantically
identical, syntactically DRIFTED** — the editor used `as`+null-check+a `prettyJson` local, CGF used
`is not … return` + inlined flatten. ⭐ The drift had already happened; the claim was too strong in the
detail and too weak in the conclusion. ⚠ **The RELOAD arms genuinely were line-for-line.**
⛔ *"CGF has ONE dispatcher, the editor has three callbacks"* → 📐 **THREE dispatchers, not two**: CGF's
method, the editor's toolbar switch, **and the editor's MCP `reloadAsset` route** — each with its own
wording for the same condition.
⛔ *"the diagnostics group is 22 sites across 5 hosts"* *(this doc's own headline)* → 📐 **20 sites across
4.** ⭐ Each of the five call kinds is exactly **4**; ⛔ **ReplayBrowser is a DIFFERENT TYPE in a DIFFERENT
ASSEMBLY** *(`Fdp.Presentation.Windows.ReplayBrowser.*`)* with no profiler and no architecture window, so it
can never join that bundle. ⚠ **`search_graph` returned BOTH same-named classes — that is what caught it**;
grep for `new FdpEntityInspectorWindow` alone would have counted 5 hosts and been wrong about one.
⇒ 🔒 **measure CODE lines and read the call sites before naming a slice or a size.**

## ⛔ 0.0c — **HISTORY: the `CE-070`/`CE-071` way-forward** *(`2026-08-27`)* — ⚠ **SUPERSEDED by §0.0d; do NOT start here**

> 🔒 **USER, `2026-08-27`:** *"cgf==editor is still valid here (the goal of the whole programme), which
> should resolve the question"* ⇒ ✅ **the ROLE question is RESOLVED: CGF gets the AI shell.**
> ⭐⭐⭐ **And the finer distinction turned out to be real: CGF ALREADY HAS IT, so the next item is a
> DELETION, not an adoption.** 📄 design **§5b.5** carries the full measurement and the corpus citation.

### ⭐ THE QUEUE — in order
| # | item | state |
|---|---|---|
| ~~**1**~~ | ✅ **`CE-070` — `SharedAiWindowRegistrar` DELETED** *(`2026-08-27`)*. ⭐⭐ **The build found a stronger argument than the analysis had:** its windows declare **`WindowScope.PerspectiveBound`** and it was a **flat host-level** registrar ⇒ ⛔ **it could never have worked even if a host had called it**, which closes the *"an out-of-repo host might call it"* defence. ⭐ Its rail is replaced by **its inverse** *(`AddSharedAiEditor_Registers_No_Flat_Host_Level_WindowRegistrar`)*, because a flat registrar is the shape a session re-adds by reflex | ✅ **DONE** — as-built §5b.6 |
| ~~**1**~~ | ✅ **`CE-071` — the comparison result surfaces are LIVE on both hosts** *(`2026-08-27`)*. 🔒 The user's MCP question resolved it: the MCP obsoletes the **export** half *(an agent reads both revisions with `git show`)*, ⛔ but **no MCP tool annotates a graph node** ⇒ ⭐⭐ **the half that becomes more valuable is exactly the half nobody wired.** 📐 It was easy — nothing was unbuilt, six wiring sites were missing. ⭐⭐ **`D5` FLIPPED:** the canvas renderer was the design's *deferred* piece and turned out to be the **cheapest** — every factory already composes "built-in + extras" and ships 4–6 live renderers | ✅ **DONE** — as-built §9 |
| ~~**2**~~ | ✅ **`CE-072` — phase 1 CLOSED.** ⛔ **The item's premise was FALSE:** 📐 the only production caller of `RegisterCommonCore` is `ShellCommandCoreBundle:98` ⇒ **no remaining direct callers to migrate.** ⭐⭐ **But looking found a real gap — the 6th rail-blindness instance, in `CE-069`'s own code:** zero tests referenced the bundle, so all seven `TheToolbarLayoutIsOneListTests` rails call the STATIC while production goes through the WRAPPER ⇒ a mis-forwarding bundle would have left them all green. 🛠 `The_bundle_emits_exactly_what_the_direct_call_emits`, red-proved by two inverse edits | ✅ **DONE** — as-built §5b.7 |
| **2** | ⚠ **`CE-073` — `tracker-counts.py --check` counts ONLY `BP-` rows** *(448 of 760)*, so **72 `CE-` · 69 `ST-`/`QA-` · 58 `TM-`/`MX-` rows are ungated** ⇒ every *"tracker-counts OK"* in this programme's gate reports never looked at the rows the batch had just added. ⛔ **Not fixed unilaterally** — widening it re-baselines the Total in every future report ⇒ 🔒 **needs a nod**: widen + re-baseline, or rename the gate to say it covers `BP-` only | ⭐ measured, awaiting a decision |
| **3** | ⚠ **`CE-074` — `SKILL.md` has no "Capabilities & boundaries" section**, though the skill's own instructions cite one and tell you absence *"must be stated explicitly in the SKILL's boundaries section, not inferred from source"*. 📐 `skill-parts/` has six partials and none carries it. 📌 Hit for real by `CE-071`: *"can the MCP annotate a graph node?"* had to be derived from the tool list. 🛠 Add a `40-boundaries.md` partial + its assembly line — ⛔⛔ **NEVER edit `SKILL.md`; it is GENERATED** *(user ruling)*. ⚠ **MCP lane's call** | ⭐ filed |
| **3** | ⭐⭐ **phase 2** — one bundle per batch from the editor as specimen: **scenario panels → gizmos → map → AI shell → time transport**. ⛔ Each needs its **own inventory + UML before code** *(obligations ①/②)* | needs design per batch |
| **4** | open ids: `CE-062` *(blueprint live-value provider on CGF)* · `CE-063` *(`EditorMapPickAdapter` vs `CanvasMapPickAdapter` — ⛔ do not merge blind)* · `CE-047` · `CE-048` · `CE-050` *(rotating ALC flake)* · `MX-011` *(MCP lane: gizmo buffer into `PanelSnapshot`)* | unchanged |
| **5** | ⚪ **the "map shows no entities" symptom** — ⛔ still **unreproduced**, not fixed. The rail stands | watch |

### ⛔⛔ THE THREE TRAPS THIS SESSION PAID FOR — **do not re-pay them**
| # | trap | the guard |
|---|---|---|
| **①** | ⭐⭐⭐ **"the caller HAS the dependency and does not pass it"** — `BP-487` *(gizmo buffer)*, `CE-065` *(event registration)*, `CE-066` *(mission editor)*, **three times in one batch** | ⭐ before designing a shared abstraction, check whether the host **already holds** the thing and merely fails to hand it over. ⛔ Not a missing abstraction — a missing **argument** |
| **②** | ⭐⭐⭐ **THE INVERSE: a class that LOOKS like the shared thing while the shared thing is elsewhere** — `SharedAiWindowRegistrar` was DI-wired, cited in a design, and **superseded by `PerspectiveWorkspaceRegistrar`** *(⇒ DELETED, `CE-070`)* | 🔒 **before adopting any "unadopted shared" class, ask what the hosts ACTUALLY use for that job.** ⛔ In-degree 0 can mean *"somebody solved it better, over there"* |
| **④** | ⭐⭐ **A RESOLUTION RAIL PROVES A TYPE IS REGISTERED, NEVER THAT A FEATURE IS REACHED** — 5th rail-blindness instance. `AddSharedAiEditor_Resolves_…` kept a never-called class alive for months, and its container has **no production caller at all** ⇒ it asserted over a graph nobody walks | ⭐ **when deleting a rail, consider asserting its INVERSE** — the wrong shape is usually the reflex shape. ⛔ And check whether the container/graph a rail asserts over is one production actually walks |
| **⑥** | ⭐⭐⭐ **WHEN A WRAPPER BECOMES THE ONLY PRODUCTION PATH TO A TESTED FUNCTION, THE EXISTING TESTS STOP COVERING PRODUCTION** — `CE-072`: seven rails call `RegisterCommonCore`; since `CE-069` both hosts reach it only via `ShellCommandCoreBundle`, which **no test touched** | 🔒 **a wrapper needs an EQUIVALENCE rail the day it is introduced** *(same args ⇒ identical output, both halves)*. ⚠ Spy-based seam rails do **not** substitute: they prove `Compose` calls *a* bundle, never that *this* bundle forwards faithfully |
| **⑦** | ⚠⚠ **A GREEN GATE MAY BE SCOPED NARROWER THAN ITS NAME** — `CE-073`: `tracker-counts.py` counts only `BP-` rows *(448 of 760)*, so *"tracker-counts OK"* never covered the `CE-` rows each batch adds | ⭐ **read the gate's own filter once**, then cite it with its scope. ⛔ *"the gate is green"* is not *"the thing I changed was checked"* |
| **⑤** | ⭐⭐ **AN AS-BUILT NOTE IS NOT A DIAGRAM EDIT** — §5b.4 recorded `ReportUnserviceable`/the 3-arg ctor as *"not built"* and said *"the diagram above is corrected"*; ⛔ only the `classDiagram` had been touched, so the `sequenceDiagram` stayed false for a batch | 🔒 **obligation ⑤ is satisfied by CHANGING THE PICTURE**, not by describing the change beside it. ⭐ Re-read every diagram in the doc, not just the one you were thinking about |
| **③** | ⭐⭐ **`--no-build` prints PASSED over a STALE BINARY** — `CE-067`: `Hrot.Blueprints.Tests` *(3 983 tests)* had not compiled and every gate over it read green | ⭐ **build the project before trusting `--no-build`**; a *"pre-existing build error"* in a TEST project means **that whole suite is dark**, not that it is noise to route around |

### ⚠ Two of MY OWN estimates were wrong this session, both caught by tools
⛔ *"a 24-site cross-assembly rename"* → 📐 **19 hits, 9 files, one tree** *(the 24 was the graph's DEGREE)*.
⛔ *"`SharedAiWindowRegistrar` is the cheapest adopter"* → 📐 **CGF constructs 0 of its 7 windows.**
⇒ ⭐ **measure the edit surface before quoting a size, and measure adoption before calling something cheap.**

⚠ **A third, added `2026-08-27`:** my `CE-070` deletion argument rested on **adoption** *(in-degree 0, the job
done elsewhere)*. 📐 The **stronger** argument — that the class was the **wrong shape** for
`PerspectiveBound` windows and could never have worked — surfaced only while reading the windows' own
constructors during the build. ⇒ ⭐⭐ **read the CONSTRUCTOR of the thing being registered, not just the count
of who registers it**; the declared scope of a window is a fact about correctness, not about adoption.

⇒ ⭐⭐ Everything is bound by §3's standing constraint: ⛔⛔ **no bundle registers a module, system,
translator or participant** — and that is now **railed structurally**
*(`TheUiBundleSeamHoldsTests.A_bundle_cannot_reach_the_run_set`)*. ⚠ If that rail fails, it is a **DESIGN
question**, not a test to update.

## 0.1 🔒 THE PROBLEM, AND WHAT THE USER APPROVED

⭐ **The problem:** ~85% of each host's composition is the SAME shared pieces wired TWICE, independently ⇒ the *"CGF forgot to wire X"* bug class. 📐 **Every defect `CE-046`…`CE-064` is an instance**, and the user found six of them by eye.

| approved `2026-08-27` | |
|---|---|
| ✅ **`Q63-B` — per-feature BUNDLES**, ⛔ not one `ComposeEditorExperience(deps)` | a monolith forces `if (host==…)` *(ruling 58)* or a nullable-knob bag *(a silent-default generator)* |
| ✅ **`Q63-D` — DISSOLUTION, not extraction**, for `IEditorLogic` | 📐 it is 128 ln / ~15 members and `EditorApplication` is 297 ln of one-line delegations. `CE-060` dissolved one call in ONE LINE |
| ✅ **`Q63-E` — THIS SESSION owns EVERY composition root** | ⇒ ⭐ no cross-lane split needed; fix all roots together |
| ✅ **the parity rail goes FIRST** *(user: "for a refactor like this that rail is absolute must")* | |

## 0.2 🔒🔒🔒 CANON — the two USER RULINGS that constrain every batch

| ⭐ axis | reference | unify? |
|---|---|---|
| ⭐⭐⭐ **UI · scenario editing · monitoring · debugging** | **the EDITOR is the SOURCE AND SPECIMEN** | ✅ **aggressively** |
| ⛔⛔ **the RUN-SET** — modules · systems · services | **each host's ROLE** *(the editor runs almost everything; CGF/IG/SimHost run only what their role needs)* | ⛔ **NEVER** |
| ⛔⛔ **NETWORK** — translators · DDS · participant | **each host's ROLE** *(the sets are near-DISJOINT — measured)* | ⛔ **NEVER** |

⚠⚠ **THE TRAP:** *"editor is the specimen"* and *"editor runs almost everything"* are **TWO DIFFERENT AXES.** 📌 A *"map bundle"* that registered `MapCullingModule`+`StyleResolutionModule` because the editor does would **silently change what CGF computes every frame — and would look like a successful unification.**

### ⛔⛔ THE STANDING CONSTRAINT *(`AQ63` §10.5)*
> **No bundle may register a module, a global system, a DDS translator, an egress/ingress system, or a participant.**
⭐ A bundle **DECLARES** what its affordances need; the **HOST** decides what runs; an unserviceable affordance **REPORTS** it *(the `ToolActivationDrainSystem(reportUnserviceable:)` pattern)*. ⛔ A bundle that seems to need one has hit the role boundary ⇒ **STOP and report** *(`R-106`)*.

## 0.3 ⭐⭐⭐ PHASE 0 — the parity rail. **VENUE AND CHANNELS ARE SETTLED; NO PRODUCTION CHANGE**

| ⭐ | |
|---|---|
| **venue** | ⭐⭐⭐ **TWO WINDOWED PROCESSES under Xvfb, driven over MCP.** ⛔ **NEVER headless** — a panel publishes only when it DRAWS |
| **it already exists** | 📐 `ClusterConformanceRails.The_asset_panels_are_the_same_on_both_hosts` *(`:867`)* launches `StartAsync("…-editor")` + `StartAsync("…-all", mode:"all")`, captures by KIND, and asserts anti-vacuity **both** directions ⇒ ⭐ **phase 0 EXTENDS what it compares, not where it runs** |
| **channels** *(all exist — read `tools/ai-debug-mcp/SKILL.md`, ⛔ never derive MCP capability from engine source)* | ⭐ **`list_panels`** → `kinds` is *"the key a cross-host comparison uses"* · ⭐ **`get_panel`** → the view model, *"assert a field, do not parse prose"* · ⭐⭐⭐ **`get_gizmo_frame`** → *"what the map is drawing this frame, as data"* |
| ⛔⛔ **what it must NOT assert** | **run-set equality.** ⭐ It proves each host is INTERNALLY COHERENT and that shared **SURFACES** match — ⛔ never that two hosts RUN the same thing *(`AQ63` §10.4 — this CORRECTS the frame handoff's wording)* |
| **tier** | ⚠ **`T3`** *(two windowed processes, minutes)* ⇒ async / CI, ⛔ never a foreground blocker |

### ⭐ The phase-0 work items
| # | item |
|---|---|
| **①** | extend the two-host comparison to the **8 known drift instances**: scenario catalog non-empty · perspective icon keys resolve · `debug.*` group present · create-core single · `MutationInterceptor` set · perspective toolbar section present · scenario root · center/rotate routed |
| **②** | ⭐⭐⭐ **map parity via `get_gizmo_frame`** — the highest-value piece; reaches what no model-level rail can |
| **③** | the two NEW user symptoms *(`2026-08-27`, **`--mode all`** — ⚠ corrected `2026-08-27`: the user never runs `--mode cgf`)*: ① **the 2D map shows NO entities on some scenarios** *(e.g. `hill-attack` loads, map empty)* · ② **center-on-entity CRASHES** ⚠ **suspect: the `E3`/`CE-051` path is mine** |
| **④** | ⛔ **nothing in production** |
| ⭐ proof | each item must **redden on the pre-fix root** *(inverse edit)* |

## 0.4 ⭐ THE PHASE ORDER *(revised by ruling — `AQ63` §9.4 REVERSES the earlier node-first plan)*

**0** parity rail → **1** the bundle seam + **menus/toolbar** *(`CgfEditorShellToolbar` already IS the pattern)* → **2+** one bundle per batch, **extracted from the editor as specimen** → **N** *(optional, LATER)* node-bootstrap adoption, **CGF first**.
⚠ **Node adoption is deliberately LAST:** it is the only phase that touches orchestration/participant/time authority — the area the ruling says not to move blindly — and 📐 **not one** of `CE-046`…`CE-064` was a node-bootstrap gap.

## 0.5 ⛔⛔⛔ FACTS A LATER SESSION MUST NOT RE-DERIVE — **including FOUR claims I got WRONG**

| ⭐ fact | |
|---|---|
| ⛔⛔ **`--mode all` MUST run WINDOWED (Xvfb). Headless dumps come back EMPTY** | 🔴🔴 **THIS FILE'S §0-prev ALREADY SAID SO, flagged ⭐⭐⭐, and I still built `AQ63` §11 on a headless venue.** ⇒ ⭐ read §0.5 *before* designing a rail. **Xvfb IS installed** *(`/usr/bin/Xvfb`)* and `run-system-tests.sh` uses it |
| ⛔ **"this container has no display"** | ⚠⚠ **WRONG — I put it in two reports and a tracker row.** 📐 T3 ran **105 passed / 2 failed** here. I generalised ONE X11 `SIGSEGV` *(`ModeStartupRails(ig)`)* into a capability claim |
| ⛔ **"the map render path is eyes-only"** | ⚠ **WRONG** — `get_gizmo_frame` returns it as data |
| ⛔ **"`translatorPacks` unsupplied is a silent-default defect"** | ⚠ **RETRACTED** *(`AQ63` §9.3)* — External is a **network POSTURE** change; ingress comes from the factory. It is a **dead parameter**, not a missing dependency |
| ⭐⭐ **the god-facade is NOT a blocker** | 📐 `IEditorLogic` 128 ln / ~15 members; `AiShared` references it in **ZERO code** *(prose only)*; ~3 members genuinely editor-only |
| ⭐⭐ **the pre/post-`Kernel.Initialize()` line already IS the node/UI boundary** | 📐 editor `:1757` of 5325 · CGF `:850` of 2599 — same 33/67 ratio; **0** kernel-module registrations after it |
| ⭐⭐ **ExCon · ReplayBrowser · Orchestrator own NO kernel** | 📐 0 `ModuleHostKernel`, 0 `RegisterModule`; only `RegisterWindow` *(9/6/2)* ⇒ they are **pure bundle consumers** |
| ⭐⭐ **the seam for the UI half EXISTS, used backwards** | 📐 `IWindowRegistrar` has 10 impls and **8 ARE the subsystems**; the system half is **50 `IEcsModule`s**. ⭐ `SharedAiWindowRegistrar` = built, in-degree **0** |
| ⚠ **the rail-blindness pattern, THREE times** | `CE-049` asserted *present+enabled* not *has something to offer* · `CE-053` **supplied the input it tested** · `CE-064` had a correct but **UNREACHABLE** assertion *(a loop over an empty collection)* |

## 0.6 ⭐ GATES + the commands that matter

```bash
bash scripts/session-design-brief.sh              # RELEARN: ledger + 7-day digest + probes
python3 scripts/rulings-check.py                  # 25/25 expected
python3 scripts/design-digest.py --check          # STATUS headers + INVENTORY + UML
python3 scripts/tracker-counts.py --check         # open 102 / done 346 at CE-064
bash scripts/quick-check.sh <proj> [filter]       # T0, ~8 s
dotnet build <affected.csproj> --no-restore       # ⛔ NEVER the .sln in the fix loop (115 s vs 8 s)
bash scripts/run-system-tests.sh --no-build       # T3, ~11 min, ASYNC only
MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file.md>
```

⚠ **Two known T3 reds, both PRE-EXISTING** *(proved against a base worktree)*: `/missions/*` capability classification *(**MCP lane's**, unfixed)* and `EntityBlueprintsEditModelTests` / `SimHostInstance` compile errors. ⚠ `TwoReloadCycles_OldAlcIsCollected` is the **known rotating ALC flake** *(`CE-050`)*.

## 0.7 ⭐ OPEN ids carried in
| id | |
|---|---|
| `CE-062` | blueprint live-value provider on CGF — ⭐ **unblocked** by `CE-059` |
| `CE-063` | `EditorMapPickAdapter` **duplicates** the shared `CanvasMapPickAdapter` — ⚠ possibly two capability levels; ⛔ do NOT merge blind |
| `CE-055`/`CE-056` | ⭐ **user confirmed NON-REPRO on a windowed box** *(`2026-08-27`)* ⇒ out of scope; ⚠ close as non-repro |
| `CE-047` `CE-048` `CE-050` | `MigrationAlertManager.Draw()` unwired · `DebugApiService.LoadScenarioLive` not routed via the session · the ALC flake |

## 0a. ✅ THE BATCH IMMEDIATELY BEFORE THIS — **`--mode all` parity** *(`CE-057`…`CE-064`, `2026-08-27`)*

📄 **Report: [`batches/REPORT_Cgf_Mode_All_Parity.md`](batches/REPORT_Cgf_Mode_All_Parity.md)** ·
designs [`../DESIGN_Cgf_Scenario_Windows_Slice.md`](../DESIGN_Cgf_Scenario_Windows_Slice.md) *(§10 = AS-BUILT)*.
⭐ Merged by the coordinator at `c67f1b2ae`; this lane then added `CE-064`.

⭐⭐ **Why it matters to the quest:** all four symptoms the user hit in `--mode all` were composition drift —
⇒ **the evidence base for `AQ63`.**

| id | what |
|---|---|
| `CE-057` | CGF resolved `{staging}/nodes/node-N/scenarios` — **a directory that does not exist**; the scenarios are in `{staging}/shared/scenarios`. `OrchestrationConstants.GetSharedScenariosRoot()` is now the ONE authority |
| `CE-058` | `PerspectiveIconKeys` — ⛔ **NOT a second toolbar**: both hosts build the same `PerspectiveToolbarSection`; the icon TABLE had one caller, so CGF took the documented text-button fallback |
| `CE-059` | CGF constructs `BlueprintDebugSession` *(it already held all 3 ctor args)* + the shared `ActiveDebugSessionMirror` ⇒ the `debug.*` group works rather than being present-and-dead |
| `CE-060` | `ScenarioOrbatAdapter.SelectEntity` **ignored its argument** on BOTH hosts; now publishes `ActivateEditorToolEvent` + `SelectEntityCommand` |
| `CE-061` | **E5** — four `ManagedWindow` wrappers became shared `Hrot.Presentation.Windows.*PanelWindow` types; four adapters moved as `Scenario*`; ⭐ editor window IDS unchanged and railed |
| `CE-064` | every catalogued scenario carries a real `SourceFilePath` — ⚠ found only because `CE-057` made the list non-empty and a T3 rail could finally fail |

## 0-prev. ⛔ HISTORY — **the cross-host conformance harness** *(`2026-08-24`)*

📄 **Designs *(the AS-BUILT records — read these FIRST)*:
[`../blueprints/Architect_Question_54_Cluster_Mcp_Contract.md`](Architect_Question_54_Cluster_Mcp_Contract.md) § AS-BUILT ·
[`../DESIGN_Headless_Testability.md`](../DESIGN_Headless_Testability.md) §6e + § conformance AS-BUILT.**
📄 **Report: [`batches/REPORT_Conformance_Harness.md`](batches/REPORT_Conformance_Harness.md)**.

⭐ **Ids: `HN-025`/`026`/`027` done; `HN-028`, `HN-029`, `MX-014` open.** ⭐ Next free: `HN-030` / `MX-015`.
⭐ **Suite: `76 → 80`, all green.** ⭐⭐ **`--mode all` answers MCP.**

### ⛔⛔ The six facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **`--mode all` MUST run WINDOWED (Xvfb), never headless** | 📐 a panel publishes only when it DRAWS and the headless runner loop never calls `DrawUIAll` ⇒ every dump would be empty. `EditorProcess.StartAsync(mode: "all")` does this |
| ⭐⭐⭐ **A PROVIDER'S DEPS MUST BE LAZY** | 📐 `_clusterTimeAdapter` is built in `RegisterWindows` — AFTER the composition root builds providers ⇒ a value-captured provider reported `time.drive:false` for SimHost and CGF, i.e. **the manifest lying in the safe-looking direction** |
| ⭐⭐⭐ **The conformance diff IGNORES `panelId`; the GOLDENS keep it** | 📐 a VM contains its own id ⇒ two hosts publishing one KIND can never be byte-identical *(a first cut reported 6 of 6 DIFFERENT entirely on the address)*. ⛔ Goldens are keyed BY id, so there it is content |
| 🔴🔴 **The cluster CANNOT be given the editor's scenario** | `POST /scenario/load` ⇒ `NOT_SUPPORTED_HERE(editor.authoring)` — a cluster loads via the orchestrator's 2PC. ⇒ the design's *"load S in both, then diff"* is not executable; only world-INDEPENDENT structure is comparable *(`HN-029`)* |
| 🔴🔴 **The ack-gate's cluster half is CROSS-LANE** | `MasterSyncController` is private in `OrchestratorSubsystem` *(TIME lane)* ⇒ `hasMaster:false` in the manifest, **asserted by a rail** so it reddens when the TIME lane exposes it *(`HN-028`)* |
| ⚠ **Comparing clocks on a FREE-RUNNING cluster measures harness latency** | 📐 the first lockstep attempt read a ~3-tick gap that was elapsed wall time. ⭐ Pause, then step, then read — CGF and SimHost are then bit-identical |

---

## 0b. ✅ **the regression net, part C** *(`N2`–`N6`)* is DONE *(`2026-08-24`)*

📄 **Design *(and the AS-BUILT — read §7b and §8b FIRST)*:
[`../DESIGN_Regression_Net.md`](../DESIGN_Regression_Net.md)** — now `BUILT`.
📄 **Report: [`batches/REPORT_Regression_Net_Part_C.md`](batches/REPORT_Regression_Net_Part_C.md)**.

⭐ **Ids: `HN-020`/`HN-021`/`HN-022` done; `HN-023`, `HN-024`, `MX-013` open.** ⭐ Next free: `HN-025` / `MX-014`.
⭐ **Suite: `58 → 76`, all green.**

### ⛔⛔ The five facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **A GOLDEN IS CAPTURED ON A FIRST LOAD IN A FRESH PROCESS** | ⛔ `HN-011`: a reload leaves entity `1000` carrying `BlueprintAssignments` ⇒ a golden captured after one **bakes the defect in**. `GoldenCaptureFixture` owns a private editor and loads **once**; ⛔ the shared collection fixture may not be used for captures |
| ⭐⭐⭐ **THE NORMALIZER'S IGNORE-LIST IS EMPTY, AND THAT IS MEASURED** | 📐 Across all 41 dumps a path and a `timestamp` appear in **one** panel *(`fdp_message_log`)* and a `frame` in one more — both already declared-volatile. ⛔ **Never widen it to go green**; a control rail re-derives the claim from the committed goldens |
| ⛔⛔ **A PANEL ID CAN CONTAIN A SLASH** | `editor/_gizmo` — it threw `DirectoryNotFoundException` on the first capture. Encoded `/`→`~`, with an injectivity rail |
| 🔴🔴 **THE SHARED EDITOR CAN HIDE A LIVE DEFECT** | 📐 With `9aa790d57` reverted, the `R-132` assertion **passed in the full suite** and **failed in its own process**. ⇒ ⭐ a falsifiable behaviour claim gets a **fresh process** *(design `Q1` overturned)* |
| ⚠⚠ **THE AUTHORING PERSPECTIVES CAN ONLY BE CAPTURED EMPTY** | 📐 **48 routes; none opens an AI asset** ⇒ 30 of 41 panels are pinned only in their no-asset shape. `MX-013` is the highest-value addition to the harness |

---

## 0c. ✅ **a preview leaves no trace** is DONE *(`HN-017`, `2026-08-24`)*

📄 **Design *(and the AS-BUILT record — read §4d FIRST)*:
[`../DESIGN_Deterministic_Network_Ids.md`](../DESIGN_Deterministic_Network_Ids.md)** — now `BUILT`.
📄 **Report: [`batches/REPORT_Preview_Leaves_No_Trace.md`](batches/REPORT_Preview_Leaves_No_Trace.md)**.

⭐ **Ids: `HN-017` done; `HN-018`, `HN-019` filed open; `HN-012`/`HN-013` closed.** ⭐ Next free: `HN-020`.

### ⛔⛔ The five facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **"what preview saves" lives in `Fdp.Toolkits/Orchestration/Preview/`** | ⛔ **NOT in either handler.** 📐 There are **two** preview handlers *(`HN-016`)* and the design named the **editor-only** one as the "one home" — that would have been exactly the hardwiring the user's steer forbids |
| ⭐⭐⭐ **A pooled allocator's issuing position IS ITS QUEUE** | ⇒ ⛔ `Reset(Read())` was never possible *(`BlockIdManager.Reset` ignores its argument; `DdsIdAllocator.Reset` writes a **global** `Req_Reset`)*. ⭐ All five allocators implement `IRestorableIdAllocator`, and ⛔ **none of them talks to the central authority** |
| ⭐⭐⭐ **The allocator may NEVER be restored without the map** | 📐 `NetworkEntityMap.Register` throws on a duplicate id and the editor never prunes ⇒ exact id repetition makes that throw **certain** on preview 2. ⭐ The drift was the only thing hiding the leak |
| ⭐⭐ **Cluster-wide needed NO new protocol** | both handlers answer `PrepareState(LoadingPreview/UnloadingPreview)`: the master broadcasts, **each node restores its own reservation locally** |
| ⚠⚠ **`Hrot.SimHost.Tests` and `Fdp.Toolkits.Tests` BOTH have rotating order-dependent reds** | 📐 Proved on a **stashed** tree: 4 then 11 failures over two identical runs. ⇒ ⛔⛔ **a full-suite red/green there is not evidence about your change** — isolate. `HN-019`, and `DEBT-AIB-030`'s shape |

---

## 0d. ✅ **the perspective model, Part A** is DONE *(`2026-08-23`)*

📄 **Design: [`../DESIGN_Perspective_Unification.md`](../DESIGN_Perspective_Unification.md) §3** — now
`BUILT`, with per-item **AS-BUILT** notes folded in *(obligation ⑤)*.
📄 **Handoff: [`batches/HANDOFF_Perspective_Model_Part_A.md`](batches/HANDOFF_Perspective_Model_Part_A.md)** ·
**Report: [`batches/REPORT_Perspective_Model_Part_A.md`](batches/REPORT_Perspective_Model_Part_A.md)**.

⭐ **`BP-488`–`BP-497`.** All items landed; ⛔ nothing descoped. ⭐ **Next free id: `BP-498`.**

### ⛔⛔ The four facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **The editor's perspective id is `"Scenario"`** | ⛔ **not `"Editor"`** — `L6.1b` is DONE. ⚠ The **subsystem** is still named `"Editor"`, and so are its node/log names ⇒ 📌 **a perspective is not a subsystem name**, which was this batch's whole lesson |
| ⭐⭐⭐ **`SwitchPerspective` REFUSES an unclaimed perspective** | ⇒ ⛔ **a rail must REGISTER a claiming window BEFORE switching.** 📐 Four existing rails had the order backwards and passed only because no check existed |
| ⭐⭐ **CGF's perspective is `"Scenario"` too, and there is NO `CGF` perspective** | `perspectiveMap["Scenario"] = "CGF"` — the one entry whose key and value differ |
| ⭐⭐ **`FindResultsWindow`'s `owningPerspective` is REQUIRED**, and the scope is a parameter | ⛔ the `?? "Authoring"` default is gone, and the ctor refuses an anonymous `PerspectiveBound` window or a `Global` one that names a perspective |

⚠ **Two things left for the coordinator** *(§6 of the report)*: CLAUDE.md's coordinator-branch row looks
stale, and `Hrot.Presentation/Windows/FdpEntityInspectorHelper.cs` is on no lane's surface list although
`A1` had to touch it.

---

## 1. ✅ HISTORY — `BP-399` *("one shell")* is **DONE**

> ⚠ **This section and §2 are HISTORY as of `2026-08-23`.** ⭐ Read §0 for where the lane actually is.

📄 **The design: [`DESIGN_Details_Panel_View_Switching.md` §7](DESIGN_Details_Panel_View_Switching.md).**
📄 **The dispatch: [`batches/TASKS_One_Shell_BP399.md`](batches/TASKS_One_Shell_BP399.md).**

| # | what | state |
|---|---|---|
| **S0** | measure whether the Diagnostics/Blackboard `L3` rows were already satisfied | ✅ **yes, no code owed** |
| **S1** | Blueprint gets the real shell *(atomic; `BlueprintDetailsWindow` deleted)* | ✅ `BP-428`–`BP-430` |
| **S2** | `details.nodeproperties` on BTree + HSM at Rank 20 | ✅ `BP-431`–`BP-433` |
| **S2b** | the asset-scoped arms leave `InspectorWindow` **as menus, not views** | ✅ `BP-434`–`BP-437` |
| **S3** | `details.utility`, ported honestly as the stub it is | ✅ `BP-438` |
| **S4** | `details.parametersync`, Rank 15 | ✅ `BP-448` — `R-99` **satisfied**, not waived |
| **S5** | retire `InspectorWindow` | ✅ `BP-449`, `BP-450` — the class is **deleted** |

⛔ **`InspectorWindow` no longer exists.** All six arms are Details views or asset-row menu items.

### ⚠ Two corrections this lane made to its own claims — **do not re-introduce either**

| ⛔ the wrong claim | ⭐ the truth |
|---|---|
| *"`S5` is blocked on `S3` alone"* *(`S2b` report)* | **`BP-439`** — it was **`S4`**; §7.6's ④-before-⑤ order was right. ⚠ The **mirror error**: cleared one blocker, inferred the remainder instead of re-reading the sequence |
| *"`ai_inspector_*` is in no layout file"* *(`S5`)* | **`BP-450`** — ⛔ **FALSE.** My grep used `--include=*.cs`, excluding the very file types a layout lives in. `BP-103b`'s stale-layout rail caught it. ⚠ **An absence claim from grep is an absence in your PATTERN** |
| *"arms ① and ⑥ need a home in the Details panel"* *(`BP-431`)* | 🔒 The user routed all three **OUT**: collisions → Diagnostics, Rename…/Find References → the Asset Browser row menu, Go to Definition → **deleted**. §7.4a |

---

## 2. ⭐⭐⭐ THE NEXT TASK — **[`batches/HANDOFF_Panel_Observability.md`](batches/HANDOFF_Panel_Observability.md)**

> 🔒 **The user's instruction, `2026-08-22`:** *"then your task will be `HANDOFF_Panel_Observability.md`."*

⛔⛔ **READ THE DESIGN FIRST: [`../DESIGN_UI_Observability_Snapshot.md`](../DESIGN_UI_Observability_Snapshot.md)**,
whole — its **§UML** is the contract, and **§Invariant** *(the draw renders ONLY from the VM)* is the
load-bearing rule. ⭐ Umbrella context: [`../DESIGN_Headless_Testability.md`](../DESIGN_Headless_Testability.md).

| phase | what | how |
|---|---|---|
| **1 — `U-obs-1`** | `IPanelViewModel` + `PanelSnapshot` + the opt-in registry + **ONE pilot panel** end-to-end + a stable panel id | ⛔⛔ **HANDS-ON, do NOT fan out** — it is the pattern every later conversion mirrors. ⭐ Then **push a green checkpoint**: it unblocks the time lane's Group T |
| **2 — `U-obs-2+`** | the per-panel fan-out *(Details/blackboard/watch first, then the gizmo peer feed, then value-ordered)* | ⭐ **SONNET subagents**, Opus reviews the real diff and re-runs each panel's gates. ⛔ Review gate: **the INVARIANT** — any drawn value not from the VM is a defect |

⭐ **New tracker area `K` — Panel observability.** ⭐ Dispatch sha `5843055e7`; **scope frozen there.**
⭐ **Run freely — wait for nothing.**

### ⚠ Before writing code

1. **rule 7** — already done this session *(merged `2d95c419`)*; re-merge if the coordinator moves again.
2. **rule 1b** — push an empty `chore: started <batch> at <sha>` marker **immediately**, before any code.
3. **`U1a`'s open call:** the handoff *leans* to homing the contract in `Fdp.Diagnostics.Contracts`
   *(beside `DebugPrimitiveBuffer`)* — ⭐ **confirm the assembly by measurement and say so in the report.**

---

## 3. ⭐ THE STANDING PROTOCOL — **what this lane does every batch**

| | |
|---|---|
| ⭐⭐ **design before code** | `R-129`: intent is in `docs/` *(current)* and `.dev/<programme>/*-DESIGN.md` *(implemented)*, **never** in the code. Cite **doc + section** per item |
| ⭐⭐ **INVENTORY before design** | `search_graph` **first** — grep can only confirm a guess, never enumerate. Record the query + its `total` |
| ⭐⭐⭐ **revert-goes-red per item** | ⛔ un-apply with the **inverse edit**, ⛔⛔ **NEVER `git checkout --`** |
| ⭐ **tiers** | `T0` `scripts/quick-check.sh <csproj> [filter]` while working; the **full gate table ONCE, at the end** |
| ⭐⭐ **the gate report substitutes for the coordinator's run** | 8-row contract: per-gate command + counts + delta · a `--no-build` column · goldens as a **diff shape** · every red **confirmed pre-existing against the base sha** · clean tree · both quarantine counts · `tracker-counts.py --check` + ids allocated · the **integration suite** for a cross-cutting change *(or why it cannot gate)* |
| ⭐⭐⭐ **obligation ⑤** | a deviation goes **back into the owning DESIGN doc**, prior state marked SUPERSEDED — ⛔ the report is ephemeral, the design is not |
| ⭐ **I allocate the ids** | state them in the report. **Next free: `BP-498`** *(⚠ was `BP-453`; `BP-453`–`BP-497` are spent)* |
| ⛔ **no PR** unless the user asks | there has never been one in this programme |
| ⭐ **links for mobile** | `https://github.com/pjanec/HROT/blob/claude/reset-working-branch-qd1qpv/<path>` — ⚠ **push first** |
| ⛔ **plain-text questions** | never the multiple-choice widget |

### ⚠ Known pre-existing — **do not re-diagnose**

| | |
|---|---|
| `StructEdit.Tests` **1 red** | `DocumentBuilderTests.Build_CircularReference_…` — confirmed in a clean worktree at `5d1fd44d` |
| `Fdp.Presentation.Tests` | ⛔ cannot run whole *(`BP-419`, test-host crash)* — gate by `--filter` |
| `ClusterRunner.Integration.Tests` | ⛔ un-gateable, pre-existing DDS-allocator crash *(Batch 101)* |
| `Fdp.Toolkits.Tests` | ⚠ `DEBT-AIB-030` — the failing identity **rotates**; neither red nor green is evidence |
| `rulings-check.py` | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` — pre-existing |
| ⭐ `design-digest.py --check` | ✅ **now fully green** — the coordinator's `8ad6d6aa` cleared the four long-standing failures |
| ⭐ `Hrot.ClusterRunner.Tests` **2 red** | `DataDrivenGizmoPredicateTests.D003_*` — `InvalidCastException` casting a test double to `DebugPrimitiveBuffer` at `DataDrivenGizmoSystem.cs:314`. ⛔ Confirmed pre-existing in a clean worktree at `c6f54318c` |
| ⚠ `Hrot.Editor.Tests` **1 FLAKY** | `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` — asserts an `AssemblyLoadContext` was GC-collected. 📐 Green 2 of 3 whole-suite runs, 3 of 3 filtered ⇒ ⛔ **neither colour is evidence** |
| ⚠ a merge that adds a project | **`dotnet restore` first** — `Hrot.SystemTests` arrived this way and `--no-restore` failed on it |

---

## 4. ⭐ CARRIED OPEN

### ⭐ `S4` / `BP-399`'s tail — **both blockers are now CLOSED; one design call remains**

📄 **[`Architect_Question_49_…`](Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md)** —
written by the coordinator, **amended by this lane `2026-08-22`**:

| | |
|---|---|
| ✅ **`Q49` option C is BUILT** *(`BP-440`–`BP-442`)* | the identity is recomputed from the catalog resolver already wired at `PerspectiveWorkspaceRegistrar:289`, through ONE derivation *(`SubtreeSyncIdentity`)*, pulled from inside `Emit` so no path can forget *(`R-126`)* |
| ✅ **`Q50` option A + `Q49` option D are BUILT** *(`BP-444`–`BP-447`)* | 🔒 user: *"i hoped the editor automatically adds the subtree's data."* ⭐⭐ **A and D turned out to be the SAME change**: every input is persisted, so it is a **generator-side projection over a document** — no editor, no ordering. `SubtreeSyncProjection` does one walk yielding both the groups and the slice fields, so *"a group without its field"* is unrepresentable |
| ⛔⛔⛔ **RE-MEASURED `2026-08-22` — `BP-446`'s LIMIT WAS DESCRIBED WRONG. 📄 Read [`Q50`](Architect_Question_50_The_Master_Blackboard_Declares_The_Subtree_Slice.md) *"THE LIMIT — re-measured"* BEFORE reasoning about this area** | ⛔ *(was: "a generated Category-2 callee blackboard does not exist in the master's compilation")* — 📐 **all 15 managed assets declare `BrainBlackboard`, an ordinary resolvable type; that skip never fires.** ⭐⭐ **The real wall is the BYTE BUDGET** — 128 bytes vs a 100-byte inline budget ⇒ **"declare the slice as a field" can never hold a Category-2 callee.** ⚠ Architectural, ⛔ not a missing helper |
| 🔴 **THE REACH — the honest state of `S4`** | the panel can only author against a **Category-2** callee *(it needs `BlackboardVariables`)*, and the generator skips **every** Category-2 callee ⇒ ⛔ **the authorable and emittable sets are DISJOINT today.** ⭐ The panel is real and writes real persisted data; ⛔ no authorable binding reaches the runtime yet |
| ✅ **POSTPONED BY THE USER, ON THE RECORD** *(`BP-452`)* | 🔒 *"is that safely postponable, providing you record it thoroughly as such?"* ⇒ ⭐ **yes**: every failure is a **build-time skip**, never a partial emit or a bad runtime copy; 📐 **no corpus asset has a sync binding.** ⭐ Three routes with a lean *(`C′` — declare the slice `Role = State`, reusing the partition tier that already escapes the budget)* — ⚠ it moves the emitted body, so it wants a nod |
| ⛔ **ONE REAL DEFECT, not postponable indefinitely** *(`BP-451`)* | **nothing validates a binding's `FieldName` against the callee's type** ⇒ the generator **can emit CS1061** — 📌 `BP-306` re-armed. ⭐ Unreachable through the UI, reachable by hand-edited JSON. 🛠 Small, needs no design call ⇒ **do it in the next batch touching this generator** |
| ⚠ also required | the **master** blackboard must be `Managed`; a Category-1 master cannot gain a field *(the one claim of `BP-446` that survived)* |
| ⚠ still awaiting a nod | `Q49`'s open sub-question — a **MISSING** subtree at load. ⭐ Recommended: a diagnostic row. Built behaviour: identity left alone, never erased |

### ⚠ Other open `BP-` rows

`BP-405` · `BP-407` · `BP-411` · `BP-416` · `BP-418` · `BP-419` · `BP-426` *(needs a running editor)* ·
`BP-427` · `BP-342` · `BP-399` · ⭐ **`BP-451`** *(a real defect — see above)* · ⭐ **`BP-452`**
*(postponed on the record)*. ⭐ Tracker: **open 92 / done 295**.

### ⭐ The other lanes — **do not touch their files**

| lane | branch | owns |
|---|---|---|
| **coordinator** | ⚠ **`claude/blueprint-authoring-status-6sr5ld`** is where the live handoffs and designs are being pushed *(`2026-08-23`)*; CLAUDE.md's table still says `…-gm0akp` — ⭐ **confirm by ancestry, not by name** | handoffs, designs, the ledger |
| ⭐ **backend** *(new `2026-08-23`)* | `claude/blueprint-macro-feature-sdmspn` | project/reference structure · the Stride cleanup. ids **`ST-`**, tracker **Area I only**. ⚠ **Our only shared file is `Program.cs`** |
| **time / MCP** | see `RESUME_Time_Stride_Session.md` | `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` · the MCP harness. ids **`TM-`**, tracker **Area H only** |

⛔ **A cross-lane edit is a STOP-and-report, not a judgement call.**


---

## ⛔ HISTORY — older STATUS strands *(moved out of the STATUS block `2026-09-20`)*

⚠ **Moved verbatim, nothing deleted.** ⛔ **Do not quote any of it as current state** — it is the
accumulated `current-answer` text of earlier programmes, newest first. The live answer is the
`SESSION 2026-09-19/20` block at the TOP of this file.

```text
  ══ (older) STRAND 0 — ROLE-AFFINITY OWNERSHIP (P3) + THE TKB COMPONENT-SET DERIVATION ══
  ══ STRAND 0 — ROLE-AFFINITY OWNERSHIP (P3) + THE TKB COMPONENT-SET DERIVATION. The live work. ══
  BRANCH claude/reset-working-branch-qd1qpv, head = the commit carrying this doc.
  Tree clean. Gates: design-digest --check clean · rulings 34/34 · tracker-counts OK.

  ⭐⭐⭐ READ FIRST, in this order:
    1. docs/DESIGN_Role_Affinity_Ownership.md — its STATUS current-answer names §3.9c (the tables are
       COMPLEMENTS) and §6i (step 4's as-built). ⛔ AND §3.6, which was RETRACTED this session.
    2. docs/designs/tkb-1/DESIGN.md §6.6 / §6.6a — the whole TKB component-set design and its as-built.

  ✅✅✅ SHIPPED 2026-09-13
  CE-264  9943c6c9c  P3 STEP 4 — CGF holds a Brain policy, SimHost a Muscle policy, and gateOnAuthority
                     comes ON in the same change (§6f's ordering hazard closed by construction).
                     NEW Hrot.Core/HrotRoleComponentSets.cs — the cluster role tables, authored ONCE;
                     CreatePolicy(NodeRole) takes a role and NOTHING else. 10 rails, 4 red-proofs.
                     ⭐ P3 went from "a mechanism every node ran with a null policy" to LIVE.
  CE-259az 603a05248 file-loaded TKB templates had an EMPTY BirthCriticalComponents. Fixed via an
                     app-layer convention — ⚠ then SUPERSEDED the same day by CE-266, see below.
  CE-266  c101e2cc2  [BirthCritical] + [PerInstanceValue] in Fdp.Core, and
                     TkbTemplate.BirthCriticalComponents becomes a DERIVED READ-ONLY view.
                     Deleted 8 authoring sites, TkbComponentConventions + its loader call, and
                     HrotRoleComponentSets' hand-written SimTransform (three producers of one fact).
                     6 new rails, 2 red-proofs — removing [BirthCritical] from SimTransform reddens
                     9 rails across 4 files.

  CE-265  74438cb7f  THE MANDATORY HALF SHIPPED. The promotion gate is DERIVED PER HOST and the file
                     path has one for the first time. ITkbEntityTranslator gains GetProducedComponents()
                     (no default impl — all 9 production translators, compiler-enumerated);
                     NEW MandatoryComponentResolver does the four-way intersection; GhostPromotionSystem
                     gates on derived ∪ explicit and gained a STALL DIAGNOSTIC (600 frames, once per
                     (TkbType, component) — ⛔ not a timeout, the ghost still waits).
                     Deleted DefineVehicle's 2 AddMandatoryComponent calls + AsComposite's EntityInfo
                     check. 13 rails (10 new + 3 into GhostProtocolTests), 5 red-proofs.
                     📄 tkb-1/DESIGN.md §6.6b carries the as-built AND the class + sequence UML.

  ⛔⛔ THREE THINGS CE-265 MEASURED THAT THIS DOC PREVIOUSLY GOT WRONG — do not re-derive them:
  · "DescriptorOwnershipMap needs a UNION accessor" — FALSE. CoveredComponentIds (:173) already was it.
    The seam law, 25th instance. I wrote that line myself from the class's summary without opening it.
  · DIRECTION MUST NOT BE FILTERED on the ingress set. ZERO translators under NED's Replication/Map/
    Ingress/ declare TargetComponentIds — only the EGRESS side overrides the interface default. An
    "ingress-only" filter yields ∅ and collapses the derivation; GeoSpatialEgressTranslator._targetIds
    is the ONLY pairing SimTransform has.
  · §6.6a's "fill both at ITkbDatabase.Register" is NOT BUILDABLE for mandatory. TkbDatabase.cs:20-32 is
    two dictionary writes with zero host context, and 3 of the 4 inputs are host-local. A TkbTemplate is
    a SHARED record (one object serves CGF, SimHost, IG, editor) ⇒ a host-local answer has no home on
    it. The cache lives with the CONSUMER. Same fact as "a TKB file may not carry it", one level down.

  ⭐⭐⭐ NEXT: nothing in this strand is in flight. The nearest open items are P3 step 3c (the boot
    warning) and CE-259bk/bl (role-derived registration, logic packs). ⚠ CE-259bm's premise is STALE:
    EnforceExplicitComponentIds is read by nothing and ComponentType.cs throws unconditionally.
    ⚠ Still open from CE-265's original row, on its own merits: the promote-leg role claim is ONE-SHOT
    (GhostPromotionSystem fires it at promotion, then the entity leaves the query), so components
    arriving afterwards are never claimed. A real gate makes that far less likely to bite; it does not
    remove it.

  ⛔⛔ FIVE THINGS THAT WILL BE RE-DERIVED WRONG IF NOT READ FIRST
  · THE AUTHORITY MASK IS READ BY NO EGRESS TRANSLATOR (§3.6, re-measured). Every production egress calls
    the ISimulationView EXTENSION, which reads DescriptorOwnership/NetworkAuthority; the overload
    resolution hides it (packedKey is a long). The mask's WHOLE production readership is SimTransform,
    BehaviorState, BrainBlackboard, plus a Position query matching ZERO HROT entities. ⇒ narrowing the
    mask changes what a node EXECUTES, never what it PUBLISHES.
  · THE ROLE TABLES ARE COMPLEMENTS (§3.9c). Brain = ALL − birthCritical; Muscle = that − brainOnly. An
    enumerated set is a WHITELIST and reproduces CE-256. EveryUnclassifiedComponentStaysOwnedByBothRoles
    is the rail a future positive enumeration must argue with.
  · NO ROLE MAY OWN A BIRTH-CRITICAL COMPONENT, on EITHER leg. GeoSpatialIngressTranslator.cs:90 applies
    an incoming position only when HasAuthority<SimTransform> is FALSE ⇒ a promoting node that claimed it
    by role freezes every ghost on that node.
  · SimTransform PRESENCE IS THE "is this spatial?" PREDICATE — 42 production .With<SimTransform>()
    filters across 37 files. ⛔ Do NOT make it universal (the Unity-Transform idea): a positionless entity
    would enter the spatial hash at the origin and become a perception/EQS/ballistics candidate. Global
    data belongs in SetSingleton, which is not an entity at all.
  · MANDATORY REQUIREMENTS ARE HOST-DEPENDENT. GhostPromotionSystem.cs:211 has NO registration guard, so a
    HARD requirement for a component the local host never registers aborts promotion every frame forever,
    silently. ⇒ a shared TKB FILE may never carry them.

  ⭐⭐ USER RULINGS THIS SESSION — do not re-litigate
  · "the components the entity have makes the entity a vehicle" → ⛔ NO per-entity-class vocabulary
    anywhere. That killed a TkbMasterDto⇒vehicle-bundle proposal. DefineVehicle and friends are AUTHORING
    helpers for the default catalogue only; the file path needs none of them.
  · "we can add TKB record override any time later … the tkb in-memory record can be just a readonly
    cache" → the component ATTRIBUTE is the source of truth; the per-type override is DEFERRED, not
    rejected, and TkbTemplate's property is the seam it lands on.
  · "i do not want to wait 10 frames by design" → ⛔ SOFT-by-design REJECTED. SoftTimeoutFrames is an
    EMERGENCY escape only. Requirements are HARD and the derivation must be EXACT.
  · "sending state on change is what actually happens … almost nothing is sent unconditionally" → the
    addition rule for [PerInstanceValue] is GUARANTEED BASELINE for a newly spawned entity, ⛔ not
    "unconditional egress".
  · "PerInstanceValue" → the attribute's name, chosen by the user over my alternatives.
  · "ghosting is a deployment property, not content" → UrbanCombat is just a dev/test scenario; whether
    its entities arrive as ghosts is NOT a property of the content, so the catalogue divergence is DRIFT.

  ⚠⚠ MY FAILURE MODE THIS SESSION, AND IT REPEATED SEVEN TIMES: I PROPOSED A MECHANISM BEFORE MEASURING
  WHETHER THE EXACT ANSWER WAS ALREADY AVAILABLE. Each was caught by the user, not by me:
  · "derive birth-critical from TkbMasterDto" — misses area/route, which carry no descriptors yet still
    need the birthright.
  · "mandatory = what translators produce" — backwards; producing it is the reason you need NOT wait.
  · "mandatory = produced ∧ the !HasComponent guard" — the guard is IDEMPOTENCY and sits on ~25 of ~30
    produced components. It selects nearly everything.
  · "the catalogues disagree ⇒ per-template policy was intended" — an INFERENCE stated as a measurement.
  · "the egress must be unconditional" — change-driven egress is the norm AND the point.
  · "unify the catalogues and it becomes derivable" — unifying makes it UNIFORM; uniform is what makes a
    CONSTANT safe. Different thing.
  · relayed-architect claim "translators derive mandatory at load time" — VERIFIED FALSE against source
    (ITkbEntityTranslator has two members and Inject takes an ENTITY). ⭐ But its CONCLUSION was right for
    a reason it never stated, which is how the host-dependence fact was found.
  ⇒ 🔒 AND THE TOOL HALF: I used grep for enumeration claims most of this session and only re-ran them
  through search_graph when challenged. They held — that was luck, not method. GRAPH FIRST for any
  complete-set or absence claim; CLAUDE.md says so and it was not followed.

  ══ STRAND 1 — MAP INTERACTION / SELECTION / TOOLS (the live one as of 2026-09-10) ══
  ✅✅✅ READ docs/SNAPSHOT_Map_Interaction_Architecture.md FIRST. It is a SNAPSHOT, not an owning
  design: §1 the block map, §2 the data flows, §3 remote map control, §4 the FINDINGS LEDGER,
  §5 the eight user rulings of 2026-09-10 + the TWO OWNING DESIGNS THAT DO NOT EXIST YET,
  §6 rot owed to other documents. Then its owners: UX_Feature_Tool_Model.md §4.7h–§4.14 and
  UX_Feature_Selection.md §2.6.
  ✅ §4.7i IS BUILT AND OPERATOR-CONFIRMED 2026-09-10 ("the handles disappeared when picker activated").
  ⚠⚠ CE-259r WAS RE-FIXED THE SAME DAY — the first attempt did NOT work and the operator run found it.
  The production picker is on ToolArbiter.Global (PickerToolHost.cs:116), so its hover is dispatched by
  GLOBALMANAGER, the FIRST group member — not by dataDriven. Swapping stateless past dataDriven alone
  left globalManager ahead of it. The group is now stateless, globalManager, dataDriven, selfCheck.
  ⛔ TWO PROCESS FAILURES WORTH NOT REPEATING: my headless probe put the picker in dataDriven, measuring
  a scenario that does not exist in production (the same "mirror the ARBITERS, not just the gesture"
  mistake CE-259q already recorded); and the new rail asserted only stateless<dataDriven so it stayed
  GREEN over the live defect. The rail is fixed IN PLACE to require stateless before EVERY arbiter, and
  red-proofed against the order that shipped this morning.
  ⛔ CE-259r IS STILL NOT VERIFIED IN THE PRODUCT — the next operator run closes it.
  ✅✅✅ THE ANCHOR-IDENTITY REFACTOR IS BUILT AND PUSHED — S0..S7 on 2026-09-10, and §6.7 on
  2026-09-11 (../DESIGN_Gizmo_Anchor_Identity.md).
  ⭐⭐⭐ READ ITS **§6.7** FIRST — it SUPERSEDES §6.6 and is the current answer. Then §6.2 "AS-BUILT",
  which OVERRIDES §5's UML and §6's table (three steps deviated while building).
  ⭐ IN ONE LINE: a gizmo anchor is identified by its NETWORK id everywhere, END TO END, and the ECS
  handle is GONE — from GizmoPickToken, from PickToken (now `long AnchorId`), from the terminal's
  hit-test (`PickTopmostAnchorId → long?`) and from every primitive producer. Each consumer resolves
  the id IN ITS OWN WORLD via NetworkIdResolver.ResolveNetworkId (map-first, and the map hit is
  VERIFIED against the entity's NetworkIdentity, so a stale map degrades to slow, never to wrong).
  ⚠⚠ AN EARLIER VERSION OF THIS BLOCK SAID the ECS handle "survives as a declared IN-PROCESS PAYLOAD
  on GizmoPickToken (AnchorIndex + StreamId)". THAT IS SUPERSEDED — the user refuted the reasoning
  ("replaybrowser is ecs module like any else. i do not want such exceptions") and it was measured
  wrong: the NetworkEntityMap is a WORLD SINGLETON, not a per-host delegate, so the SILENT-DEFAULT
  argument never applied. ReplayBrowser now maintains one like every other ECS module.
  🔴 A LIVE DEFECT FELL OUT: DataDrivenGizmoSystem routed MenuAction/StructUpdate through
  FindGizmoByIndex((int)evt.AnchorId,…) — a network id narrowed to an int and compared to Entity.Index,
  i.e. defect D1 in the one place S0/S5 never swept. Fixed; FindGizmoByIndex deleted. GlobalGizmoManager
  had already keyed those events by the id ⇒ the seam law, 5th measured instance.
  ⭐⭐⭐ THE PROCESS LESSON WORTH KEEPING: T-1 caught a regression in my OWN change after I had already
  written a confident design paragraph saying the opposite. I removed the ingress DROP ("the consumer
  decides"); SC-GZ037-4 and ANetworkIdThisNodeDoesNotKnowYieldsNoEvent reddened and were RIGHT — DDS is
  broadcast and Recipient routes to the FOCUS HOLDER FIRST (R-144), so a foreign drag would have fed one
  operator's gesture into another operator's active tool. Both behaviours kept, and better: the drop now
  asks "is this id in my WORLD" (any node can answer) instead of "is it in my MAP" (which is what made a
  mapless node silently drop everything).
  ⛔ NEW, FILED, NOT FIXED: CE-259ah — DrawEntityBadge writes an ECS handle nothing reads into offsets
  24/28 while the renderer takes the badge position from BoxCenterX/Y, which nothing writes ⇒
  HealthBarGizmo badges render at the world origin. Same family, different offsets, needs a placement
  decision. ⭐ Closed: CE-259ag.
  ✅ CLOSED BY IT: CE-259x (the exclusive-filter leak — but by DELETING S0's fix and removing the CAUSE,
  a disjoint tool-id range, §6.1) and CE-259h (the pick token's network-stable contract).
  🔴 NEW, FILED, NOT FIXED: CE-259z — an EntityLocal primitive's SpatialAnchor key is truncated to 32
  bits, so an id above int.MaxValue makes the shape VANISH with no error. Cannot be widened (the payload
  union is full and 64 bytes is a DDS invariant) ⇒ it is constraint C7; asserted + railed, open question
  is whether to ENFORCE that no tool emits EntityLocal rather than rely on it.
  🔴🔴 NEW, FILED, BLOCKS GATING: CE-259aa — Fdp.Presentation.Tests aborts with a native SIGSEGV
  (exit 139, reproduced outside the test host) inside DebugGizmoLayerHitTests, so ~95 of its ~185 tests
  have NEVER RUN. That is the real cause of CE-088's "54 of 185 discovered". PROVEN pre-existing at
  740b522c with all work stashed. ⛔ Do NOT quarantine (R-131) — the crash is hiding the other tests.
  Interim gate: --filter the class you need.
  📐 CE-259y CORRECTED: it is SEVEN reds, not two (CE-259aa was hiding five), and its dependency on S7
  is WITHDRAWN — S7 could not collapse AnchorIndex, so that row is now just "the wrapper must forward
  its ISimulationView".
  ✅ ALSO FIXED 2026-09-10: CE-259ab — DataDrivenGizmoSystem hard-cast its IDebugDrawBuilder seam to
  DebugPrimitiveBuffer at 4 sites, so DataDrivenGizmoPredicateTests.D003_* threw InvalidCastException
  inside Execute and had NEVER RUN. Pre-existing (identical at da2d360d). Fixed BOTH halves: the system
  degrades-and-asserts, and the hand-rolled D003NoOpDrawBuilder is retired for a real buffer — degrading
  alone would have made a red rail GREEN AND BLIND.
  ⚠⚠ HOW IT WAS INVISIBLE ALL SESSION, worth remembering on a fresh VM: 68 projects had never been
  restored, so every suite in them reported NETSDK1004 and was skipped. `dotnet restore <sln>` once, then
  a full-solution build, is what surfaced them. ⛔ NETSDK1004 is not a code break — do not treat it as one,
  and do not treat "the suite did not run" as "the suite is green".
  📐 KNOWN PRE-EXISTING REDS, all reproduced at da2d360d so none is this lane's: Hrot.IG.Tests 5
  (EntityInfoTranslator ×4, EntityMasterTranslator ×1) · Hrot.ClusterRunner.Tests 3
  (OrchestratorSubsystemTests) · Hrot.Editor.Tests 1 (AiHotReloadCoordinator ALC collection) ·
  Hrot.Presentation.Tests rotates 0–1 (CE-084) · Fdp.Toolkits.Tests rotates 0–2 (DEBT-AIB-030).
  ✅✅✅ CE-259aa IS CLOSED, 2026-09-10, and it was bigger than the row said. NEW OWNING DESIGN:
  ../DESIGN_Gizmo_Renderer_Seam.md — READ ITS §6.1 (as-built), it is where the findings are.
  📐 Fdp.Presentation.Tests now reports 544 tests (535 pass / 8 fail / 1 skip). It reported ~90 and
  then died, so ~450 had NEVER EXECUTED. CE-088's "54 of 185" is explained and subsumed: same crash,
  not a discovery bug. ⛔ THE LESSON: "the suite did not run" is not "the suite is green", and a
  native crash makes those indistinguishable from the summary line.
  ⭐ Root cause: the Fdp.Presentation renderer WRAPPER had invented a SECOND DispatchShape hook firing
  before the real renderer, so test doubles captured unfiltered primitives AND left Raylib drawing.
  The real seam was one layer down all along (GizmoMap.Presentation .cs:193-195) and
  GizmoMap.Presentation.Tests had always used it, 41/41 headless. Fourth measured instance of the
  seam law (CE-259p, SC-GZ067-1, CE-259ab, this).
  🔴 CE-259y IS REFUTED, not fixed-as-filed. The dropped ISimulationView is the DESIGNED END STATE:
  feedback2.md:798 "completely severs the presentation layer's reliance on the heavy simulation ECS",
  :871 "Eradicating Entity", and the SpatialAnchor mechanism IS built with production producers
  (EntityPresentationGizmo.cs:95). The 7 reds were rails for the RETIRED ECS mechanism, now rewritten.
  🔒 The unread parameter is what manufactured the false finding — a parameter nobody reads is a claim
  nobody checks. Deleted from the wrapper AND the layer; the 4 hosts stopped passing a world.
  🔴 NEW, FILED, NOT FIXED: CE-259ac — a gizmo Line is UNPICKABLE. The live hit-test serves Box2D and
  Sphere only; line hit-testing was lost in the terminal migration and its only record was a rail that
  could not run. R-137. NOT re-implemented on a rail's say-so (a UX decision, no design record asks
  for it) but PINNED by SC-GZ026-2b so it cannot be lost twice.
  ⚠ TWO HARD-CODED TEST HOOKS DELETED: TestHook_IsCaptureActive/IsInteractionActive were `=> false`,
  so every Assert.True could never pass and every Assert.False was vacuous. The real state is private
  one assembly down behind a Raylib-polling HandleInput ⇒ not railable headlessly, and now SAID so.
  ⭐ NEW REUSABLE CONTROL: [RequiresDisplayFact] (FDP/Engine/Fdp.Presentation.Tests/Raylib/). Use it for
  ANY test that opens a window or issues a Raylib draw call — a skip costs one summary line, a crash
  costs every test after it.
  📐 The 8 remaining Fdp.Presentation.Tests reds (EntityInspectorPanel, EventBrowserPanel,
  PerspectiveMenu, WindowManagerMainToolbar) are NOT gizmo-related and are PROVEN pre-existing
  (identical 8 with all work stashed). They had simply never run — they belong to their own owners.
  ✅✅ CE-259z CLOSED 2026-09-10, and its "open policy question" hid a THIRD live instance of the
  same family: DrawEntityLocal / DrawEntityLocalInteractive wrote an ECS index into offset 8, which the
  renderer probes as a NETWORK id against the SpatialAnchor cache ⇒ the lookup missed and the primitive
  was SILENTLY SKIPPED, drawn nowhere. feedback2.md:871 had specified the fix ("DrawEntityLocal will now
  accept `long anchorNetworkId`") and it was never built. ROUTED not deleted; measured ZERO production
  callers, so it was a trap for the next author, not an outage.
  ⭐⭐⭐ AND THE ATTEMPT TO FINISH IT PROPERLY FOUND SOMETHING BETTER: I tried to also stamp the S5
  identity in BoxAnchorId and the rail read back 0. For a Line, BoxAnchorId (long @44-51) OVERLAPS
  LineEnd.Z (@44-47) and EndColor (@48-51). ⇒ A LINE HAS NO SLOT FOR AN IDENTITY. That is structural,
  and it means CE-259ac CANNOT be closed by teaching the hit-test about lines — the primitive could not
  carry what it routes on. LEAN recorded on that row: give a gizmo that wants a clickable line a
  co-located invisible Box2D/Sphere, which VertexEditGizmo and RouteWaypointGizmo already do.
  🔒 Both facts are PROVED by rails, not asserted: the second one writes the identity and watches the
  geometry die. Its first version asserted the opposite and failed — which is how the fact was found.
  ✅✅✅ CE-259ac FIXED 2026-09-11 — LINES ARE CLICKABLE. 🔒 User: "what is the issue with clickability
  of something as simple as a line? I do not want to accept it." ⭐⭐ THEY WERE RIGHT AND THIS IS THE
  MOST INSTRUCTIVE MISS OF THE WHOLE PROGRAMME: my layout fact was TRUE ("a Line cannot host
  BoxAnchorId" — LineEnd@36-47 + EndColor@48-51 vs BoxAnchorId@44-51) and I attached the WRONG
  CONCLUSION to it. It argues against storing the identity INSIDE a Line — not against clickable
  lines. ⛔ I generalised from ONE blocked route to "the capability is unavailable" without measuring
  the others. That is R-139's failure mode with a measurement attached: a true file:line does not make
  the inference from it true.
  📐 Measuring the other routes found a REAL BUG, not a missing feature: the renderer has ALWAYS drawn
  Box2D rotated (Renderer2D.cs:296 DrawRectanglePro, and :139 composes the anchor yaw) while the
  hit-test compared AXIS-ALIGNED extents ⇒ a rotated box DREW ROTATED AND PICKED AXIS-ALIGNED. Latent
  only because no production gizmo had set a non-zero angle yet.
  ⭐ THE FIX, with NO contract change: an oriented-box hit-test (reduces exactly to the old compare at
  angle 0) + DebugPrimitive.MakePickSegment(from, to, networkId, pickThickness, subElementId). A
  clickable line IS a thin oriented box, and Box2D already carries centre, extents, angle, a 64-bit
  BoxAnchorId and SubElementId@52 for "which segment". It is EmitPickBox's transparent-pick-target
  pattern generalised from a point to a segment.
  ⛔ Widening Line itself was checked and IS out: Stride/…/DebugPrimitiveRenderer3D.cs:222-223 needs
  the full Vector3. That is the only part of my original analysis that survives.
  ⚠⚠ AND THE RED-PROOF CAUGHT A FLAW IN MY OWN RAIL, worth remembering: my first rail probed the
  diagonal's MIDPOINT, which hits with or without the rotation — the inverse edit stayed GREEN and
  exposed it. It also corrected my description of the bug: an un-rotated segment box is NOT "the
  bounding square" — extents are (length/2, thickness/2), so ignoring the angle leaves a long thin
  corridor ALONG THE X AXIS through the midpoint whatever direction the segment runs. Rails now probe
  off-centre; the red-proof reddens 2 of 11.
  🔴 STILL OPEN, and it is a UX CALL not work: no gizmo is wired to USE clickable lines yet. Searched
  docs/ and .dev/ — NO record says what clicking an edge should DO (insert a vertex at that point?
  select the segment?). The mechanism is delivered and railed; the gesture semantics need one decision.
  🔴 ALSO FILED, latent: CE-259ad — the hit-test does not resolve EntityLocal coordinates (reads
  BoxCenterX/Y raw), so an EntityLocal pick target would draw in the right place and click in the wrong
  one. Same draw-vs-pick family. NOT live: every pick target is World-space today.
  ✅✅✅ CE-259ae DONE 2026-09-11 — POLYGON AREAS AND ROUTES ARE SELECTABLE BY CLICKING THEIR LINES,
  AND RIGHT-CLICKABLE FOR THEIR CONTEXT MENU. 🔒 User requirement, verbatim: "polygon areas and routes
  entities should be selectable by clicking on their lines, also context menu by right clicking them."
  ⭐⭐⭐ THE FINDING THAT MADE IT SMALL: both halves were ALREADY BUILT and merely UNREACHABLE.
  SelectionInteractionSystem selects Token.Target with no GizmoTypeId filter; and
  ContextMenuProjectorGizmo ALREADY emits MenuJsonArea (EditablePolyline) and MenuJsonRoute (RoutePlan)
  bound by network id, with the terminal resolving the menu from the hit primitive's BoxAnchorId.
  ⇒ nothing was missing but a PICK TARGET on the lines (both gizmos emitted Line only, and the
  hit-test serves Box2D/Sphere). One shared EntityPresentationGizmoShared.EmitPickSegments, two call
  sites, done. ⭐ Measure the seams before designing — that is what turned a feature into a helper.
  ⛔⛔ SubElementId MUST be 0, and this was measured not guessed: an EditablePolyline entity may have a
  VertexEditGizmo INJECTED, and FindGizmo gives injected gizmos STRICT PRIORITY ignoring GizmoTypeId
  ⇒ a non-zero sub-element would make a click on edge i start DRAGGING VERTEX i. With 0 it falls
  through that gizmo's idx<0 guard, and 0 already means "the whole entity".
  ⚠⚠ A Z-ORDER WORRY I HAD BACKWARDS, worth remembering: I believed these would steal an active tool's
  handles because the stateless group emits FIRST. The hit-test walks the buffer in REVERSE, so a
  layer-0 tie goes to the LAST-emitted primitive ⇒ the arbiter groups still win and CE-259r/§4.7i
  holds unchanged. Reading the loop direction stopped me "fixing" a non-problem.
  ⚠ NOT done deliberately: the menu CONTENT is unchanged, and clicking an edge does nothing
  gizmo-specific yet (insert a vertex there?) — searched docs/ and .dev/, no record specifies it.
  ⭐ Also fixed a blind shared double: FullCapturingDrawBuilder inherited EmitRaw's DEFAULT NO-OP and
  silently dropped every raw primitive, so rails using it were blind to pick boxes and bindings.
  ✅✅ CE-259af DONE 2026-09-11, and it came from the USER ASKING WHY A FIELD EXISTS: "why are we still
  keeping a field for ecs entity index and generation in the gizmo now?" ⭐ Answering it properly meant
  justifying the FIELD rather than the decision — and that turned up a real latent defect.
  📐 DebugPrimitivesIngressTranslator AppendRaw'd received primitives VERBATIM, so a receiving node's
  buffer held the SENDER's ECS handle at offsets 8/12 ⇒ ToPickToken would rebuild a foreign handle and
  SelectionInteractionSystem would select a locally-plausible WRONG entity, silently. Defect D2
  relocated from the wire to the receiver's own boundary. NOT live (that translator is instantiated
  only in tests) but IG imports it and holds a map.
  ⭐⭐ FIX: strip the payload at the ONE place foreign primitives enter a buffer ⇒ a received primitive
  arrives with StreamId==0, the adapter yields an invalid token, and the interaction goes over DDS to
  the OWNING node which resolves via its map. It degrades onto the DESIGNED remote path instead of a
  wrong selection. ⇒ the field's contract is no longer "do not compare this" but "valid only for a
  primitive emitted in THIS PROCESS" — structural now, not a comment.
  ⛔⛔ AND THE NAIVE FIX IS WRONG — I NEARLY SHIPPED IT. Zeroing 8/12 unconditionally breaks two of the
  THREE roles offset 8 carries: EntityLocal uses it as the SpatialAnchor cache KEY (remote EntityLocal
  geometry would stop resolving at all) and Text/EntityBadge use StringHash@8 + LineOffsetPx@12. The
  strip is shape-discriminated, with TWO red-proofs — removing it reddens 1, a BLANKET zeroing reddens
  the other 2. That second red-proof exists because I made that mistake.
  ⛔⛔ THE NEXT SENTENCE IS SUPERSEDED BY §6.7 AND IS KEPT ONLY AS HISTORY — DO NOT QUOTE IT. The payload
  WAS deleted, and the premise below is false: the map is a WORLD SINGLETON, not a per-host delegate, so
  no host can forget to pass it, and ReplayBrowser now maintains one like every other ECS module.
  ~~⚠ Why not delete the payload instead: ReplayBrowser has NO NetworkEntityMap (:991 falls back to the
  linear FindEntityByNetworkId, which C5 forbids), so resolving locally needs a delegate every host
  must remember to pass — the SILENT-DEFAULT failure that produced CE-259y. The payload needs nothing
  passed, so it cannot be forgotten in one host. 📄 DESIGN_Gizmo_Anchor_Identity.md §6.6.~~
  ⛔ PROCESS FAILURE WORTH NOT REPEATING (the third of the day): HandleInput builds a pick token TWICE
  and the first S5 pass converted ONE arm. Every suite stayed green because the rails exercise the
  hit-test, not HandleInput. `scripts/find.sh 'AnchorGeneration != 0'` found it in one call. And the
  rail that should have caught it lived in GizmoMap.Contracts.Tests as a RE-IMPLEMENTATION of the
  production logic — that project cannot reference production code at all. Moved, and both arms now
  call ONE seam, DebugGizmoLayer.MakePickToken.
  🔴 NEW, MEASURED, NOT BUILT: CE-259w — ONE RIGHT-CLICK ENDS TWO TOOLS. The picker cancels on right
  PRESS (EntityPickerGizmo.cs:164), the vertex editor self-removes on right RELEASE (:198-200), so the
  DOWN pops the picker, the editor resumes, and the UP then ends the editor. Pre-existing; §4.7i only
  made it visible. Lean is (a) pair the gesture in the arbiter — the press recipient also gets the
  release. ⛔ Blast radius NOT measured yet; do not build before it is (R-139).
  ⭐ ALSO OPEN, needs a nod: CE-259v — a suspended EntityRotatorGizmo draws a line to a STALE cursor.
  OPEN with leans: CE-259s, CE-259t. FIXED 2026-09-10: CE-259q.
  ⛔ The 2026-09-09 T1 manual test found the amber-crosshair defect; it is CE-259r and NOT yet fixed.
  ✅✅✅ RUN-THE-REAL-THING, 2026-09-11 — THE ANCHOR-IDENTITY WORK IS PROVEN IN THREE RUNNING HOSTS.
  📄 DESIGN_Gizmo_Anchor_Identity.md §6.8a (editor + --mode all) and §6.8b (ReplayBrowser).
  ⭐ --mode editor and --mode all under xvfb, driven over HTTP: hill-attack live, simTime 348.9, the AI
  chain (contact → firing), 739–802 primitives/frame per perspective with 16–24 Box2D pick boxes each,
  117 translators / 20 live topics. ZERO GizmoAnchorIdentityException across 1130 log lines.
  ⭐⭐ --mode replaybrowser (the riskiest host — §6.7 gave it a NetworkEntityMap it never had, rebuilt at
  every seek): a 50.5 MB / 3671-frame .fdp recorded from the editor, loaded, seeked to 5 frames with
  /replay/entities returning n=8 EVERY TIME, stepped forward/back with correct clamping, 8 panels
  captured per frame, unloaded. ZERO exceptions.
  ⭐⭐⭐ AND THE ZERO IS NOT VACUOUS — an INVERSE-EDIT RED-PROOF in that process: a temporary
  Box2D(subElementId:7, anchorId:0) in ReplaySpatialBoundsGizmo.Draw ABORTED IT ON FRAME 1 via
  DebugPrimitiveBuffer.AppendRaw:74 → StatelessGizmoSystem.Execute:107 → ReplayBrowserSubsystem
  .Update:421 ⇒ the emitters really run there, the central guard is armed there, and it fails fast
  UNHANDLED (no scheduler try/catch swallows it — the CE-188 disease). Reverted; clean drive re-run.
  ✅✅✅ CE-259am FIXED 2026-09-11 — THE REPLAY BROWSER CONTRIBUTES A DEBUG PROVIDER, and fixing it found a
  SECOND, PRE-EXISTING defect. 📄 AS-BUILT: ../DESIGN_Gizmo_Anchor_Identity.md §6.8c.
  ⭐⭐ A seam ADOPTION, not a mechanism: it was the ONLY perspective-owning subsystem not implementing
  IProvidesDebugSurface — the seam Program.cs:388 already selects on and four other hosts already have.
  Now providers=[ReplayBrowser] with a MEASURED matrix (world.read/world.entityMap/panels.gizmo true, the
  other six honestly false). world: is a Func for a LOAD-BEARING reason unique to this host (RebindActiveRepo
  REPLACES the repo on every seek, so a captured value would answer from the pre-load master forever), and
  entityMap: goes through a NEW shared SubsystemDebugProvider.EntityMapFrom — the exact analog of TkbFrom.
  ⚠ CGF/SimHost/IG deliberately NOT migrated to it: their private fields ARE the singleton they set, so they
  are correct BY CONVENTION and swapping three hosts deserves its own measurement.
  ⭐ The message is fixed too: one GizmoFeedAbsenceReason() names which of three states it is instead of
  asserting a missing buffer the red-proof showed was there.
  🔴🔴 THE SECOND DEFECT, and it was NOT debug-API-only: the instant world.read became reachable,
  /entities/{id}/focus answered 500 "Strict Mode Violation: CenterOnEntityCommand (ID: 8104) … not
  registered" — BYTE-FOR-BYTE the crash CE-065 already fixed on CGF, whose own header records
  POST /entities/1000/focus → 500 … (ID: 8104) as its reproduction. The shared list had FOUR adopters and
  this is the fifth host that needed it; RepositoryPriming registers component tables, NEVER events.
  ⭐ Fixed in TWO halves and the second is the point: ① PresentationComponentRegistry.RegisterAll at a new
  PrepareRepo choke point (Initialize AND RebindActiveRepo, idempotent); ② the shared CenterOnEntitySystem
  ticked from Update — ⛔⛔ ① ALONE WOULD HAVE MADE THE ROUTE LIE (ok:true, camera never moves, since this
  host runs no kernel so nothing consumes the event). ⭐ It also fixes the UI path: the entity-inspector's
  "Center on entity" was publishing into a world that had never heard of the event.
  ⛔⛔ AND A RED-PROOF CAUGHT A FLAW IN MY OWN RAIL — the most useful thing in the batch. My perspective
  assertion was src.Contains("\"<Perspective>\"") and the inverse edit STAYED GREEN: every one of these
  subsystems also writes `public string Name => "<Perspective>"`, so the literal was satisfied by the NAME
  property while perspective: named something else. THIRD recorded instance of that blindness (the
  fully-qualified EntityRotatorGizmo since CE-051; new FdpEventBus() in CE-260). Fixed IN PLACE, reddens now.
  📐 Rails went into the feature's OWN suite (TheDebugProvidersDoNotUnderReportTests 10/10 → 21/21) because
  this is a THIRD defect shape: CE-162 was argument-present-and-null, CE-163 argument-absent-from-a-provider,
  this is NO PROVIDER AT ALL — and both older rails read an argument list that does not exist. Four
  inverse-edit red-proofs, all 1🔴.
  📐 Blast radius BOUNDED: Configuration:190 rejects replaybrowser combined with anything ⇒ standalone-only,
  so --mode all and --mode editor cannot be affected.
  🔴 NEW, FILED, NOT FIXED: CE-259an — TWO REPLAY WORLDS. /replay/load loads into an ISOLATED
  ReplayBrowserContext owned by the debug service, so /replay/entities says 8 while GET /entities says 0 —
  and BOTH are correct (world.read reports the UI's own repo, still empty until an operator opens a
  recording). ⛔ Reading /entities → 0 as "the recording is empty" is the CE-110 mistake again. LEAN: do NOT
  rewire the shared route — add the hint. Searched docs/ and .dev/: no design record says which world
  /replay/load should target.
  ⛔ STILL NOT RUN-PROVEN, unchanged: a real PICK/SELECTION (CE-259al — the interaction bus is isolated
  from the world bus by design, so no HTTP route can publish onto it) and Stride/ (cannot run here).
  ══ STRAND 2 — ENTITY CREATION (as of 2026-09-03, untouched since) ══
  READ docs/blueprints/BOOTSTRAP_Entity_Creation_Session.md §5.0 — THE AGREED PLAN
  (user-confirmed 2026-09-01). That is the ordered continuation point; this file is only the longer LOG.
  STATE AS OF 2026-09-03 (branch head e762fe988, next free id CE-166):
    P1 EntityCreationPack adoption — ✅ COMPLETE, ALL SIX HOSTS. Host (f) IG landed 2026-09-03 with
       Q65-A' + CE-143 + CE-144 atomically, VERIFIED (GhostDestructionSystem deleted; IgNodeBootstrapper
       .cs:362 calls EntityCreationPack.Build; CE-141+CE-144 confirmed on a live four-process cluster).
    P2 relocate GhostPromotionSystem out of NedReplicationModule into EntityCreationPack, add+remove in
       ONE commit — 🔴 UNBLOCKED, the next buildable step.
    P3 ⭐⭐⭐ AUTO-TAKEOVER (role-affinity ownership) — 🔴 FULLY DESIGNED, ENTIRELY UNBUILT.
       ../DESIGN_Role_Affinity_Ownership.md, build-state READY-TO-BUILD, "Nothing here is built yet",
       §6 steps 0->3b. ⚠ Its §5 holds THREE OPEN DECISIONS that are the USER's to settle first.
       ⛔ "P1 done" does NOT mean entity-creation unification is done — P3 is the unimplemented half.
  ⭐ ALSO LIVE, a separate strand raised 2026-09-03: DESIGN_Subsystem_Composition_Unification.md §4.1
     (role-based node composition, READY-TO-BUILD at B1). §4.1L/CE-165 found that the RUNNING Hrot.Editor
     double-registers UnitHierarchySystem + EqsResultUpdateSystem and corrupts unit rosters, so B1
     ([SingleInstance] + a central duplicate check) is now a FIX, not a guard, and wants a reproducing
     rail with an inverse-edit red-proof.
  ⛔ Do not read THIS file top-to-bottom; the STATUS block below is the longer LOG and the sections
  below it are HISTORY.
folded-back: 2026-09-03 — the previous current-answer said "(f) is UNBLOCKED, §4.9 is the next batch"
  and named head 4a69ad3f8. Both were a day behind: that batch shipped. Corrected together with
  BOOTSTRAP §5.0/§5 and DESIGN_Entity_Creation_Unification.md §5 step 3 (which was three days behind,
  still claiming host (a) only). Cause in all three: the work was reported in chat and in commits, and
  the owning documents were not updated — CLAUDE.md obligation ⑤.
  The AGREED ORDER OF WORK across the 2026-08-30 compaction (unchanged):
  (1) entity creation — pack step 4, then MOVE CreateEntityRequestSystem out of Hrot.CGF to a shared
  assembly (Architect_Question_65 §5 obstacle 1), then pack step 3 as ONE uniform pipeline, then Q65-A'
  (originators self-target) and Q65-B (widen the NodeRole gate on GhostPromotionSystem inside
  NedReplicationModule -- NOT a per-host composition change; corrected 2026-08-31); THEN (2) back to the gizmo /
  symbology work: CE-134 (health bar) first, then CE-133, CE-135, CE-136 against
  UX_Feature_Entity_Symbology.md §3.8. ⭐ Q65 is RESOLVED — genesis is already peer-to-peer and needs no
  contract change; DESIGN_Entity_Creation_Unification.md §2.3's "halves" are SUPERSEDED. ⛔ Do not
  re-derive any of it.
ruling-2026-08-31: 🔒 THE GOVERNING RULING is Architect_Question_65 §0 (user, verbatim): the shared
  entity-creation code "should not restrict any ECS enabled node from creating own networked entities ...
  no exceptions, not removing capabilities by design, and only concrete authoring code picks the way it
  needs." ⭐ BOTH paths stay legitimate: OwnerAppInstanceId = 0 routes to the arbiter (CGF) for
  BRAIN-ENABLED entities and is CORRECT; OwnerAppInstanceId = localNodeId creates+owns locally (IG map
  drawings). The AUTHORING CALL SITE picks -- not a policy table, not a TKB flag, not config.
  ⛔ EntityCreationPack.Build gets NO flag that omits the request or spawn system (DESIGN §3.1
  invariant 6, §3.4, acceptance 9-11).
measured-2026-08-31: (a) IG CAN already publish EntityMaster -- SharedTranslatorPack is gated on
  participant != null, NOT on role (NedReplicationModule.cs:213), and IG calls .WithReplication at
  IgNodeBootstrapper.cs:142; it also already has MapVisualOverlayEgressTranslator for OWNED area
  entities. IG lacks only the ability to BECOME THE OWNER: request source + CreateEntityRequestSystem +
  NetworkSpawningSystem. (b) The bottleneck mechanism is ONE FIELD --
  SpawnEntityCommandEgressTranslator.cs:167 writes Owner = default => 0 => arbiter.
  (c) 🔴 ORDERING HAZARD: NetworkSpawningSystem.cs:92 and
  SpawnEntityCommandEgressTranslator.cs:80 read the SAME bus event => IG's pack adoption (step 3) and
  Q65-A' (retarget its tools) MUST ship in ONE commit or IG double-spawns. Q65 §6 carries it.
CE-142 (new, 2026-08-31): ownership DELEGATION is mechanism gated by policy. All three pieces --
  DeferredTakeOwnershipEgressTranslator (_roleHasBrain, :230), its ingress (_roleHasMuscle, :232) and
  DeferredTakeoverSystem (_roleHasMuscle, :206) -- contain ZERO role logic; the only role-specific
  thing is the INJECTED BrainMuscleOwnershipStrategy POLICY. Probe: ungating the receive side is FREE
  (ExecuteTakeover self-filters on ownerNodeId and guards each component with HasComponentByTypeId, so
  no throw on unregistered components). Latent silent drop: :313 publishes the bus command on
  _isDefaultProcessor && _ownershipStrategy != null, but the wire translator exists only on
  _roleHasBrain -- they coincide by CONVENTION only. Fix: mechanism on participant != null, policy on
  _ownershipStrategy != null. WITH or AFTER pack step 3; NOT a prerequisite for path 2.
  Prior docs saying that gate was "correct, do not widen" are RETRACTED (Q65 §5.3).
STEP 4 IS BUILT (2026-08-31). New: Hrot/Engine/Hrot.Core/Tkb/UrbanCombatTkbCatalog.cs (RegisterAll +
  BuildMannequinAnimationDef + public TkbType codes); HrotEnvironment.CreateTkb() seeds it;
  UrbanCombatNewScenario forwards; its FIVE private per-template methods DELETED (they were a second
  copy missing StrideRenderModelDefDto on all five ⇒ render-less, collider-less entities);
  EditorSubsystem's explicit RegisterUrbanCombatTkbTemplates call REMOVED (TkbDatabase.Register THROWS
  on duplicates and it was 4 lines after CreateTkb ⇒ would have crashed at startup);
  EditorStrideSubsystem LEFT ALONE (builds its own new TkbDatabase(), so no duplicate — but it still
  misses NedTkbCatalog; Stride tree cannot build on Linux, follow-up). New rails:
  Hrot.SimHost.Tests/UrbanCombatCatalogRails.cs 14/14, two inverse-edit red-proofs (remove the seeding
  => 13/14 red; strip one StrideRenderModelDefDto => exactly 1 red). T1 Hrot.SimHost.Tests 798 pass /
  1 fail / 3 skip; the 1 fail is QA-012 (FullBranchPipelineTests.BranchedRecording_...), PROVEN
  pre-existing this run by git stash + rebuild on base 7face3aee. Hrot.Editor.Tests --filter
  UrbanCombat 18/18.
CE-145 (new, deferred BY THE USER to a Windows/VS session): the animation TKB descriptor DTOs
  (CharacterAnimationDefDto + SlotDefDto/MontageDefDto/MontageNotifyRefDto/NotifyMarkerDefDto/
  StanceTransitionDto/AimConfigDto/SlotCompositingMode, plus AnimNotifyCategory and StanceId) MOVED to
  FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/ but KEPT their Hrot.MuscleCharacter.Animation.* namespaces, so
  zero consumer files changed (C# binds on namespace, not assembly). CE-145 = rename them to
  Fdp.Toolkit.Tkb.Domain — 53 files across 20 projects, 6 in the Stride tree that cannot compile on
  Linux, which is why it waits for VS. ⚠ I first sized this at 24 files; that count covered only four
  DTO names and missed StanceId + AnimNotifyCategory. Each moved file carries a header explaining why a
  Hrot.* namespace sits in Fdp.Toolkits.
OBSTACLE 1 IS DONE (2026-08-31). The 3 request-tier files moved to
  Hrot/Engine/Hrot.Common/Systems/ with namespace Hrot.Common.Systems (renamed, NOT preserved -- keeping
  "CGF" in the name of a type every node registers is the misconception Q65 kills).
  ⛔ TARGET WAS NOT Hrot.Core: Q65 §5.4's own "resolved" answer was WRONG. CreateEntityRequestSystem:394
  constructs Hrot.Common.Serializers.InitialUnitSubordinateIntent by FULLY-QUALIFIED name, and
  Hrot.Common.csproj:33 references Hrot.Core, so Hrot.Core -> Hrot.Common is a CYCLE. Moving
  InitialUnitSubordinateIntent instead was measured at ~30 consumers + a cohesive genesis-intent file =>
  more churn. Hrot.Common is reachable from every host (Editor transitively via SimHost/CGF/NED) and is
  where SharedApplicationBootstrapper lives.
  ⚠⚠ ERROR CLASS, THIRD INSTANCE IN ONE DAY: I checked the files USINGS and called the dependency set
  clean; a fully-qualified reference in a method body is invisible to that. A usings scan is NOT a
  dependency scan -- grep the body for <OtherAssembly>. prefixes, or just BUILD IT (8s/project).
  Churn: 6 one-line using additions. ⭐ The Stride fence held with ZERO action -- EditorStrideSubsystem.cs
  already imported Hrot.Common.Systems, so the one file both lanes could reach was never contested.
  Hazard handled: Hrot.Core has TreatWarningsAsErrors, so EntityLifecycleInterfaces.cs's <see cref> into
  Hrot.Common would be CS1574 => error; changed to <c>.
  New rails: RequestTierPlacementRails 12/12 (not in a host assembly, no CGF in the namespace, publicly
  constructible, IEcsModuleSystem). Non-vacuity probe (flip expectations to the OLD values) reddens
  exactly 6 of 12 -- that proves the rails read reality; it is NOT a defect red-proof.
  Gates: 10 projects build; T1 Hrot.SimHost.Tests 810 pass / 1 fail (QA-012, pre-existing) / 3 skip;
  Hrot.Editor.Tests 341/0/1; EntityCreationFlowTests 7/7 (integration -- exercises the moved system
  end to end).
PRE-EXISTING BREAK FIXED (out of my lane, flagged for the backend lane): Hrot.SimHost.Integration.Tests
  did not compile AT ALL on base -- SimHostInstance.cs used AttributeCompilerFactory with no
  using Fdp.Toolkit.Replication.Attributes. Proven pre-existing by git stash + rebuild (same CS0103 on
  base). Fixed with the one missing using because it was blocking verification of my own change; the
  whole test project is now buildable and its 7 entity-creation tests pass.
STEP 3 STARTED (2026-08-31): EntityCreationPack BUILT in Hrot/Engine/Hrot.Common/EntityCreation/
  (Pack + Context + EntityCreation). Host (a) StrideNodeBootstrapper ADOPTED -- and that closed a second
  gap: it had no CreateEntityRequestSystem at all, so nothing could ask it to create an entity, not even
  itself. Scheduling unchanged (spawn via SimHostModule/BeforeSync; request + finalization via
  RegisterGlobalSystem). ⚠ Follow-up: no DDS ingress/ACK sink passed there because HrotNodeContext
  exposes no lifecycle adapters => local requests only; strictly better than before.
  ⛔ NOT in this slice: the two authoring affordances (DESIGN §3.4) -- they need CE-143's ReliableInitType,
  so they land with Q65-A'.
  Rails: EntityCreationPackRails 8/8 (acceptance 2,3,5,9 + no-kernel + no-suppression-flag tripwires).
  Red-proof: remove ctx.Elm.SetTranslators => exactly 1 rail reddens (the CE-139 defect class).
  T1 818 pass / 1 fail (QA-012) / 3 skip. Hrot.NodeComposition.Tests 22/22.
  ⚠ Acceptance 3's rail uses REFLECTION on EntityLifecycleModule._translators -- private, no accessor.
  A read-only accessor on the ELM would be the better fix (Fdp.Toolkits change, deferred).
  REMAINING hosts for step 3: SimHost, Editor, CGF, then IG -- IG atomic with Q65-A' + CE-143 + CE-144.
CE-145 DONE + MERGED (2026-08-31, from claude/ce145-stride-namespace-win at 03ecea4da). Namespace rename
  complete (55 files, not the 24/53/56 I quoted); EditorStrideSubsystem now uses HrotEnvironment.CreateTkb()
  and the strip translator is back at index 2; Fdp.Examples.Scenarios dropped its now-redundant
  Hrot.MuscleCharacter.Animation reference. Verified live in the Stride editor: entities=6, visuals=6.
  Merge verified on Linux: 8 projects build, T1 818/1/3 = identical to pre-merge. ⚠ One T1 run reported 3
  failures while naming only 1 (26s vs the usual 14s); two following runs were 818/1/3, so the steady state
  is 1 (QA-012) but that suite is not perfectly deterministic under load.
  ⚠⚠ MY HANDOFF'S GREP MISSED THREE THINGS: relative-qualified refs (Components.StanceId, 15 refs/6 files,
  hard CS0234); a fully-qualified ref that must NOT move (…Contracts.AnimationBackendConfig); and that
  …Animation.Descriptors was declared by the moved file ALONE, so the rename DELETES the namespace and every
  using of it is an error. HABIT: for a namespace move, grep the namespace SEGMENTS too, and check whether
  the moved file was the sole declarant.
CE-146 (new, 2026-08-31) -- and it came from THEIR probe refuting MY hypothesis. I claimed the strip
  translator's Capsule gate was unsatisfied; Apply_Infantry_AddsCapsuleRenderDef PASSES, so type 200 does
  carry a Capsule StrideRenderModelDefDto. The two VehicleState reds are instead: (a)
  Translator_Infantry200 = STALE TEST, it calls VehicleKinematicsTkbTranslator.Inject() alone so the strip
  post-pass never runs => fix the test, not the product; (b) SI3_InfantryMoveTo = REAL CROSS-HOST GAP =
  CE-146: EditorSubsystem.cs:1241 uses bare TkbTranslatorSet.Base(), the strip's only registrar is
  EditorStrideSubsystem, and the strip lives in Hrot.Stride.Core (unreachable from Hrot.Editor) => Capsule
  infantry keeps VehicleState on every host but the Stride editor, while the crowd bridge that guards on
  !HasComponent<VehicleState>() (NavigationIntentBridgeSystem) is SHARED in Fdp.Toolkits. Three options in
  DESIGN §3.3; ⛔ do not pick one before measuring whether any non-Stride host actually runs that bridge
  over capsule infantry. The two StrD21 navigation reds are unattributed.
  ⭐ Their baseline was 5 pre-existing reds, not 4 -- the 5th is the AttributeCompilerFactory build break
  that blocked the whole solution build on Windows, and obstacle 1's commit already fixed it.
CE-146 PROBED AND RESOLVED (2026-08-31) -- and my own three options (A/B/C) were the WRONG FRAME.
  MEASURED: (1) the crowd guard is DOUBLE-gated on _dtCrowd != null (NavigationIntentBridgeSystem:235,243);
  (2) two production registrars -- StrideMuscleModules:70 passes a crowd, but SimHostCoreLogicPack:118 uses
  the NO-ARG ctor => the crowd path is INERT on SimHost, so SimHost's missing strip is NOT a gap;
  (3) DotRecastDtCrowdProvider exists only in the Stride tree (EditorStrideSubsystem:635, :887) and
  Hrot.Editor has ZERO DtCrowd references; (4) BUT EditorStrideSubsystem:892 does
  _editor = new EditorSubsystem() and injects the Stride muscle via MuscleModuleFactory =>
  "EditorSubsystem + a LIVE crowd" IS a real production configuration and SI3 replicates it faithfully.
  => CE-146 is a REAL production defect whose root is the TWO SPAWN PIPELINES OVER ONE WORLD (the exact
  ambiguity CE-139 named): EditorSubsystem:1241 uses bare Base() (no strip => VehicleState stays => crowd
  registration SKIPPED) while EditorStrideSubsystem's list has the strip at index 2. Which pipeline handled
  a spawn decides whether that infantry can join the crowd.
  RESOLUTION: it is step 3 HOST (e) -- collapse the Stride editor's second pipeline into the pack. And the
  fix needs NO new reference: Hrot.Editor can never name the strip, but EditorStrideSubsystem already
  injects the muscle via MuscleModuleFactory, so it passes EntityCreationContext.ExtraTranslators the same
  way. That is precisely what the pack's add-only ExtraTranslators is for.
  ⛔ Options A (move the strip down) and B (restore the shape guard) are DEAD. C (one host only is fine) is
  also dead -- the Editor genuinely runs a live crowd when Stride hosts it.
  ⚠ Verification of host (e) CANNOT be done on Linux -- hand it to the Windows lane.
  ⚠ The two StrD21 navigation reds are plausibly the same root but remain UNATTRIBUTED; do not claim them
  until host (e) is done and they are re-run.
  ⚠ TWO STALE DIAGNOSTICS to fix in words, not code: NavigationIntentBridgeSystem.cs:234-240's warning text
  ("the translator fix is absent", "ShapeKind must be Capsule") describes the pre-relocation design -- the
  tripwire is correct, its explanation is not; and Translator_Infantry200_DoesNotInjectVehicleState calls
  the kinematics translator ALONE, encoding the removed design => re-home it onto the strip.
CE-143 (new, 2026-08-31, from the architect review): ReliableInitType is HARDCODED to AllPeers at
  CreateEntityRequestSystem.cs:302 (root) and :397 (TKB children), and EntityCreationRequest carries NO
  field to override it. Enum has None / PhysicsServer / AllPeers. => an IG drawing created via path 2
  waits for ConstructionAck from all expected peers: pointless latency and a stall risk. FIX: add an
  init-only ReliableInitType to EntityCreationRequest, default AllPeers (so adoption changes nothing),
  both affordances take it explicitly, IG passes None. Decide explicitly whether :397's children
  inherit the parent's InitType (lean: yes). SHIP WITH Q65-A' (step 4) -- it is the one real
  prerequisite for IG drawings being USABLE, not merely correct. STILL UNVERIFIED (needs a live
  cluster): do peers ACK ghosts of entities they neither own nor simulate?
obstacle-1-resolved (2026-08-31): the move target is Hrot.Core/Network/ -- NOT Fdp.Toolkits (that would
  invert the layering, since both files depend on Hrot.Core.Network) and not Hrot.Common. Exactly 2
  files move: CreateEntityRequestSystem.cs + EntityRequestFinalizationSystem.cs. Measured: neither
  references Hrot.CGF/Map/Editor/IG except its own namespace line; JsonAttributeCompiler and
  IOwnershipDistributionStrategy are already in Fdp.Toolkits. Zero new project references. Also update
  the <see cref="Hrot.CGF.Systems.CreateEntityRequestSystem"/> in EntityCreationRequest's docs.
  RULED by the user 2026-08-31: YES, move DeleteEntityRequestSystem.cs too => the move is 3 FILES.
  Measured, and the case is stronger than the lean: its ctor takes EntityRequestFinalizationSystem as a
  REQUIRED arg (so leaving it behind splits a hard dependency), its usings are Hrot.Core.Network + Fdp.*
  only, IEntityDeletionRequestSource is in the SAME file as the creation one
  (Hrot.Core/Network/EntityLifecycleInterfaces.cs:102), and it has 1 production construction site
  (CgfSubsystem.cs:728). The see-cref fix in EntityCreationRequest is an explicit deliverable of the
  same commit; sweep for other stale crefs.
CE-144 (new, 2026-08-31): the DESTROY side has the SAME double-consumption hazard as spawn, and it
  fails SILENTLY. GhostDestructionSystem (IgBootstrapperHelpers.cs:30) does _entityMap.Unregister +
  world.DestroyEntity IMMEDIATELY; NetworkSpawningSystem.ProcessDestroy (:98 -> :213) does
  cmdBuffer.SetLifecycleState(TearDown) + _elm.BeginDestruction. If IG holds BOTH, whichever runs first
  defeats the other: GhostDestruction first => ProcessDestroy finds nothing in the map, logs to stderr
  and returns => ELM teardown NEVER runs => EntityMaster is never disposed => peer IGs keep ZOMBIE
  drawings. Reverse order rips the entity out mid-teardown. Either order is wrong => once IG has
  NetworkSpawningSystem, DROP GhostDestructionSystem (its own comment says it "replaces SpawningModule").
  Ships in the SAME commit as IG's adoption + Q65-A' + CE-143. Acceptance 11 extended to the destroy
  side. NOT verified: the actual execution order today (irrelevant to the fix, decides the symptom).
  ⛔ CORRECTS my own earlier text in Q65 §5.1 and DESIGN §5.1 ("IG keeps GhostDestructionSystem").
NON-FINDING, recorded so it is not re-derived: the deletion tier has only a DDS source while creation
  has three (DDS + in-memory + composite). That is NOT a gap -- the local destroy path bypasses the
  request tier entirely (NetworkSpawningSystem.cs:98 consumes bus DestroyEntityCommand), so any node the
  pack equips can destroy what it owns in-process. Do not add an in-memory deletion source.

stale-below: ⛔ EVERYTHING except §0's header and §0.0e is HISTORY, newest first — §0.0c (the CE-070/071
  way-forward), §0.0b (phase 1's seam), §0.0a/§0.0 (phase 0), §0-prev and below. They are kept as the
  record of WHY, not as instructions. ⚠ §0.0c and the §0 header both used to say "Start here"; §0.0d
  supersedes both (corrected 2026-08-27).
known-conflict: ⛔ HANDOFF_Cgf_Bootstrap_Unification.md (the dispatched frame) is STALE on two points —
  its stage-1 god-facade prerequisite and its phase-0 rail wording. AQ63 §10.4 and §12 supersede both,
  deliberately. The handoff is NOT edited (rule 1: never amend a dispatched handoff).
```