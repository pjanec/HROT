<!--STATUS
state: LIVE
updated: 2026-08-27
current-answer: §0 is the CURRENT quest (AQ63 subsystem-composition unification) and is where a fresh
  session STARTS. §0a is the immediately-preceding batch (--mode all parity, CE-057..064). §0-prev and
  below are HISTORY, newest first.
stale-below: everything from §0-prev down is HISTORY. ⛔ Read §0 first, then §0a.
known-conflict: ⛔ HANDOFF_Cgf_Bootstrap_Unification.md (the dispatched frame) is STALE on two points —
  its stage-1 god-facade prerequisite and its phase-0 rail wording. AQ63 §10.4 and §12 supersede both,
  deliberately. The handoff is NOT edited (rule 1: never amend a dispatched handoff).
-->
# ⭐⭐⭐ RESUME — **the UI / variable implementation lane**

> 🔒🔒 **Branch: `claude/reset-working-branch-qd1qpv`** *(re-pointed by the USER, `2026-08-23`)*. ⛔ Push
> nowhere else. ⭐ **CURRENT quest ids: `CE-` (next free `CE-070`)**; ⚠ `BP-` are this lane's HISTORICAL
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
> — **THE STANDING DESIGN: the approach, the constraints, the phase plan, and §5 = buildable phase-0 detail.** ⭐ Start here.
> **①** [`Architect_Question_63_Unify_Subsystem_Composition.md`](Architect_Question_63_Unify_Subsystem_Composition.md)
> — ⭐⭐ **§9 and §10 are USER RULINGS (canon)**; ⭐⭐ **§12 is the phase-0 venue**; ⛔⛔ **§11 is SUPERSEDED — do not quote it.**
> **②** [`batches/HANDOFF_Cgf_Bootstrap_Unification.md`](batches/HANDOFF_Cgf_Bootstrap_Unification.md) — the dispatched FRAME. ⚠ **stale on two points**, see the STATUS block.
> **③** [`Architect_Question_62_Unify_The_Composition_Root.md`](Architect_Question_62_Unify_The_Composition_Root.md) — the predecessor; ⚠ AQ63 §3 supersedes its SHAPE and STAGING.
>
> 🔒 **Branch `claude/reset-working-branch-qd1qpv`** · dispatch sha **`fd8da0967`** · rule-1b started-marker pushed (`1c4325ac5`; phase 0's own at `830fd32c7`). ⭐ ids **`CE-`**, next free **`CE-070`**.
> ⭐ **RELEARN** before acting on this file.

## ✅✅✅ 0.0 — **PHASE 0 IS DONE** *(`2026-08-27`, head `9bff523c7`)*
📄 **[`batches/REPORT_Composition_Phase0.md`](batches/REPORT_Composition_Phase0.md)** · as-built folded into
the design's **§5.6 / §5.7 / §5.8**.

| ⭐ what a next session must know, and must NOT re-derive | |
|---|---|
| ⭐⭐⭐ **The rail found a REAL CRASH on its first real run** — `CE-065`. The `E3` slice routed *"center on entity"* onto a shared system but left its **event registration** in `EditorSubsystem`, and `ClusterRunner/Program.cs:52` turns strict mode on **process-wide** ⇒ the publish threw out of CGF's ImGui context menu and killed the process. ⭐ Fixed by putting the two events on `PresentationComponentRegistry`'s ONE list *(where `SelectEntityCommand` already was — which is exactly why the sibling menu item worked)* | §5.7 |
| ⛔⛔ **`--mode cgf` ALONE CANNOT BOOT.** `DdsIdAllocator` waits 30 s for `Hrot.Orchestrator` then throws; **exit 134** before `/status`. ⇒ **exercise CGF via `--mode all` + the `Scenario` perspective.** ⚠ *"the `--mode cgf` symptoms"* is shorthand for *"CGF's symptoms"* | §5.8 |
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

## ⭐⭐⭐ 0.0c — **THE WAY FORWARD.** Start here. *(`2026-08-27`)*

> 🔒 **USER, `2026-08-27`:** *"cgf==editor is still valid here (the goal of the whole programme), which
> should resolve the question"* ⇒ ✅ **the ROLE question is RESOLVED: CGF gets the AI shell.**
> ⭐⭐⭐ **And the finer distinction turned out to be real: CGF ALREADY HAS IT, so the next item is a
> DELETION, not an adoption.** 📄 design **§5b.5** carries the full measurement and the corpus citation.

### ⭐ THE QUEUE — in order
| # | item | state |
|---|---|---|
| ~~**1**~~ | ✅ **`CE-070` — `SharedAiWindowRegistrar` DELETED** *(`2026-08-27`)*. ⭐⭐ **The build found a stronger argument than the analysis had:** its windows declare **`WindowScope.PerspectiveBound`** and it was a **flat host-level** registrar ⇒ ⛔ **it could never have worked even if a host had called it**, which closes the *"an out-of-repo host might call it"* defence. ⭐ Its rail is replaced by **its inverse** *(`AddSharedAiEditor_Registers_No_Flat_Host_Level_WindowRegistrar`)*, because a flat registrar is the shape a session re-adds by reflex | ✅ **DONE** — as-built §5b.6 |
| **1** | 🔴 ⭐⭐ **`CE-071` — the visual-asset-comparison UI is UNMOUNTED; route it to `PerspectiveWorkspaceRegistrar`.** 📐 `ComparisonSummaryPanel`/`ComparisonSidebar`: **zero** production constructions, registered into **no** `WindowManager` ⇒ the feature's UI **has never rendered on either host**. 📄 The intent is explicit — `Visual_Asset_Comparison_Detailed_Design.md:1082-1083` says both are docked windows registered as `ai_comparison_summary`/`ai_comparison_sidebar`. ⚠ `CE-070` neither caused nor worsened it *(the registrar naming them was called by nothing)* — it **stopped hiding it**. ⛔⛔ **NOT mechanical:** a **capability decision** *(which perspectives get a comparison panel; one `ComparisonSessionRegistry` or one per perspective)* ⇒ needs its own inventory + UML first. ⭐ The state classes are already railed, so the work is **composition, not behaviour** | ⭐ **READY to design** — measurement in §5b.6 |
| **2** | phase 1's other adopters: `CgfEditorShellToolbar`'s remaining **direct** callers *(the two composed sites are done)* | small |
| **3** | ⭐⭐ **phase 2** — one bundle per batch from the editor as specimen: **scenario panels → gizmos → map → AI shell → time transport**. ⛔ Each needs its **own inventory + UML before code** *(obligations ①/②)* | needs design per batch |
| **4** | open ids: `CE-062` *(blueprint live-value provider on CGF)* · `CE-063` *(`EditorMapPickAdapter` vs `CanvasMapPickAdapter` — ⛔ do not merge blind)* · `CE-047` · `CE-048` · `CE-050` *(rotating ALC flake)* · `MX-011` *(MCP lane: gizmo buffer into `PanelSnapshot`)* | unchanged |
| **5** | ⚪ **the "map shows no entities" symptom** — ⛔ still **unreproduced**, not fixed. The rail stands | watch |

### ⛔⛔ THE THREE TRAPS THIS SESSION PAID FOR — **do not re-pay them**
| # | trap | the guard |
|---|---|---|
| **①** | ⭐⭐⭐ **"the caller HAS the dependency and does not pass it"** — `BP-487` *(gizmo buffer)*, `CE-065` *(event registration)*, `CE-066` *(mission editor)*, **three times in one batch** | ⭐ before designing a shared abstraction, check whether the host **already holds** the thing and merely fails to hand it over. ⛔ Not a missing abstraction — a missing **argument** |
| **②** | ⭐⭐⭐ **THE INVERSE: a class that LOOKS like the shared thing while the shared thing is elsewhere** — `SharedAiWindowRegistrar` was DI-wired, cited in a design, and **superseded by `PerspectiveWorkspaceRegistrar`** *(⇒ DELETED, `CE-070`)* | 🔒 **before adopting any "unadopted shared" class, ask what the hosts ACTUALLY use for that job.** ⛔ In-degree 0 can mean *"somebody solved it better, over there"* |
| **④** | ⭐⭐ **A RESOLUTION RAIL PROVES A TYPE IS REGISTERED, NEVER THAT A FEATURE IS REACHED** — 5th rail-blindness instance. `AddSharedAiEditor_Resolves_…` kept a never-called class alive for months, and its container has **no production caller at all** ⇒ it asserted over a graph nobody walks | ⭐ **when deleting a rail, consider asserting its INVERSE** — the wrong shape is usually the reflex shape. ⛔ And check whether the container/graph a rail asserts over is one production actually walks |
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
| **③** | the two NEW user symptoms *(`2026-08-27`, `--mode cgf`)*: ① **the 2D map shows NO entities on some scenarios** *(e.g. `hill-attack` loads, map empty)* · ② **center-on-entity CRASHES** ⚠ **suspect: the `E3`/`CE-051` path is mine** |
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
