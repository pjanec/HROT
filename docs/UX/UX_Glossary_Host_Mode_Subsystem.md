# Glossary — process · mode · subsystem (and the word "host")

> **Established from code, 2026-08-10**, because the user asked: *"I use these terms interchangeably and
> maybe they are not [equal]."*
>
> **Short answer: they are not equal.** The equivalence holds for the modes you work in daily and breaks
> for composite modes — and the design depends on the distinction.

## The three things

| Term | What it actually is | How many |
|---|---|---|
| **Process** | one `Hrot.ClusterRunner` run | 1 |
| **Mode** | the `--mode` **CLI selector** — a token or comma-list resolving to a *set* of subsystems | 1 selector per process |
| **Subsystem** | an `ISubsystem` implementation. **The unit that composes UI**: owns its map canvas, gizmo registries, action registry, selection state, and registers its own windows | **1..N per process** |

## Why mode ≠ subsystem

| Case | Evidence |
|---|---|
| `--mode all` / `demo` expands to **five** subsystems | `HrotRunnerConfiguration.cs:77-78` — orchestrator, simhost, ig, excon, cgf |
| Modes are **comma-composable** — `--mode orchestrator,simhost,cgf` | `:86-88` |
| `ios` and `excon` are **two tokens, one subsystem** | `:85` — a legacy alias |
| `migrate` maps to **no subsystem at all** | `Program.cs:158-171` — `MigrateMode` is not an `ISubsystem` |
| `ci` is **special-cased**, bypassing the name lookup | `Program.cs:127-156` |
| `PerspectiveUpdateSubsystem` is injected **always**, for every mode | `Program.cs:177,212` — a subsystem with no mode token |

⇒ **Mode is a selector; subsystem is the thing selected.** The mapping is neither one-to-one nor total in
either direction.

### Where your mental model *does* hold

For **`editor`, `simhost`, `cgf`, `ig`** run singly — which is how they are worked on daily — one mode
token selects exactly one UI-composing subsystem, so mode ≈ subsystem is a safe shorthand.
✅ **It is only wrong for composite modes** (`all`, comma-lists) and the two non-subsystem modes.

⚠ Note also that **`editor` and `replaybrowser` are validated as standalone** (`:127-141`) — they can
never share a process. So the Editor is *always* a single-subsystem process, which is why the shorthand
never bites you there.

## ⭐ The distinction that actually matters: what is owned per-subsystem vs per-process

**This is the load-bearing part.** Verified by counting construction sites:

| Owned **per subsystem** | Owned **once per process** |
|---|---|
| `MapCanvas` — IG, CGF, SimHost, Editor, ReplayBrowser each construct their own | **`WindowManager`** — exactly one, `LocalWindowController.cs:45` |
| `GlobalActionRegistry` — Editor `:1135`, SimHost `SimHostApp.cs:359` *(CGF constructs none — that is [UXI-23](UX_Issues.md#uxi-23))* | **`GlobalMenuRegistry`** and the main menu bar |
| Gizmo registries, stateless registries, gizmo settings | The ImGui context and the docking layout |
| Selection state, inspector state | `imgui.ini` and `fdp_windows.json` |
| The windows it registers | The **current perspective** |

⇒ **In `--mode all`, three map canvases exist in one process** (simhost, ig, cgf). That is precisely why
`SubsystemOrchestrator.SwitchMapOwner` exists and why `DrawWorldAll` lets **only the active owner draw**
— see [§5b of the assessment](UX_Current_UI_Architecture.md#5b-how-perspective-switching-actually-works).

## 🔴 The word "host" — I created an ambiguity, and I own it

The repo already uses **"host"** for the **process** — *"the cluster host"*, *"a generic cluster-node
window aggregator"*. **I have been using it for the subsystem.** Both readings appear across
`docs/UX/`, which is exactly the confusion this glossary exists to end.

> ### Vocabulary from here
>
> | Use | For |
> |---|---|
> | **process** *(or "the cluster host")* | one `ClusterRunner` run |
> | **mode** | the `--mode` selector, and nothing else |
> | **subsystem** | the UI-composing unit — say this where I have been saying "host" |

⚠ **Existing docs are not being mass-renamed** — the churn is not worth it. Read "host" in
`docs/UX/*` as **subsystem** unless it is plainly talking about `ClusterRunner` itself.

## What this changes in the design

**The "mode axis" is misnamed — it is the *subsystem* axis.** Registration happens in a *subsystem's*
composition root, not once per process, so in `--mode all` five subsystems each register their own set
into their own registries.

| Axis | Correct name | Where it lives |
|---|---|---|
| ~~Mode~~ → **Subsystem** | the predicates and handlers **a subsystem** binds | that subsystem's composition root |
| **Perspective** | a condition on the registration | runtime, per process |
| **Data** | component presence, per entity | the world |

**One consequence worth carrying:** [UXR-90](UX_Requirements.md#uxr-90) parity is between *subsystems* —
Editor, SimHost, CGF — which happen to be single-subsystem modes in normal use. Stating it as "modes" is
the shorthand, not the mechanism.

## ⭐ Co-running subsystems: independent, and **focus follows perspective**

> **User, 2026-08-10:** *"When we run multiple subsystems at once they are each **independent, as if
> running in its own process**. They are switched visually using perspectives — just one subsystem
> 'focused'. Main-menu subsystem-dependent parts, context menus and map display need to respect which
> subsystem is currently focused. So technically menu and map are process-related, but need to be
> **reconfigured based on the current perspective**."*

**Two invariants follow, and they are testable:**

1. 🔒 **Co-running subsystems never couple.** Separate worlds, registries, selection, canvases. Anything
   that assumes otherwise is a bug — this is why per-subsystem registries are correct, not accidental.
2. 🔒 **The process-level singletons must present only the *focused* subsystem's contributions**, with
   the focus derived from the current perspective.

### ✅ Two of the three surfaces already do this

| Surface | Focus follows perspective? | Mechanism |
|---|:--:|---|
| **Windows** | ✅ | `WindowScope.{Global, PerspectiveBound}` + `OwningPerspective == CurrentPerspective` (`ManagedWindow.cs:154-165`) |
| **Map** | ✅ | `perspectiveMap` (perspective → subsystem, `Program.cs:244-251`) → `SwitchMapOwner` → **only the active owner's `DrawWorld` runs** |
| **Context menus** | ✅ effectively | drawn by the focused subsystem's own gizmo layer / panels, from its own registries |
| **Main menu** | ❌ **no filter of any kind** | flat union of everything every composed subsystem registered |

> ### ⇒ The menu is the only surface that does not implement the model
>
> **So [UXI-05](UX_Issues.md#uxi-05) is not "invent a perspective filter" — it is "give menu items the
> `WindowScope` treatment that windows already have".** Same two-tier rule, same key, already proven in
> production one layer over:
>
> | Tier | Menu items |
> |---|---|
> | **Global** | File, Settings, Help, Window — always shown |
> | **PerspectiveBound** | subsystem-dependent items — shown only when their perspective is focused |

⚠ **Key on the *perspective*, not the subsystem.** Perspective is the finer key and degenerates to
subsystem for the cluster roles, while also covering the Editor's internal BTree/HSM/Blueprint
perspectives (⚠ *"Scenario" is a display **label** over the `Editor` id, not a fourth perspective*) — which the subsystem-keyed `perspectiveMap` deliberately does not list.

> ### ⚠ Correction to the previous revision of this document
>
> An earlier version said the per-process menu vs per-subsystem registries was *"an asymmetry… why a
> per-subsystem filter is not enough on its own"*. **That was confused.** The menu being per-process is
> **correct by design**; what is missing is simply that its contents are never filtered by focus. There
> is no asymmetry to resolve — only a filter to add.
