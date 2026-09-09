<!--STATUS
state: LIVE
build-state: PARTIAL
verified: 2026-08-28 (coordinator source scan)
current-answer: PARTIAL. DONE: stale comment fixed (CE-051), WorkspaceMenuBuilder kept, EditorTool.Select drain case. MISSING: delete SelectionRenderSystem/SelectionRenderConstants + update RenderLayerPresenceTests; fix dangling <see cref> in SelectionState.cs:11/48 (-> SelectionHighlightGizmo).
-->
# Feature design — Deciding the four half-built items

> **Design for [UXI-02](UX_Issues.md#uxi-02) · drafted 2026-08-10 · needs no architect round.**
> **Status: 🟡 PARTIAL — DONE: stale comment fixed (CE-051), `WorkspaceMenuBuilder` kept, `EditorTool.Select` drain case. MISSING: delete `SelectionRenderSystem`/`SelectionRenderConstants` + update `RenderLayerPresenceTests`; fix dangling `<see cref>` in `SelectionState.cs:11/48`.**
>
> These four were deliberately held out of [UXI-01](UX_Feature_DeadUI_Removal.md) because they are
> **half-built, not superseded** — each encodes an intent. This design decides each one.

## 0. Prior art — ✅ re-verified 2026-08-10 against the [Seam Inventory](UX_Seam_Inventory.md)

**All four decisions hold.** One addition to the work list:

| Item | Index says | Verdict |
|---|---|---|
| `ScenarioEditorModule` | 2 prod consumers, 5 tests, adopter `Hrot.Editor` | ✅ **KEEP** confirmed — a live, registered module |
| `SelectionHighlightGizmo` | 1 prod consumer (`Hrot.Editor`) | ✅ the surviving implementation, as designed |
| `WorkspaceMenuBuilder` + `WorkspaceMenuEntry` | **0 prod consumers**, 1 test | ✅ model-and-tests-without-a-renderer confirmed ([UXI-21](UX_Issues.md#uxi-21)) |
| `SelectionInteractionSystem` | 5 prod, adopters **Editor · IG · ReplayBrowser · SimHost** | context: the shared mechanism CGF sits outside ([UXI-11](UX_Issues.md#uxi-11)) |

> ### ⚠ New task — the delete has a loose end nobody listed
>
> The index reported `SelectionRenderSystem` and `SelectionRenderConstants` at **2 production consumers**,
> against this design's *"nothing instantiates it"*. Verified: both are `<see cref>` **doc links** in
> `Hrot/Engine/Hrot.Core/Components/Map/SelectionState.cs:11,27,48` — **not** instantiations. The claim
> holds.
>
> 🔴 **But those crefs name `Hrot.IG.Systems.SelectionRenderSystem` — a namespace the type already left**
> (it now lives in `Hrot.ScenarioEditor.Rendering`). They are **dangling today** and deleting the type
> makes them permanently so. ⇒ **`UXT` task: fix all three `<see cref>` links in `SelectionState.cs`**,
> pointing them at `SelectionHighlightGizmo`.

## ⭐ The finding that decides two of the four

Investigating produced a single coherent story that my earlier reading got wrong:

> **The `ScenarioEditor` migrations (PACK2-E002 / E003) are COMPLETE. They finished via the *gizmo*
> path, not the *systems* path — so the module's empty systems list is a correct outcome, not a gap.**

`ToolPresenceTests` states it outright: *"verifying that the tool-migration (**PACK2-E002**) was
**complete**: all 10 interaction tools now live in `Hrot.ScenarioEditor` and are absent from
`Hrot.IG`"* — and it asserts the old `Tool` classes were **deleted** and replaced by gizmos
(`MeasureTool` → `MeasureGizmo`).

🔴 **This retracts a claim I made earlier in the session** — that *"PACK2-E002 is tool migration, which
is exactly UXI-07's work, so somebody already decided where the tool controller lives."* **Wrong.**
PACK2-E002 was a **relocation** (move tools out of `Hrot.IG`) and it finished by **converting tools into
gizmos**. It says nothing about a tool *abstraction*. [UXI-07](UX_Issues.md#uxi-07) is genuinely new work
with no pre-existing home. Logged as [Corrections row 11](UX_Tasks_Detail.md#corrections).

## The four decisions

### 1. `ScenarioEditorModule` → ✅ **KEEP.** It is not a stub; the comment is stale

| Evidence | |
|---|---|
| Instantiated in **production** | `EditorSubsystem.cs:900` — `new ScenarioEditorModule(fileService)` |
| **Registered into the kernel** | `RegisterModule(...)`; also `EditorHarness.cs:195`, `OfflineKernelBootTests.cs:48` |
| `IEcsModule` is a **well-adopted** abstraction, not a stalled one | `CycloneNetworkModule`, `GeographicModule`, + several `IEcsModuleSystem` implementors |
| It has its own tests | `ScenarioEditorModuleTests.cs` ("PACK2-E001") |
| It carries a real dependency | `ScenarioFileService`, exposed as `FileService` and consumed by the editor |

⇒ **A live, registered module whose systems list is legitimately empty.** The only defect is the comment
*"populated in PACK2-E002 and PACK2-E003"*, which describes work that **has since completed differently**.

**Action:** correct the comment to record that both migrations landed via `[GizmoProjector]`
registration. **One-line change. Do not delete anything.**

### 2. `SelectionRenderSystem` + `SelectionRenderConstants` → ❌ **DELETE.** Superseded, and confirmed

The ambiguity I flagged (*"migration never finished"* vs *"superseded"*) is resolved: **superseded.**

| | Draws | Registered how |
|---|---|---|
| `SelectionRenderSystem` (`IMapLayer`) | primary = filled green + outline | **nothing instantiates it** |
| **`SelectionHighlightGizmo`** (`IStatelessGizmo`) | primary = green ring, secondary = yellow ring | `[GizmoProjector(SelectionState, SimTransform)]` — auto-registered, and the Editor pulls the registrar at `EditorSubsystem.cs:1099-1100` |

Same job, and the gizmo is the one that runs. **Same pattern as the tools** — PACK2-E003 finished by
converting to gizmos too.

**Action:** delete both files, and **update `RenderLayerPresenceTests`** — it currently asserts
`SelectionRenderSystem` still exists, so it is a green test guarding a corpse. Its *other* assertions
(that the types are absent from `Hrot.IG`) are still valuable and stay.

> ⚠ **A gap this uncovers — file it, do not fix it here.** SimHost and CGF do **not** register
> `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar`, so they may have **no selection highlight at all**.
> That is a real finding and a new issue; it is not a reason to keep dead code.

### 3. `WorkspaceMenuBuilder` → ✅ **KEEP, and file the renderer as a feature**

A complete, documented model (`WorkspaceMenuEntry` with icon key, label, active/dirty markers, optional
select action) with **~15 unit tests** — and **no production renderer**. Only tests call `Build(...)`.

⚠ **A correction it forces:** [UXR-03](UX_Requirements.md#uxr-03)'s `Now` column says *"scenario name
appears only inside the `Workspace` dynamic submenu"*. **That submenu does not render at all.** The name
surfaces only inside *dynamic menu-item labels* (`Save Scenario '<name>'`, `EditorSubsystem.cs:2593-2600`)
— so UXR-03 is **worse** than recorded, not better.

⇒ But this builder is **not** UXR-03's answer either: UXR-03 wants the name visible *without opening a
menu*. What the builder actually serves is a **document switcher** — open assets with active/dirty
markers. Genuinely useful, separately.

**Action:** keep the model and its tests; **file "render the Workspace document switcher" as its own
issue**, `P2`. Deleting ~15 tests' worth of designed behaviour to re-derive it later is the more wasteful
option.

### 4. `EditorTool.Select` → 🔗 **Hand to [UXI-07](UX_Issues.md#uxi-07).** Do not touch it now

`case EditorTool.Select: break;` — the button does nothing, because selection runs on the always-on ECS
gizmo path. **The button is dead; the capability is not.**

Two bad options and one good one:

| Option | Verdict |
|---|---|
| Remove the button now | ❌ churn — `Select` is a legitimate default/idle tool that UXI-07 will reintroduce |
| Disable it with a tooltip now | ❌ churn — a fix with a two-week lifespan |
| **Let UXI-07 own it** — `Select` becomes the idle tool the controller returns to | ✅ |

**Action:** none here. Recorded as an explicit hand-off so it is not lost. ⚠ It **does** violate
[UXR-X1](UX_Requirements.md#uxr-x1) (no dead controls) until UXI-07 lands — accepted knowingly, and
tracked rather than forgotten.

## Scope summary

| Item | Action | Cmplx |
|---|---|:--:|
| `ScenarioEditorModule` | fix a stale comment | `WIRING` |
| `SelectionRenderSystem` + constants | delete + update `RenderLayerPresenceTests` | `RW-L` |
| `WorkspaceMenuBuilder` | keep; file the renderer as a new issue | — |
| `EditorTool.Select` | hand to UXI-07 | — |
| *(uncovered)* SimHost/CGF may lack any selection highlight | file as a new issue | — |

## Acceptance

| | |
|---|---|
| **Build + gates** | green, including `Hrot.Presentation.Tests` (owns both presence tests) |
| **Behaviour** | 🔒 unchanged at runtime — `SelectionRenderSystem` was never instantiated, so deleting it cannot alter a frame |
| **Revert-to-red** | `RenderLayerPresenceTests` must be seen **failing** against the old assertion before the new one is accepted — otherwise we have only proved the test still compiles |
| **No stale comment survives** | grep `PACK2-E00` → no comment claims pending work that has completed |

## Out of scope

The two issues this design *files* but does not fix: the Workspace renderer, and the possible missing
selection highlight in SimHost/CGF.
