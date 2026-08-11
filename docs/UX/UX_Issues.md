# Scenario-Authoring UX — Issue Register (`UXI`)

> **Formalised 2026-08-10.** The "what is wrong" discussion is **closed**; this is its output.
> Mirrors the blueprint programme's [issue tracker](../blueprints/Blueprint_Issues_Tracker.md) pattern.
>
> **Flow:** `UXI-nn` (an issue) → **a feature design doc** → `UXT-nn` (implementation tasks).
> **An issue is not ready to break into tasks until its design doc exists and is agreed.**
>
> Evidence for every issue: [UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md) ·
> Scope: [UX_Requirements.md](UX_Requirements.md) · Order: [UX_Cleanup_Path.md](UX_Cleanup_Path.md)

**Complexity:** `WIRING` = call existing code · `RW-L` ≲150 lines · `RW-M` new component / some design ·
`RW-H` new subsystem or architect decision first. 🔴 = correctness / data-loss / trust defect.

**Status:** ☐ open · ▣ design in progress · ✅ designed, ready to cut into tasks · ☑ done ·
⊘ refuted · 🔒 blocked.

## Register

| # | ID | Issue | Cmplx | Req | Design doc | St |
|---|---|---|:--:|---|---|:--:|
| **A** | | **Foundation & hygiene** | | | | |
| | <a id="uxi-01"></a>**UXI-01** 🔴 | **Superseded UI, and the namespace that lies.** `Hrot.UI.Common` builds nowhere yet owns the namespace the live panels declare | `RW-L` | — | [UX_Feature_DeadUI_Removal.md](UX_Feature_DeadUI_Removal.md) | ✅ |
| | <a id="uxi-02"></a>**UXI-02** | **Half-built items need a decision each** — `ScenarioEditorModule`, `SelectionRenderSystem`, `WorkspaceMenuBuilder`, `EditorTool.Select`. Not dead; each encodes an intent | `RW-L` | — | [UX_Feature_HalfBuilt_Decisions.md](UX_Feature_HalfBuilt_Decisions.md) | ✅ |
| | <a id="uxi-20"></a>**UXI-20** `P2` | **The `Hrot.UI.Common` namespace outlives its project.** After [UXI-01](#uxi-01) the name is inaccurate rather than hazardous — ~87 files, 4 test projects, one co-owned file. Never on the critical path | `RW-L` | — | [UXI-01 §2](UX_Feature_DeadUI_Removal.md#2--the-design-decision-delete-now-rename-later-or-never) | ☐ |
| | <a id="uxi-21"></a>**UXI-21** `P2` | **The Workspace document switcher has a model and ~15 tests but no renderer.** `WorkspaceMenuBuilder` builds open-asset entries with active/dirty markers that nothing draws | `RW-L` | — | — | ☐ |
| | <a id="uxi-22"></a>**UXI-22** | **SimHost and CGF may have no selection highlight at all** — neither registers `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar`, which owns `SelectionHighlightGizmo`. ⚠ confirm before treating as a defect | `RW-L` | — | — | ☐ |
| **H** | | **Cross-host parity** | | | | |
| | <a id="uxi-23"></a>**UXI-23** 🔴 | **SimHost and CGF lack the common map-interaction set.** No rubber-band visual, no measure, no centre-on-selected, no delete-selected, and **CGF has no `SelectionInteractionSystem` at all**. Absorbs [UXI-22](#uxi-22). Delivered by the mechanisms in B and C plus per-host wiring | `RW-M` | [UXR-90](UX_Requirements.md#uxr-90) | — | ☐ |
| | <a id="uxi-24"></a>**UXI-24** 🔴 | **Multi-select is not supported anywhere.** The multi-entity handler overload is a default no-op no host overrides; items act on the clicked entity only. Needs AND-over-selection visibility and fan-out execution | `RW-M` | [UXR-91](UX_Requirements.md#uxr-91) | — | ☐ |
| **B** | | **Entity actions** | | | | |
| | <a id="uxi-03"></a>**UXI-03** | **No shared action vocabulary.** Identity, label and ordering are re-declared per host; `Center`/`Delete` exist 3× | `RW-M` | [UXR-89](UX_Requirements.md#uxr-89) | — | ☐ |
| | <a id="uxi-04"></a>**UXI-04** | **The same entity offers different actions per surface** — inspector lambdas vs map JSON vs hardcoded ORBAT rows. Includes the ORBAT item seam, which lets ExCon's 434-line fork collapse | `RW-M` | [UXR-85](UX_Requirements.md#uxr-85) | — | ☐ |
| | <a id="uxi-05"></a>**UXI-05** | **No menu consults perspective.** The toolbar has the filter; `GlobalMenuRegistry` does not | `RW-M` | [UXR-86](UX_Requirements.md#uxr-86) | — | ☐ |
| | <a id="uxi-06"></a>**UXI-06** | **Perspective restore speaks the wrong vocabulary** — validated against subsystem names, so a saved `BTree`/`HSM`/`Blueprint` is dropped. ⚠ minor: you lose your place only | `RW-L` | — | — | ☐ |
| **C** | | **Tools** | | | | |
| | <a id="uxi-07"></a>**UXI-07** | **A tool is not a thing.** No abstraction, no current-tool state, 4 activation idioms — 2 of them for the same tools in one class. Toolbar cannot show active state; `EditorTool.Select` is a dead button | `RW-M` | [UXR-81](UX_Requirements.md#uxr-81), [UXR-84](UX_Requirements.md#uxr-84) | — | ☐ |
| **D** | | **Layout** | | | | |
| | <a id="uxi-08"></a>**UXI-08** | **No shipped default layout.** `imgui.ini` is machine-wide with no path seam; nothing seeds a new user; the default cannot be authored or committed | `RW-M` | [UXR-04](UX_Requirements.md#uxr-04) | — | ☐ |
| **E** | | **Map** | | | | |
| | <a id="uxi-09"></a>**UXI-09** 🔴 | **Camera setup copy-pasted 4×, every copy stale**, and nothing is occlusion-aware — `DockspaceLayout.CentralSize` exists and no camera reads it | `RW-M` | [UXR-18](UX_Requirements.md#uxr-18) | — | ☐ |
| | <a id="uxi-10"></a>**UXI-10** | **Map symbology seam exists and no host uses it** — every host passes `DefaultEntityShapeLibrary` | `RW-L` | — | — | ☐ |
| | <a id="uxi-11"></a>**UXI-11** | **CGF and ExCon sit outside the shared selection mechanism** — CGF has no `SelectionInteractionSystem`; ExCon uses an id list over the wire | `RW-M` | — | — | ☐ |
| **F** | | **Duplication** | | | | |
| | <a id="uxi-12"></a>**UXI-12** | **Spawn UI ×4** — `SpawnerPanel`, `MiniExConPanel`, `SimHostSpawnPanel`, plus an inline combo in ExCon's ORBAT | `RW-M` | [UXR-83](UX_Requirements.md#uxr-83) | — | ☐ |
| | <a id="uxi-13"></a>**UXI-13** | **Gizmo main-menu-bar block copy-pasted ×4**, bypassing the overload built for it | `RW-L` | — | — | ☐ |
| | <a id="uxi-14"></a>**UXI-14** | **`PanelConstants` copied verbatim** by ExCon; **`MapLayerBits`** hand-synced with a comment admitting it | `RW-L` | — | — | ☐ |
| | <a id="uxi-15"></a>**UXI-15** | **IG runs two entity inspectors at once** — its own 78 L and the shared 593 L | `RW-L` | — | — | ☐ |
| **G** | | **Correctness & robustness** | | | | |
| | <a id="uxi-16"></a>**UXI-16** 🔴 | **No `Delete` confirms, in any of the five hosts.** Every one fires immediately | `RW-L` | [UXR-15](UX_Requirements.md#uxr-15) | — | ☐ |
| | <a id="uxi-17"></a>**UXI-17** | **Two `async void` menu handlers** — unobserved exceptions | `RW-L` | — | — | ☐ |
| | <a id="uxi-18"></a>**UXI-18** | **Editor's JSON parser reads `children` without a `ValueKind` guard** — a non-array throws `InvalidOperationException`, which its `catch (JsonException)` does not catch | `RW-L` | — | — | ☐ |
| | <a id="uxi-19"></a>**UXI-19** ⚠ | **Two presentation gizmos may match one entity** — Editor registers both; overlapping projector keys. **Unverified** — establish before treating as a defect | — | — | — | ☐ |

**Counts:** 24 issues · **2 designed** · 6 🔴 · 2 unverified (UXI-19, UXI-22).

## Dependency order

```
UXI-01 ──────────────────────────────────────  first, always: removes an editing trap
UXI-02 ──────────────────────────────────────  independent of UXI-07 after all
UXI-03 ───── UXI-04 ── UXI-05 ── UXI-06
UXI-07 ──────────────────────────────────────  no pre-existing home: PACK2-E002 is DONE
UXI-08, UXI-09, UXI-10, UXI-16, UXI-17, UXI-18  independent — any order
UXI-24 multi-select ── shapes UXI-03's API; UXI-11 (shared selection) precedes it
UXI-23 parity ──────── delivered BY UXI-03/04/07/11; not a separate mechanism
UXI-12..15 ────────────────────────────────── after the seams exist
```

## Rules

1. **An issue references a feature design doc.** Detail lives in the design, not here — this register
   stays a register.
2. **No tasks are cut before the design is agreed.** That is the gate this layer exists to enforce.
3. **Evidence is code**, cited `file.cs:line`, and **re-derived before building** — the
   [Corrections table](UX_Tasks_Detail.md#corrections) has 11 rows, five of them our own claims.
4. ⚠ **A code comment describing future work is not evidence the work is pending.** Correction 11 came
   from reading `"populated in PACK2-E002"` as a live plan when that migration had already completed in
   a different shape. Check whether the work landed before planning around the comment.
5. **One design per session prompt**, summarised before moving on (user, 2026-08-10).
