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
| | <a id="uxi-02"></a>**UXI-02** | **Half-built items need a decision each** — `ScenarioEditorModule`, `SelectionRenderSystem`, `WorkspaceMenuBuilder`, `EditorTool.Select`. Not dead; each encodes an intent | `RW-L` | — | — | ☐ |
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

**Counts:** 19 open · 1 designed · 4 🔴 · 1 unverified.

## Dependency order

```
UXI-01 ──────────────────────────────────────  first, always: removes an editing trap
UXI-02 ──┐
         ├── UXI-07 tools        (UXI-02 decides ScenarioEditorModule = the tool home)
UXI-03 ──┴── UXI-04 ── UXI-05 ── UXI-06
UXI-08, UXI-09, UXI-10, UXI-16, UXI-17, UXI-18  independent — any order
UXI-11 ────────────────────────────────────── before UXI-04 lands multi-select
UXI-12..15 ────────────────────────────────── after the seams exist
```

## Rules

1. **An issue references a feature design doc.** Detail lives in the design, not here — this register
   stays a register.
2. **No tasks are cut before the design is agreed.** That is the gate this layer exists to enforce.
3. **Evidence is code**, cited `file.cs:line`, and **re-derived before building** — the
   [Corrections table](UX_Tasks_Detail.md#corrections) has 10 rows, four of them our own claims.
4. **One design per session prompt**, summarised before moving on (user, 2026-08-10).
