# Shared surfaces — co-ownership and the consult-before-touch rule

> **Two programmes edit this repo at the same time.** The **blueprint** programme
> (`docs/blueprints/`, inner loop — the graph canvases) is **actively developed in parallel sessions**.
> The **UX** programme (`docs/UX/`, outer loop — the scenario shell) is this one.
>
> 🔒 **`Hrot.ClusterRunner` must stay fully operational at all times.** Blueprint work runs against it.
> Nothing in the UX programme may leave it broken, degraded, or "fixed after the refactor".
>
> **This file is the consultation channel.** It survives context compaction, and sessions from either
> programme can read it. Proposed changes to a co-owned surface get written here **before** they are made.

## The rule

| | |
|---|---|
| ✅ **Always allowed** | **Add** files, projects, windows, registrations, layout, menu *entries the UX programme owns*. Placing and docking existing windows. **Reading** shared view-models and calling their existing public seams. |
| ⚠ **Consult first** | Any change to the **internals** of a co-owned window or panel. Adding a view-model/section seam to a shared panel. Changing a **shared menu**'s structure or an existing command's behaviour. Renaming or moving co-owned types. |
| ⛔ **Never** | Forking a shared panel ([non-goal 4](UX_Requirements.md#non-goals)). Changing `ClusterRunner`'s behaviour for the editor's convenience. Breaking the **construction kit** — the distributed `--mode` variants must keep working. |

**Why this bites and git does not catch it:** a UX change that alters a shared panel's *behaviour* can
invalidate a blueprint session's visual verification mid-flight. That is a semantic collision; branches
and merges do not detect it.

> **The structural mitigation, worth stating:** the new editor shell is a **greenfield project** — new
> files, new `.csproj` — so it is **collision-free by construction**. This is a real argument for the new
> app over repairing the old shell in place, which would have collided continuously. Keep it that way:
> when a golden-path step *can* be satisfied by placing a window rather than editing one, place it.

## Co-owned surfaces

⚠ **This list is a starting set, assembled from the opening audit. It is not proven exhaustive** — verify
against code before relying on a "not on the list, therefore safe" conclusion, and add what you find.

| Surface | Path | Also hosted by | Notes |
|---|---|---|---|
| Mission / behavior assignment panel | `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` | ExCon, CGF, IG | The Path A ↔ Path B shared panel. 815 lines |
| Entity inspector | `Fdp.Presentation` `FdpEntityInspectorWindow` + `Hrot.Editor/UI/EntityPropertyInspector.cs` | ExCon, IG, CGF, SimHost, ReplayBrowser, StrideMock | Widest reuse in the repo |
| ORBAT panels | `Hrot.ExCon/Panels/OrbatPanel.cs` (434 lines) · `Hrot.Editor/UI/EditorOrbatPanel.cs` (27-line stub) | ExCon / editor | The stub is UX-owned; **ExCon's is not** |
| Window / perspective manager | `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` | every subsystem | Layout, menus, status bar, fonts, DPI |
| Global menu registry + shell commands | `GlobalMenuRegistry`, `MenuCommandAdapter`, `ShellCommands` | every subsystem | **Menu structure is co-owned** |
| Editor composition root | `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (~4.2k lines) | — | UX-heavy, but the blueprint programme wires its windows here (`_blueprintRegistrar`, retarget hooks) |
| **Blueprint / BTree / HSM editor windows** | `Hrot.Blueprints.Editor/`, `Hrot.BTree.Editor/`, `Hrot.Hsm.Editor/`, `Hrot.Editor.AiShared/` | — | 🔴 **The active surface of the parallel programme. Place, do not touch.** |
| Cluster host | `Hrot/Runner/Hrot.ClusterRunner/` | all modes | 🔒 Must stay operational. Additive changes only, gated on its suites |
| Scenario translators | `Hrot/Subsystems/Hrot.SimHost/Serializers/` | SimHost, editor, CGF | Persistence changes need a migration, not a break |

## Proposed changes awaiting consultation

Add a row **before** making the change. Keep it short: what, why, what could break, and what the other
programme should check.

| # | Date | Surface | Proposed change | Why | Risk to the other programme | Status |
|---|---|---|---|---|---|---|
| — | — | — | *(none yet)* | | | |

Status values: `PROPOSED` · `ACKED` (other programme has seen it) · `DONE` · `WITHDRAWN`.

## Known re-sequencing caused by this constraint

| Requirement | Effect |
|---|---|
| [UXR-14](UX_Requirements.md#uxr-14) — one inspector | Requires merging ~4 windows ⇒ **in-window change** ⇒ moves behind consultation. Not early spine work |
| [UXR-20](UX_Requirements.md#uxr-20) — one behaviors section | Same. It is the *first* thing to do once in-window change is affordable, not the first thing overall |
| [UXR-04](UX_Requirements.md#uxr-04) · [UXR-06](UX_Requirements.md#uxr-06) | **Unaffected** — pure layout and registration, satisfiable additively. These lead |

Rationale and options: [Q25-F-iii](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-iii--how-do-we-combine-the-content-of-existing-windows-into-new-composite-panels)
and [Q25-F-v](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-v--how-do-we-stay-out-of-the-parallel-blueprint-work).

## ⚠ Open: does the other side read this?

**Claude cannot reach the parallel sessions.** This file only works if the blueprint programme's sessions
actually consult it, which requires either the user relaying, or a link from
[`docs/blueprints/Blueprint_Gaps_Programme_RESUME.md`](../blueprints/Blueprint_Gaps_Programme_RESUME.md)
to this file. **That link has not been added** — it edits a doc the parallel programme owns, which is
itself the kind of change this file says to consult about first. Tracked as
[Q25-F‴](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-v--how-do-we-stay-out-of-the-parallel-blueprint-work).
