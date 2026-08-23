<!--STATUS
state: LIVE
build-state: SPLIT — §3 (Part A, the rename + the validation prerequisite) is READY-TO-BUILD and carries
  the UML in §5. §4 (Part B, CGF grows asset perspectives) is DESIGN: it lands feature by feature with the
  unification, and its first slice needs the freeze decision in §8.
updated: 2026-08-23
current-answer: the whole file. §3 is what to build now; §4 is the target it builds toward.
design-basis: PROGRAMME_Unification_And_Harness.md D1+D2 (user decisions, 2026-08-23) ·
  UX/UX_Glossary_Host_Mode_Subsystem.md (process · mode · subsystem · perspective — perspective is the
  finer key) · UX/UX_Feature_Perspective_Restore.md §3 (the unknown-id refusal, designed, never built).
known-conflict: none. ⚠ This doc's INVENTORY contradicts the perspective LIST implied by
  UX_Glossary_Host_Mode_Subsystem.md and UX_Feature_Perspective_Restore.md — see §1's ⚠ row; those docs
  name four editor perspectives and there are six.
-->
# DESIGN — perspective unification: make the editor's and the cluster's perspective names the same

> ⭐⭐⭐ **Why:** conformance can only compare like with like. Today the editor shows
> `Editor · BTree · HSM · Blueprint` and a CGF-hosting runner shows `CGF`. ⇒ **nothing lines up**, and a
> cross-host check has to translate rather than compare. 📄 Charter **D1**/**D2**.

## 1. ⭐ INVENTORY — measured `2026-08-23` at `129e80505`

```bash
grep -rn "OwningPerspective\s*=" --include=*.cs Hrot/ FDP/           # who sets it: exactly ONE place
grep -rhoE ': base\("[^"]+", *"[^"]*", *"[A-Za-z]+"' --include=*.cs  # every perspective literal
grep -rn "CreateRegistrar(" --include=*.cs Hrot/ | grep -v Tests     # the asset-perspective factory calls
```

| fact | value |
|---|---|
| ⭐⭐ **`ManagedWindow(id, title, owningPerspective, scope)`** | the perspective is a **plain ctor string**; `OwningPerspective` is assigned in exactly one place *(`ManagedWindow.cs:141`)* |
| ⭐⭐⭐ **`GetPerspectives()` DERIVES the list** | distinct `OwningPerspective` over registered `PerspectiveBound` windows ⇒ ⛔ **no registry to extend, and an EMPTY perspective is not representable** |
| **visibility rule** | `Global \|\| isPinned \|\| OwningPerspective == currentPerspective` *(`ManagedWindow.cs:160-162`)* — plain string equality |
| ⚠⚠ **perspective literals in PRODUCTION** | `Editor` **8** · `ExCon` **7** · `IG` **5** · `SimHost` **2** · **`Authoring` 2** · **`Analysis` 2** · `Blueprint` **1** · `CGF` **4** *(multi-line registrations)* |
| ⚠⚠ **⇒ there are SIX editor-side perspectives, not four** | 🔴 **`Authoring`** *(`anim_backend_inspector` · `utility_decision_editor`)* and **`Analysis`** *(`ai_comparison_summary` · `ai_comparison_sidebar`)* are **undocumented, have no `perspectiveMap` entry**, and `GetPerspectives()` returns them. ⛔ Any rename inventory or per-perspective golden that assumes four is already wrong |
| ⭐⭐ **the asset perspectives are created by a PARAMETERISED registrar** | `PerspectiveWorkspaceServices.CreateRegistrar(perspectiveName, …)`, called **three times** — `EditorSubsystem.cs:2688` *(BTree)*, `:2696` *(HSM)*, `:2748` *(Blueprint)*. ⭐ Its doc: *"binding each to … the correct `OwningPerspective` so each perspective remembers its own dock layout independently"* |
| **CGF's windows** | 4, all perspective `"CGF"` — `cgf_fdp_inspector` · `cgf_fdp_events` · `cgf_architecture_diagnostics` · `cgf_system_profiler`. ⚠ **All DIAGNOSTICS, none an asset editor** |
| ⛔ **CGF does NOT reference `Hrot.Editor.AiShared`** | it *does* reference `Hrot.Blueprints.Editor` ⇒ the blueprint asset editor is already reachable; the shared side-panels are not |
| **map ownership** | `perspectiveMap` *(`Program.cs:248-254`)* = `{IG, SimHost, ExCon, CGF, StrideMock}` → subsystem name, a `Dictionary<string,string>` ⇒ ⭐ **many perspectives → one subsystem is already the supported shape** |
| **gizmo follow** | `PerspectiveCoordinatorSystem` keeps `gizmoControllables` **keyed by perspective**, and on each switch does `RemoveListener(outgoing)` then `AddListener(incoming)` |
| **persisted layout** | `layout/default/fdp_windows.json` names a perspective in **exactly one field** — `ActivePerspective` *(currently `"Blueprint"`)*; per-window entries are `IsOpen`/`IsPinned` only. `layout/default/imgui.ini` names **none** |
| ⭐ **rename size** | **8** window registrations use `"Editor"`; **33** non-test and **44** test occurrences of the literal `"Editor"` overall *(⚠ not all are the perspective — needs per-site judgement, not sed)* |

## 2. ⭐⭐ THE MECHANISM — how a perspective comes to exist

⭐⭐⭐ **A perspective is not declared anywhere. It exists because a window claims it.**

| step | |
|---|---|
| **1** | a window is constructed with `owningPerspective: "X"` and `WindowScope.PerspectiveBound` |
| **2** | `GetPerspectives()` now returns `X`; the toolbar/menu offer it |
| **3** | ⭐ *(cluster only)* `perspectiveMap["X"] = "<subsystem>"` makes `SwitchMapOwner` hand that subsystem the map |
| **4** | ⭐ *(optional)* `RegisterPerspectiveLabel("X", "Display Name")` and `RegisterPerspectiveIconKey` |

⇒ ⭐⭐ **Consequence for the whole programme: CGF's perspective list GROWS AUTOMATICALLY, feature by
feature, as each ported window lands with the right name.** ⛔ There is no "create the perspective" step to
schedule, and no half-built empty perspective to worry about.

## 3. ⭐⭐⭐ PART A — the rename *(`READY-TO-BUILD`)*

> ⭐ Charter **D2**: rename the editor's perspective **id** `Editor` → `Scenario`. Today `"Scenario"` is
> only a display **label** over the `Editor` id, so ids would not have matched across hosts.

### ⛔⛔ A0 — THE PREREQUISITE: `SwitchPerspective` must refuse an unknown id

📐 **Measured:** `WindowManager.SwitchPerspective` accepts **any** string, sets `CurrentPerspective`, and
fires. ⇒ after the rename, a developer's **own** stored layout still says `ActivePerspective: "Editor"`,
which selects a perspective **no window claims** ⇒ 🔴 **every `PerspectiveBound` window fails the visibility
gate and the UI comes up blank, with no error and no log line.**

⭐ `UX_Feature_Perspective_Restore.md` §3 already specifies the fix — *"Log and no-op instead of silently
hiding every bound window"* — ⛔ **and it was never implemented.** ⇒ **A0 builds it, and A1 does not start
until it is green.**

| A0 | |
|---|---|
| **do** | in `SwitchPerspective`, refuse a name not in `GetPerspectives()`: **log and no-op**. ⭐ Also make the restore path fall back to a valid perspective rather than trusting the file |
| **rail** | switch to `"NoSuchPerspective"` ⇒ `CurrentPerspective` **unchanged**, one log line, windows still drawn |
| ⚠ **lane** | `WindowManager` is `FDP/Engine/Fdp.Presentation` — **shared**. This is a deliberate, sanctioned edit *(it is A0's whole point)*, ⛔ not a drive-by |

### A1–A4 — the rename itself

| # | step | note |
|---|---|---|
| **A1** | rename the **id** at the 8 `"Editor"` window registrations → `"Scenario"` | ⛔ **per-site judgement**: of 33 non-test occurrences most are *not* the perspective *(subsystem name, mode token, type names)*. ⭐ The 8 `: base(…, "Editor", …)` sites are the perspective |
| **A2** | keep the **display label** working — `RegisterPerspectiveLabel("Scenario", "Scenario")` or drop the now-redundant alias | ⭐ the label mechanism already exists; the rename makes id and label agree for the first time |
| **A3** | update `layout/default/fdp_windows.json` **only if** the shipped default should open on Scenario | 📐 it currently says `"Blueprint"`, so **no migration is required** — ⛔ do not invent one |
| **A4** | follow the rename through the **44 test occurrences** | ⭐ several assert the perspective list; ⚠ **and any test asserting "four perspectives" is already wrong** *(§1)* — fix the count to the measured set, do not delete the assertion |

⭐⭐ **Not in Part A:** ⛔ the `Authoring`/`Analysis` perspectives are left exactly as they are. They are a
separate finding *(§8-E)*; renaming them is not required to make editor and CGF agree.

## 4. ⭐⭐ PART B — CGF grows the asset perspectives *(`DESIGN`)*

⭐⭐⭐ **The reuse vehicle already exists and is already parameterised by perspective name:**
`PerspectiveWorkspaceServices.CreateRegistrar("BTree", …)`. ⇒ **the unification is CGF calling it**, not a
reimplementation.

| what | status |
|---|---|
| ⭐ the registrar, per perspective | ✅ **exists**, one production factory, three calls today |
| ⛔ **CGF → `Hrot.Editor.AiShared` reference** | **absent** — the first real cost |
| ⛔ **`PerspectiveWorkspaceServices`' dependencies satisfiable in CGF** | ⚠ **unmeasured** — catalog, refactor service, debug registry, breakpoint manager, validators, live-value provider… ⭐ several are naturally absent in CGF *(charter **D3**: absent capabilities are tolerated and reported, not faked)* |
| ⭐ **`perspectiveMap` entries** | `{Scenario, BTree, HSM, Blueprint} → "CGF"` — additive, many→one already supported |
| ⭐ **`gizmoControllables` entries** | one per new perspective name, all pointing at CGF's controllable |

### ⭐ CGF keeps its diagnostics perspective — **add, do not replace**

📐 CGF's four existing windows are **diagnostics, not asset editors**. ⇒ ⭐⭐ **recommendation: CGF ends up
owning FIVE perspectives** — `Scenario · BTree · HSM · Blueprint` *(as features land)* **+ `CGF`** for the
diagnostics it already has. ⭐ The user anticipated exactly this: *"or in future maybe also other
perspectives, still belonging to the cgf."*
⇒ ⛔ **Nothing moves on day one**, `perspectiveMap["CGF"]` keeps working, and each asset perspective appears
the moment its first window lands.

## 5. ⭐⭐⭐ UML — Part A

```mermaid
classDiagram
    direction LR

    class ManagedWindow {
        <<exists · Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs>>
        +string Id
        +string OwningPerspective
        +WindowScope Scope
        +bool IsPinned
    }
    class WindowManager {
        <<exists · same folder · A0 EDITS THIS>>
        +string CurrentPerspective
        +GetPerspectives() IReadOnlyList
        +SwitchPerspective(name) void
        +RegisterPerspectiveLabel(p, label) void
        +IsPerspectiveActive(p) bool
    }
    class PerspectiveWorkspaceServices {
        <<exists · Hrot.Editor.AiShared/Windows>>
        +CreateRegistrar(perspectiveName, ...) PerspectiveWorkspaceRegistrar
    }
    class PerspectiveWorkspaceRegistrar {
        <<exists · one per perspective>>
        +string PerspectiveName
        +RegisterExtraWindow(w) void
    }
    class PerspectiveCoordinatorSystem {
        <<exists · Hrot.ClusterRunner/Systems>>
        +string CurrentPerspective
        +ProcessPendingEvents() void
    }
    class EditorSubsystem {
        <<exists · calls CreateRegistrar 3x at 2688/2696/2748>>
    }
    class CgfSubsystem {
        <<exists · Part B adds registrar calls here>>
        +RegisterWindows(wm) void
    }

    WindowManager "1" *-- "many" ManagedWindow : owns
    PerspectiveWorkspaceServices ..> PerspectiveWorkspaceRegistrar : creates per perspective
    PerspectiveWorkspaceRegistrar ..> ManagedWindow : registers with OwningPerspective
    EditorSubsystem ..> PerspectiveWorkspaceServices : BTree HSM Blueprint
    CgfSubsystem ..> PerspectiveWorkspaceServices : Part B
    WindowManager ..> PerspectiveCoordinatorSystem : OnPerspectiveChanged
```

```mermaid
sequenceDiagram
    autonumber
    participant U as user or restore
    participant W as WindowManager
    participant G as GetPerspectives
    participant M as ManagedWindow
    participant P as PerspectiveCoordinatorSystem

    Note over U,W: A0 — the refusal that Part A depends on
    U->>W: SwitchPerspective "Editor" (a stale stored id)
    W->>G: is it a claimed perspective?
    G-->>W: no — claimed set is Scenario BTree HSM Blueprint Authoring Analysis
    W-->>U: log and no-op, CurrentPerspective unchanged
    Note over W,M: without A0 this would succeed and every bound window would hide

    U->>W: SwitchPerspective "Scenario"
    W->>G: is it claimed?
    G-->>W: yes
    W->>W: CurrentPerspective = Scenario
    W->>P: OnPerspectiveChanged old new
    P->>P: queue, then SwitchMapOwner on the next frame
    M->>M: visible if Global or pinned or owning == Scenario
```

## 6. ⚠ RISKS TO MEASURE — **before Part B, not before Part A**

| # | risk | the measurement |
|---|---|---|
| **R1** | ⭐ **gizmo listener churn on an intra-subsystem switch.** With four perspectives mapped to one CGF controllable, `Scenario → BTree` does `RemoveListener` then `AddListener` on the **same object** | does a listener count reaching 0 have any side effect *(teardown, buffer clear)*? ⛔ If yes, the gizmo feed dies on every intra-CGF switch |
| **R2** | **`SwitchMapOwner("CGF")` fires on every intra-CGF switch** | is it idempotent w.r.t. camera and selection, or does it reset them? |
| **R3** | ⚠ **`PerspectiveWorkspaceServices`' dependency set in CGF** | construct it in a CGF-shaped host and see what is genuinely absent ⇒ feeds charter **D3**/**D4** |

## 7. ⭐ WHAT THIS BUYS

| | |
|---|---|
| ⭐⭐ **conformance compares like with like** | same perspective name in both modes ⇒ ⛔ no id translation layer *(the thing the withdrawn Batch A was going to build)* |
| ⭐⭐ **the granular check the charter needs** | one feature = one perspective + one `PanelKind` ⇒ its golden moves alone |
| ⭐ **a blank-UI failure mode is closed** | A0 fixes a defect that exists **today**, independent of the rename |

## 8. ⭐⭐ SUB-QUESTIONS — **recommendation each; the user approves**

| # | question | ⭐ my lean |
|---|---|---|
| **51b-A** | Build **A0 before A1**? | ⭐⭐⭐ **yes, and it is not optional** — without it the rename can brick a developer's UI silently |
| **51b-B** | CGF **adds** asset perspectives and **keeps** `CGF` for diagnostics? | ⭐⭐ **yes** — its four windows are not asset-scoped, nothing has to move, and each new perspective appears with its first window |
| **51b-C** | Part B touches `Hrot.Editor.AiShared` — the **frozen** area *(`R-128`)*. Whose lane? | ⚠ **the UI/variable lane's**, or the freeze is narrowed for this. ⛔ **Do not have two sessions build it** — that is the exact thing the freeze exists to prevent |
| **51b-D** | Does Part A wait for the lanes to be idle? | ⭐ **no** — 8 registration sites plus tests is small and touches nothing either lane is in. ⚠ **Part B does** |
| **51b-E** | The undocumented **`Authoring`** / **`Analysis`** perspectives | ⭐ **leave them, document them, and fix any test that assumes four.** ⛔ Not part of this design — but ⚠ **they will show up in `GET /perspectives` and in per-perspective goldens**, so they must stop being a surprise |
