# Scenario-Authoring UX — Issue Register (`UXI`)

> **Formalised 2026-08-10.** The "what is wrong" discussion is **closed**; this is its output.
> Mirrors the blueprint programme's [issue tracker](../blueprints/Blueprint_Issues_Tracker.md) pattern.
>
> **Flow:** `UXI-nn` (an issue) → **a feature design doc** → `UXT-nn` (implementation tasks).
> **An issue is not ready to break into tasks until its design doc exists and is agreed.**
>
> 📖 **Vocabulary:** [UX_Glossary_Host_Mode_Subsystem.md](UX_Glossary_Host_Mode_Subsystem.md) — *process*,
> *mode* and *subsystem* are not equal, and "host" here means **subsystem**.
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
| | <a id="uxi-22"></a>**UXI-22** | ✅ **CONFIRMED, and wider than filed.** Neither SimHost nor CGF calls `Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar` — costing them the selection ring **and** the map entity context menu, health bars, LOS, vis-cone, spatial grid. Absorbed into [UXI-23](#uxi-23) | `RW-L` | [UXR-90](UX_Requirements.md#uxr-90) | [UX_Map_Parity_Baseline.md](UX_Map_Parity_Baseline.md) | ⊘→23 |
| **H** | | **Cross-host parity** | | | | |
| | <a id="uxi-23"></a>**UXI-23** 🔴 | **SimHost and CGF lack the common map-interaction set.** No rubber-band visual, no measure, no centre-on-selected, no delete-selected, and **CGF has no `SelectionInteractionSystem` at all**. Absorbs [UXI-22](#uxi-22). ⭐ **Mostly one missing registrar call per host.** 🔒 **Ruled 2026-08-10: all three share the FULL set; differences are data availability or host rules, never set membership** — so this is wiring, not curation. ⭐ **Absorbs UXI-04 step 5** (turn the map menu on in SimHost/CGF) **and its prerequisite: CGF constructs no `GlobalActionRegistry` at all (0 handlers) and SimHost binds only 4** — none of them Center/Select/Delete. Activating the map menu before binding them yields **inert items** | `RW-M` | [UXR-90](UX_Requirements.md#uxr-90) | [UX_Map_Parity_Baseline.md](UX_Map_Parity_Baseline.md) *(inventory)* · [the bill](UX_Feature_Cross_Surface_Actions.md#-the-bill-for-the-id-rule--why-simhostcgf-activation-is-not-a-flag-flip) | ☐ |
| | <a id="uxi-24"></a>**UXI-24** 🔴 | **Multi-select is not supported anywhere.** The multi-entity handler overload is a default no-op no host overrides; items act on the clicked entity only. Needs AND-over-selection visibility and fan-out execution. 🔴 **Prerequisite found: no ctrl/shift additive click-select exists anywhere** — rubber-band is the only route to a multi-selection | `RW-M` | [UXR-91](UX_Requirements.md#uxr-91) | [UX_Map_Parity_Baseline.md](UX_Map_Parity_Baseline.md) | ☐ |
| **B** | | **Entity actions** | | | | |
| | <a id="uxi-03"></a>**UXI-03** | **No shared action vocabulary.** Identity, label and ordering are re-declared per host — **`Center` 9×, `Delete` 10×** (one cased `"DELETE"`). ⭐ **Re-scoped 2026-08-10: the mechanism already exists** (`SharedContextMenuPopulator` + `IEntityActionController`, unit-tested) and **one** subsystem uses it — the mapless one. Four measured blockers stop the rest; all four are what Q26-B's per-item registration removes | `RW-M` | [UXR-89](UX_Requirements.md#uxr-89) | [UX_Feature_Entity_Action_Vocabulary.md](UX_Feature_Entity_Action_Vocabulary.md) | ✅ |
| | <a id="uxi-04"></a>**UXI-04** | **The same entity offers different actions per surface** — inspector lambdas vs map JSON vs raw-ImGui ORBAT rows (all four ORBAT panels use raw ImGui). ⭐ **The ORBAT seam is already adopted by BOTH Editor and ExCon** (`IOrbatController`+`IOrbatDataProvider`); ExCon keeps its fork because `SharedOrbatPanel`'s whole menu is **one item**. 🔴 **SimHost and CGF emit no per-entity map menu at all** — one missing registrar call, but the fix is a registry-backed gizmo, not that call | `RW-M` | [UXR-85](UX_Requirements.md#uxr-85) | [UX_Feature_Cross_Surface_Actions.md](UX_Feature_Cross_Surface_Actions.md) | ✅ |
| | <a id="uxi-25"></a>**UXI-25** | **ExCon's 434-line ORBAT fork.** Split out of [UXI-04](#uxi-04) by the user, 2026-08-10. ⚠ **Not redundant — the shared panel is impoverished** (its whole menu is *Disembark*), so this is blocked until UXI-04 gives `SharedOrbatPanel` the shared menu. Then: bind ExCon's descriptors to `IOrbatController` and retire `OrbatPanel`. 🔒 ExCon is **DDS-only, no ECS world** — bind via the facade, never via `Entity` | `RW-M` | [UXR-85](UX_Requirements.md#uxr-85) | [UXI-04 §migration](UX_Feature_Cross_Surface_Actions.md#migration) | 🔒 |
| | <a id="uxi-05"></a>**UXI-05** | **The main menu is the only surface that does not follow focus.** Windows filter by `WindowScope.{Global,PerspectiveBound}` and the map follows `SwitchMapOwner` — the menu is a flat union. ⭐ **Copy `MainToolbarManager`'s filter, not `WindowScope`** — `string? Perspective`, `null` = global. 🔴 **The union is not the registry** (Editor is its only writer, 10 items) **but four copy-pasted `BeginMainMenuBar` blocks that never check focus** — so this **is [UXI-13](#uxi-13)** seen from the other side | `RW-L` | [UXR-86](UX_Requirements.md#uxr-86) | [UX_Feature_Menu_Follows_Focus.md](UX_Feature_Menu_Follows_Focus.md) | ✅ |
| | <a id="uxi-06"></a>**UXI-06** 🔴 | **The default perspective can be a non-perspective.** `defaultPersp` is an `ISubsystem.Name`; for `--mode all` that is **`Orchestrator`**, whose windows are all `Global`/empty-perspective — so **first launch hides all 22 perspective-bound windows** (SimHost 5 · IG 8 · ExCon 6 · CGF 3) and no `fdp_windows.json` ships to mask it. ⚠ **Re-scoped after user review: dropping `BTree`/`HSM`/`Blueprint` on restore is DESIRED** (document-driven, documents not persisted) — the design now makes that deliberate | `RW-L` | — | [UX_Feature_Perspective_Restore.md](UX_Feature_Perspective_Restore.md) | ✅ |
| **C** | | **Tools** | | | | |
| | <a id="uxi-07"></a>**UXI-07** 🔴 | **A tool is not a thing.** No abstraction, no current-tool state, **six** activation idioms — `Edit`/`Route`/`Rotate` each reachable by **two pipelines in one class**. 🔴 **Two exclusive-focus arbiters share one event bus with no arbitration**, so two "exclusive" tools can act on the same drag; exclusivity is also only per-entity. Toolbar cannot show active state *even in principle*; `Select` is dead; the enum names **four deleted classes**. ⚠ **The programme's first genuinely new abstraction — prior art is empty** | `RW-M` | [UXR-81](UX_Requirements.md#uxr-81), [UXR-84](UX_Requirements.md#uxr-84) | [UX_Feature_Tool_Model.md](UX_Feature_Tool_Model.md) · [Q27](Architect_Question_27_Tool_Model.md) | 🔒 |
| **D** | | **Layout** | | | | |
| | <a id="uxi-08"></a>**UXI-08** | **No shipped default layout.** `imgui.ini` is machine-wide with no path seam; nothing seeds a new user; the default cannot be authored or committed | `RW-M` | [UXR-04](UX_Requirements.md#uxr-04) | — | ☐ |
| **E** | | **Map** | | | | |
| | <a id="uxi-09"></a>**UXI-09** 🔴 | **Camera setup copy-pasted 4×, every copy stale**, and nothing is occlusion-aware — `DockspaceLayout.CentralSize` exists and no camera reads it | `RW-M` | [UXR-18](UX_Requirements.md#uxr-18) | — | ☐ |
| | <a id="uxi-10"></a>**UXI-10** | **Map symbology seam exists and no host uses it** — every host passes `DefaultEntityShapeLibrary` | `RW-L` | — | — | ☐ |
| | <a id="uxi-11"></a>**UXI-11** | **CGF and ExCon sit outside the shared selection mechanism** — CGF has no `SelectionInteractionSystem`; ExCon uses an id list over the wire | `RW-M` | — | — | ☐ |
| **F** | | **Duplication** | | | | |
| | <a id="uxi-12"></a>**UXI-12** | **Spawn UI ×4** — `SpawnerPanel`, `MiniExConPanel`, `SimHostSpawnPanel`, plus an inline combo in ExCon's ORBAT | `RW-M` | [UXR-83](UX_Requirements.md#uxr-83) | — | ☐ |
| | <a id="uxi-13"></a>**UXI-13** | **Gizmo main-menu-bar block copy-pasted ×4**, bypassing the overload built for it — `DebugGizmoLayer.DrawMainMenu()` exists and **only a standalone viewer calls it**. ⭐ **Same defect as [UXI-05](#uxi-05)**: these four blocks *are* the unfocused menu union. UXI-05 step 2 makes them correct; this makes them one | `RW-L` | — | — | ☐ |
| | <a id="uxi-14"></a>**UXI-14** | **`PanelConstants` copied verbatim** by ExCon; **`MapLayerBits`** hand-synced with a comment admitting it | `RW-L` | — | — | ☐ |
| | <a id="uxi-15"></a>**UXI-15** | **IG runs two entity inspectors at once** — its own 78 L and the shared 593 L | `RW-L` | — | — | ☐ |
| **G** | | **Correctness & robustness** | | | | |
| | <a id="uxi-16"></a>**UXI-16** 🔴 | **No `Delete` confirms, in any of the five hosts.** Every one fires immediately | `RW-L` | [UXR-15](UX_Requirements.md#uxr-15) | — | ☐ |
| | <a id="uxi-17"></a>**UXI-17** | **Two `async void` menu handlers** — unobserved exceptions | `RW-L` | — | — | ☐ |
| | <a id="uxi-18"></a>**UXI-18** | **Editor's JSON parser reads `children` without a `ValueKind` guard** — a non-array throws `InvalidOperationException`, which its `catch (JsonException)` does not catch | `RW-L` | — | — | ☐ |
| | <a id="uxi-19"></a>**UXI-19** ⚠ | **Two presentation gizmos may match one entity** — Editor registers both; overlapping projector keys. **Unverified** — establish before treating as a defect | — | — | — | ☐ |

**Counts:** **25 issues** · **7 designed** (1 🔒 on the architect) · 8 🔴 · 1 unverified (UXI-19) · UXI-22 folded into UXI-23 · UXI-25 split out of UXI-04 (user, 2026-08-10).

## Dependency order

```
UXI-01 ──────────────────────────────────────  first, always: removes an editing trap
UXI-02 ──────────────────────────────────────  independent of UXI-07 after all
UXI-03 ───── UXI-04 ── UXI-06 ── UXI-05   ⚠ 06 BEFORE 05: menu items key on perspective ids
UXI-07 ──────────────────────────────────────  🔒 architect round Q27; the 🔴 two-arbiter fix is separable and can ship first
UXI-08, UXI-09, UXI-10, UXI-16, UXI-17, UXI-18  independent — any order
UXI-24 multi-select ── shapes UXI-03's API; UXI-11 (shared selection) precedes it
UXI-23 parity ──────── delivered BY UXI-03/04/07/11 + owns UXI-04's step 5 and its binding prerequisite
UXI-25 ExCon ORBAT ─── 🔒 blocked on UXI-04 (shared menu must exist before the fork can collapse)
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
6. ⭐ **Every design opens with a "Prior art" section** citing a row of the
   [Seam Inventory](UX_Seam_Inventory.md) — **including when the answer is "nothing exists"**. The prior
   is that the seam *already exists and is under-adopted*: eight instances this session, no
   counter-example. ⚠ A call-site scan cannot find an unadopted seam — that is what the inventory is for.
   Check the wrapper (`LambdaFoo`/`DefaultFoo`) before calling an `IFoo` unadopted.
