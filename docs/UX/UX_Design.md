# Scenario-Authoring UX — Design (`UXD`)

> **Status: BASE — v0.4, 2026-08-06.** Structure and decision register are in place. The four opening
> questions are **answered** ([OQ-1…OQ-4](UX_Requirements.md#answered-questions)); the remaining
> structural decisions are in the architect round
> **[Q25](Architect_Question_25_Scenario_Authoring_Golden_Path.md)**.
> **Do not implement from an `OPEN` decision.**
>
> Requirements: [UX_Requirements.md](UX_Requirements.md) · Journey spec:
> [UX_Golden_Path.md](UX_Golden_Path.md) · Tasks: [UX_Task_Tracker.md](UX_Task_Tracker.md) ·
> Orientation: [UX_Programme_Briefing.md](UX_Programme_Briefing.md)

## 1. Design thesis

HROT's authoring problem is **not** missing capability. Nearly everything the golden path needs exists
somewhere in the codebase. The problem is that the capability is reachable only by knowing the
implementation: which window hosts it, which perspective owns it, which order the panels must be
touched in.

> **Therefore the design is mostly composition, not construction.**
> We are building a *spine* that the existing capability hangs off, plus the feedback layer that makes
> it trustworthy. Genuine construction is now down to three items: the recoverability net
> ([UXR-15](UX_Requirements.md#uxr-15)), entity templates ([UXR-16](UX_Requirements.md#uxr-16)), and the
> problems list ([UXR-X2](UX_Requirements.md#uxr-x2)). A general undo stack was **ruled out** — see the
> [rationale](UX_Requirements.md#uxr-17).

### Two paths, one panel set

Per [Who we are building for](UX_Requirements.md#who-we-are-building-for), the programme serves **Path A**
(authoring, editor, engineers/advanced SME) and **Path B** (runtime intervention, ExCon, ordinary SME).
Both are driven by the **same shared panels**, and forking them is a
[non-goal](UX_Requirements.md#non-goals). The design consequence:

> **Every difference between the two audiences must be expressible as a capability the host composes,
> or as disclosure within a panel — never as a duplicated panel and never as a global "mode".**

This is [UXD-06](#uxd-06), and it is the constraint that keeps Path B's stricter bar from becoming a
second UI to maintain.

### The inversion

HROT was built bottom-up: subsystem → panel → menu entry. This programme runs the other way:

```
golden path  →  what the author must see at each step  →  which panel owns it  →  what must be wired
```

Consequence: **the golden-path walkthrough is the specification**, and panels are implementation
detail. When a design question arises, the tiebreaker is "which answer makes the walkthrough shorter
for the author", not "which fits the current window layout".

### A new shell, not a repaired one ⭐

**Ruled by the user 2026-08-06 ([UXD-08](#uxd-08)):** the editor becomes its **own application with a
purpose-built shell** — fully-fledged feature-wise, **init path shared** with the cluster host so all
the internal machinery still runs, and the UI grown **step by step** by composing what mostly already
exists.

This is not cosmetic. The current shell is `LocalWindowController.OpenLocalWindow()` — ~60 lines that
loop over subsystems asking each to dump its windows into one manager, and pick the default perspective
as *"the name of the second subsystem"*. **The bag-of-windows is not a defect in the editor; it is the
correct output of a generic cluster-node window aggregator.** Panel work cannot fix that. A curated
shell can.

Two consequences for everything below:

1. **The golden path becomes the build order, not only the acceptance test.** Step *N* ships exactly the
   surface step *N* needs. Nothing enters the shell without a step that earns it.
2. **[UXR-04](UX_Requirements.md#uxr-04) stops being a fight.** A greenfield shell has no legacy layout
   to argue with, so "the default layout is a working layout" becomes a design choice instead of a
   migration.

The seam between shared init and new shell is [Q25-F](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-f--a-dedicated-editor-application-with-a-purpose-built-shell)
(Claude's lean: prove the shell behind a selector in the existing exe, then extract the shared
composition and split the exe). ⚠ **"Standalone" does not mean "no cluster machinery"** — scenario load
still publishes a `TransitionStateIntent` and Play/Stop still goes through `PreviewClusterOpHandler`, so
the new app hosts the orchestrator too. That is precisely why the shared-init constraint is right.

## 2. The five questions → the target layout

From [Briefing §3](UX_Programme_Briefing.md#3-the-frame-we-design-against). Every panel in the default
layout must own one of the five questions; a panel that owns none does not belong in the default layout.

| Question | Owner | State |
|---|---|---|
| 1. Where am I? | Title/header + play-mode chrome | to build |
| 2. What is in my world? | **Outliner** | stub — the spine |
| 3. What is this thing? | **Inspector** (with Behaviors section) | fragmented across 4 panels |
| 4. What can I do now? | Toolbar + object context menu + palette | weak / absent |
| 5. Did it work? | **Problems panel** + status pill + toasts | absent |

### 🔴 The map is not a panel — corrected 2026-08-06

**An earlier revision of this section drew the map as a docked centre panel. That was wrong, and it
mattered.** The real model, confirmed in code:

- The **2D symbolic map is rendered by Raylib across the whole OS window**, *underneath* ImGui.
- ImGui runs a **dockspace with `PassthruCentralNode`** (`Program.cs:347-349`), so the central node is
  transparent and the Raylib map shows through it.
- **ImGui windows dock along the screen edges.** The map is visible only where no ImGui window covers it.
- In the **BTree / HSM / Blueprint** perspectives the map is not visible at all — their central window is
  the graph canvas.
- 🔒 **The map stays Raylib, for speed.** Hosting it in an ImGui window (render-to-texture + image blit)
  is a [non-goal](UX_Requirements.md#non-goals). The new shell must be designed around this, not against it.

```
┌──────────────────────────────────────────────────────────────────────┐
│ menu bar · scenario name* · [PLAY ▶] [⏸] [⏭] [⏹] · mode chrome       │  ImGui
├────────────┬────────────────────────────────────────┬────────────────┤
│ OUTLINER   │ ← ── ── ── ── ── ── ── ── ── ── ── ── →│   INSPECTOR    │
│ (Q2)       │ ┊   the dockspace CENTRAL NODE is    ┊ │   (Q3)         │  ImGui docked
│            │ ┊   transparent — this is the        ┊ │   ├ Identity   │  at the EDGES
│ ORBAT tree │ ┊   EFFECTIVE MAP VIEWPORT           ┊ │   ├ Transform  │
│ names+icons│ ┊                                    ┊ │   ├ Behaviors  │
│            │ ← ── ── ── ── ── ── ── ── ── ── ── ── →│   └ Components │
├────────────┴────────────────────────────────────────┴────────────────┤
│ PROBLEMS (Q5)  │  Asset Browser  │  Hot Reload Log                   │
└──────────────────────────────────────────────────────────────────────┘
   ▲                                                                ▲
   └──── the RAYLIB MAP spans this ENTIRE rectangle, behind it all ──┘
```

**The consequence, and it is a real defect class:** the map's screen extent (the OS window) and its
*visible* extent (the central node) are different rectangles. Anything that reasons about "where the map
is" — centre-on-entity, frame-all, fit-selection, zoom-to-cursor, edge-panning, hit-testing, gizmo
placement — must use the **effective viewport**, not the window. See [UXD-30](#uxd-30) and
[UXR-18](UX_Requirements.md#uxr-18).

The graph canvases (BTree/HSM/Blueprint) keep their own perspectives — they are already good. What
changes is that **entering and leaving them is driven from the entity**, not from the Perspective menu.

## 3. Design decisions (`UXD`)

Status: `OPEN` = undecided · `LEAN` = Claude's recommendation, awaiting architect/user · `RULED` = the
user has set the direction, shape still in the architect round · `DECIDED` = ruled, safe to implement
(records who ruled and where).

**Seven of these are in the Q25 round.**
⭐ [Q25-F](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-f--a-dedicated-editor-application-with-a-purpose-built-shell)
= UXD-08 + UXD-09 — **answer this one first; it reframes Q25-D.** Then
[Q25-A](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-a--how-do-we-spend-a-cheap-recoverability-budget)
= UXD-02 · [Q25-B](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-b--how-is-an-entity-template-prefab-represented)
= UXD-04 · [Q25-C](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-c--where-does-an-asset-authored-behavior-declare-its-affinity-and-its-parameters)
= UXD-03 · [Q25-D](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-d--two-audiences-one-set-of-shared-panels-what-is-the-mechanism)
= UXD-06 · [Q25-E](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-e--where-does-the-one-problems-list-live)
= UXD-10. **Record the answers there, then flip the rows here to `DECIDED`.**

### Structural

| ID | Decision | Status | Notes |
|---|---|:--:|---|
| <a id="uxd-01"></a>**UXD-01** | The default Editor-perspective layout (§2) | `LEAN` | Proposed above. Cheap to change now, expensive later — settle before UXT work on panels. Ties [UXR-04](UX_Requirements.md#uxr-04) |
| <a id="uxd-02"></a>**UXD-02** | **Recoverability model** — what protects an author's work, given that a general undo stack is ruled out | `RULED` 🔴 | **User ruling (OQ-3): cheap safety first.** Reason is architectural, not budgetary — the same editor code runs in the simulation runtime, where undo is *semantically* impossible. Shape is [Q25-A](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-a--how-do-we-spend-a-cheap-recoverability-budget) (Claude's lean: autosave + revert + confirm, **plus** bounded single-step inverses on the 4 spatial gizmo gestures, which the runtime host simply does not register). Blocks [UXR-15](UX_Requirements.md#uxr-15), [UXR-17](UX_Requirements.md#uxr-17) |
| <a id="uxd-03"></a>**UXD-03** | **Behavior as a first-class concept** — one affinity contract, one param schema, one assignment path, over three implementations | `OPEN` → [Q25-C](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-c--where-does-an-asset-authored-behavior-declare-its-affinity-and-its-parameters) | Blocks [UXR-20](UX_Requirements.md#uxr-20), [UXR-22](UX_Requirements.md#uxr-22), [UXR-40](UX_Requirements.md#uxr-40), [UXR-41](UX_Requirements.md#uxr-41). ⚡ **Sharpened by a new finding:** affinity already exists (`BehaviorContractAttribute(name, BehaviorCategory)` → `BehaviorCatalog.GetValidBehaviors(tkbType)`) but is built by reflecting **one assembly in a static ctor** — so an asset-authored behavior can never declare affinity, which is precisely why `AppendEditorBTreeBehaviors` appends everything ungated. Same root cause fails both UXR-22 and UXR-23 |
| <a id="uxd-04"></a>**UXD-04** | **Entity template/prefab representation** + override semantics | `LEAN` → [Q25-B](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-b--how-is-an-entity-template-prefab-represented) | User: wanted, and likely cheap if built on what the scenario format already saves — **the code supports that instinct** (a scenario entity is already a self-contained bag from a pluggable translator set; blueprint assignments already persist as a portable `BlueprintAssignmentDto` list). Claude's lean: scenario-fragment representation + copy-on-place, carrying a template id from day one so live overrides remain possible later. Blocks [UXR-16](UX_Requirements.md#uxr-16) |
| <a id="uxd-05"></a>**UXD-05** | **Where the Behaviors section lives** — inside the unified Inspector, or a dedicated docked panel the Inspector links to | `OPEN` | Blocks [UXR-14](UX_Requirements.md#uxr-14), [UXR-20](UX_Requirements.md#uxr-20). Not in Q25 — decide after the walk shows how the author actually moves between map and behavior |
| <a id="uxd-06"></a>**UXD-06** | **Two audiences over one shared panel set** — how Path A and Path B differ without forking panels or inventing a global mode | `OPEN` → [Q25-D](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-d--two-audiences-one-set-of-shared-panels-what-is-the-mechanism) | Blocks [UXR-26](UX_Requirements.md#uxr-26), [UXR-73](UX_Requirements.md#uxr-73), [UXR-75](UX_Requirements.md#uxr-75). Claude's lean: **per-host composition** (the codebase's existing pattern) + disclosure within a panel; reject a global mode. ⚡ Verified: **no role/mode/expert-mode concept exists anywhere today** — this is new either way. Separately, Claude's lean is that OCC conflict handling belongs in the **service**, so no host renders a version modal |
| <a id="uxd-07"></a>**UXD-07** | **Path B gesture set** — the minimum walk-up-usable surface for runtime intervention (add entity, retask, verify) | `OPEN` | Blocks [G7](UX_Requirements.md#g7--runtime-intervention-excon). ⚠ **All of Path B is code-inferred** — what an ExCon operator sees today is untraced. Needs its own walk before design |
| <a id="uxd-08"></a>**UXD-08** | **A dedicated editor application with a purpose-built shell** — features and init path shared with the cluster host; only the UI composition is new, grown step by step along the golden path | `RULED` ⭐ → [Q25-F](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-f--a-dedicated-editor-application-with-a-purpose-built-shell) | **User ruling (2026-08-06): do it.** ⚡ **This is the cause behind the cause.** `LocalWindowController.OpenLocalWindow()` *is* the editor shell, in ~60 lines: every subsystem dumps its windows into one manager, the default perspective is literally `_subsystems.Skip(1).FirstOrDefault()?.Name`, perspectives are hardcoded cluster roles, the title is "HROT Cluster Runner", and `ScanForSubsystems` builds a DDS participant for **every** subsystem before filtering. The bag-of-windows is what this host is *for*. Cheap: the host is 2,217 lines and **no subsystem project depends on it** (only `InternalsVisibleTo`). Q25-F decides the seam (Claude's lean: **F3 → F1** staged), what the shell keeps (**G2**), how window content is combined (**H1**), and whether `--mode editor` survives (**I1**) |
| <a id="uxd-09"></a>**UXD-09** | **Panel composition rule** — how content from several existing windows becomes one new panel without forking them | `LEAN` → [Q25-F-iii](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-iii--how-do-we-combine-the-content-of-existing-windows-into-new-composite-panels) | Required by [UXR-14](UX_Requirements.md#uxr-14) and [UXR-20](UX_Requirements.md#uxr-20), which both merge ~4 windows. ⚠ **Lean revised 2026-08-06 by the parallel-development constraint** ([UXD-10b](#uxd-10b)): **place, do not edit** — H0 first for everything layout can satisfy; re-host a view-model only where the seam already exists (read-only reuse); defer section-extraction until consulted. UXR-14/UXR-20 are therefore **re-sequenced behind the consult protocol**, not dropped. ⚠ **The missing view-model seams are where an estimate will be wrong** |
| <a id="uxd-10b"></a>**UXD-10b** | **Parallel-programme safety** — how the UX programme avoids disrupting active blueprint work on shared surfaces | `RULED` 🔒 → [Q25-F-v](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-v--how-do-we-stay-out-of-the-parallel-blueprint-work) | **User constraint 2026-08-06:** `ClusterRunner` stays fully operational; blueprint development continues in parallel sessions; the new UI must be careful about changing window *content* — prefer **placing** windows — and any in-window or shared-menu change must be **synchronised/consulted** with the other sessions. Mechanism: [SHARED_SURFACES.md](SHARED_SURFACES.md) (co-ownership list + consult-before-touch + additive-only at the shell boundary). Claude's lean: **J1 + J2**. ⚠ **Open ([Q25-F‴](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-v--how-do-we-stay-out-of-the-parallel-blueprint-work)): only the user can confirm the blueprint sessions will read that file** |

### Feedback & trust

| ID | Decision | Status | Notes |
|---|---|:--:|---|
| <a id="uxd-10"></a>**UXD-10** | **Diagnostic bus** — one sink that validation, compiler, load and runtime faults all publish to, with a navigable source reference per entry | `LEAN` → [Q25-E](Architect_Question_25_Scenario_Authoring_Golden_Path.md#q25-e--where-does-the-one-problems-list-live) | Required by [UXR-X2](UX_Requirements.md#uxr-x2), [UXR-34](UX_Requirements.md#uxr-34), [UXR-62](UX_Requirements.md#uxr-62). Claude's lean: build editor-side, but define the entry contract (severity · message · source ref · navigate) so ExCon publishes into the same model later. Reuse candidates ⚠ untraced: the alert manager, `DiagnosticsWindow`, `FindResultsWindow`'s navigation model |
| <a id="uxd-11"></a>**UXD-11** | **No-dead-control enforcement mechanism** — command-registry completeness test + debug-build throwing `default:` arms | `LEAN` | Required by [UXR-X1](UX_Requirements.md#uxr-x1). Cheap, high trust yield, and it protects every later task. **Strong candidate for the first slice** |
| <a id="uxd-12"></a>**UXD-12** | **Acknowledgement mechanism** — toast/status-line convention for gesture outcomes | `OPEN` | Required by [UXR-X3](UX_Requirements.md#uxr-x3). Must not become modal spam |
| <a id="uxd-13"></a>**UXD-13** | **Play-mode chrome** — what changes visually, and the Play/Pause/Step/Stop contract wording | `LEAN` | Tint + explicit state label. Reuses correct `EditorPreviewAdapter` snapshot/rewind semantics. Consider renaming Preview → Play, keeping an Unreal-style *Simulate* distinction if needed. [UXR-31](UX_Requirements.md#uxr-31) |

### Composition & reuse

| ID | Decision | Status | Notes |
|---|---|:--:|---|
| <a id="uxd-20"></a>**UXD-20** | **Outliner build vs adopt** — grow `EditorOrbatPanel` (27-line stub) or adapt ExCon's `OrbatPanel` (434 lines, has hierarchy) | `OPEN` | Blocks [UXR-10](UX_Requirements.md#uxr-10)…[UXR-13](UX_Requirements.md#uxr-13). **Trace ExCon's panel first** — adopting may be most of the work already done. Note it targets the cluster/ExCon data path |
| <a id="uxd-21"></a>**UXD-21** | **Param authoring coverage** — extend `BehaviorUiCompiler`'s attribute vocabulary so map-pick/entity-pick are declarative, retiring the 3 special-cased behaviors and the raw-JSON fallback | `LEAN` | [UXR-23](UX_Requirements.md#uxr-23). Existing infra is good; the gap is vocabulary + a "no DTO ⇒ loud diagnostic" rule instead of a silent JSON textbox |
| <a id="uxd-22"></a>**UXD-22** | **Params storage** — structural JSON vs the current escaped string | `OPEN` | [UXR-24](UX_Requirements.md#uxr-24), [UXR-63](UX_Requirements.md#uxr-63). Migration cost: existing scenarios + `MissionPlanTranslator` + the DDS wire form. **Check the wire form before assuming this is editor-local** |
| <a id="uxd-23"></a>**UXD-23** | **Command palette** — new, or extend the existing command registry + `FindResultsWindow` pattern | `LEAN` | [UXR-05](UX_Requirements.md#uxr-05). Commands already carry `Id`/`DisplayName`/`Category`/`IsEnabled` (`EditorCommandDescriptor`) — a palette is largely a view over what exists |
| <a id="uxd-30"></a>**UXD-30** | **The effective map viewport** — how the shell tells the map where its *visible* rectangle is, so centre/frame/fit/pick operate on what the user can actually see | `LEAN` → [Q25-F-vi](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-vi--the-effective-map-viewport) | Required by [UXR-18](UX_Requirements.md#uxr-18). ⚡ **The mechanism already exists and is one assignment:** `MapCamera.Offset` is the screen point that `Camera.Target` maps to (`MapCamera.cs:29-33`) — set it to the centre of the effective viewport and `FocusOn()` centres in the visible area, with **zero** rendering change and no perf cost. 🔴 **Verified: the editor never sets `Offset` at all** (no `Camera.Offset` assignment anywhere in `Hrot.Editor`), and `MapCamera`'s ctor leaves it `Vector2.Zero` (`:62`) — so `FocusOn` should be putting the target at the window's **top-left corner, under the docked panels**. Other hosts set it to a full-window or hardcoded centre (`IgApplication.cs:617`; `CgfSubsystem.cs:577` and `SimHostVisualization.cs:226` both hardcode `1280/2, 720/2`). **Nothing anywhere is occlusion-aware.** Options + lean in Q25-F-vi |
| <a id="uxd-24"></a>**UXD-24** | **Object context menus** as the primary discovery path | `LEAN` | [UXR-25](UX_Requirements.md#uxr-25), [UXR-43](UX_Requirements.md#uxr-43), [UXR-50](UX_Requirements.md#uxr-50). Seams exist (`JsonEntityContextMenuHandler`, `ContextMenuLogic`, gizmo context menus with icon resolver) |

## 4. Sequencing principle

Ordered by **trust-per-unit-cost**, not by visibility. Rationale: every later task is verified by a
person walking the golden path — if controls still lie, that verification is worthless.

0. **Stand up the new shell** ([UXD-08](#uxd-08)) — an empty, curated editor shell over the shared init
   path, plus the default-layout mechanism. Everything after this composes *into* it, so it comes first;
   but it stays deliberately near-empty until a golden-path step earns each surface.
   **⚠ Under [UXD-10b](#uxd-10b) the early milestones are deliberately biased toward *layout* over
   *content*:** what can be achieved by placing an existing window comes first, and what needs in-window
   change waits for a consult. That is a re-ordering, not a reduction — see
   [SHARED_SURFACES.md](SHARED_SURFACES.md#known-re-sequencing-caused-by-this-constraint).
1. **Make the editor honest** — no dead controls, acknowledgement, problems panel ([UXD-11](#uxd-11),
   [UXD-10](#uxd-10), [UXD-12](#uxd-12)). Nothing downstream is verifiable without this. *A new shell
   inherits no dead controls of its own — but the composed panels bring theirs along, so the
   enforcement test still earns its keep.*
2. **Build the spine** — outliner, unified inspector, selection ([UXD-20](#uxd-20), [UXD-05](#uxd-05),
   composed per [UXD-09](#uxd-09)).
3. **Make the loop legible** — play chrome, status pill, tool state, palette, context menus
   ([UXD-13](#uxd-13), [UXD-23](#uxd-23), [UXD-24](#uxd-24)).
4. **Fix assignment** — one model, no allocator internals, typed params ([UXD-03](#uxd-03),
   [UXD-21](#uxd-21)).
5. **Structural bets** — recoverability net, templates ([UXD-02](#uxd-02), [UXD-04](#uxd-04)).
   Architect-gated.
6. **Prove the round-trip** — an explicit save/reload/run regression, then the walkthrough doc
   ([UXR-61](UX_Requirements.md#uxr-61), [UXR-X6](UX_Requirements.md#uxr-x6)).

Milestones are not yet cut into tasks — that follows the reconnaissance walk (see
[UX_RESUME.md](UX_RESUME.md#next-up)).

## 5. Constraints

| Constraint | Consequence for design |
|---|---|
| 🔒 **`ClusterRunner` must stay fully operational, continuously.** Blueprint development runs against it in parallel sessions | No "will fix after the refactor" states. Extraction work is gated on ClusterRunner's own suites staying green. Favours the staged F3→F1 seam, where the first stage cannot break the host at all |
| 🔒 **The construction kit survives.** The system must keep composing network-distributed variants exactly as today (`--mode orchestrator,simhost,cgf`, `ig`, `excon`, `all`) | A shared host library keeps **generic** subsystem composition with real network factories. **The editor app is one preset of the kit, not a replacement.** No answer may reshape the host around the editor's needs |
| **The editor is networkless, all-in-one, in-process — by design, and already enforced in code** | `EditorSubsystem` discards the injected `INetworkFactory` (`:557`) and ⚡ **never reads the offline one either** — `:180` is a dead field in a `sealed`, non-`partial` class. So the editor preset drops network composition **entirely**, removing nothing it reads. ⚠ Make the intent explicit rather than a discarded positional parameter, so nobody "helpfully" wires it |
| ⚡ **CORRECTED 2026-08-10 — an editor-side control plane exists.** *Not* intent: the AI Debug API (`DebugApiHost`, a loopback `HttpListener` inside `Hrot.Editor`) + its MCP proxy are on `feat/ai-debug-api` and a [parallel session is porting them](../mcp-port/MCP_PORT_RESUME.md) | ⇒ **"networkless" means no DDS / no cluster transport, never "no sockets".** Design the shell so hosting the API is composition, not surgery. 🔒 It wires `EditorSubsystem.cs` **first** — [sequencing rule](../SESSION_SYNC.md#sequencing-rule) |
| ⚠ **Shared panels have parallel owners** ([UXD-10b](#uxd-10b)) | **Place, do not edit.** Additive at the shell boundary; in-window and shared-menu changes go through [SHARED_SURFACES.md](SHARED_SURFACES.md) first |
| 🔒 **The 2D symbolic map is Raylib, full-OS-window, rendered BEHIND ImGui** — for speed. ImGui docks at the edges over a `PassthruCentralNode` dockspace | The map is **not** a panel and must not become one. Its *visible* rect is the dockspace central node, not the window — every centre/frame/fit/pick must use the effective viewport ([UXD-30](#uxd-30)). Render-to-texture into an ImGui window is a [non-goal](UX_Requirements.md#non-goals) |
| **The map is absent from the BTree/HSM/Blueprint perspectives** — their central window is the graph | Map-dependent affordances belong to the Scenario perspective only. Do not put map-reading UI where there is no map |
| **ImGui** (immediate-mode) via `Fdp.Presentation.WindowManager` | No retained widget tree; panel state is explicit. Layout is docking + perspectives — work with that machinery, do not fork it |
| Editor is **one subsystem among many** (`--mode editor`), co-locating Brain + Muscle | Author-facing changes must not regress the ExCon/IG/CGF surfaces that share these panels |
| Panels are **shared across subsystems** (`MissionPanel`, `FdpEntityInspectorWindow`, ORBAT) | Prefer additive composition over editing shared panels in place; verify the other hosts |
| **Headless-testable seams** are the house pattern (logic in `Handle*` methods, ImGui in the composition root) | Keep it. It is why this codebase can be tested at all — and [UXD-11](#uxd-11) depends on it |
| Scenario JSON has a **migration adapter** with schema stamping and sidecars | Persistence changes ([UXD-22](#uxd-22)) must ship a migration, not a break |
| The **architect cannot be reached by Claude** | Every `OPEN` decision above needs a relayed round. Batch them into as few architect questions as possible |

## 6. Risks

| Risk | Mitigation |
|---|---|
| **Shared panels regress other subsystems.** ExCon/IG/CGF consume the same panels | Enumerate every host of a panel before touching it; run their test suites as gates |
| **The undo decision blocks everything else** | Sequenced last among structural work (§4); the cheap-safety option (c) is a valid ship |
| **Cosmetic churn mistaken for progress.** Icons and tints feel productive and change nothing | Every task states which `UXR` it closes and which of the five questions it improves. No `UXR` ⇒ no task |
| **Scope creep into runtime capability** | Non-goals list in [UX_Requirements.md](UX_Requirements.md#non-goals) is binding |
| **Doc drift** — this programme's own registers becoming misleading, as happened to ~6 blueprint docs | The tracker is the only status source; every task ends by updating it in the same commit |
| **A UX change that tests green and still feels bad** | Mandatory visual verification per task ([Briefing §5.11](UX_Programme_Briefing.md#511-visual-verification-is-mandatory)) |
