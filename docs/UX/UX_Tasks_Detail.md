# Scenario-Authoring UX — Task Detail (`UXT`)

> **Status: BASE — empty register, 2026-08-06.** Tasks are cut from the
> **[golden-path walk](UX_Golden_Path.md#deviation-log)**, which has **not yet been performed** (it needs
> a Windows session — see [UX_RESUME.md](UX_RESUME.md#next-up)).
>
> Checklist view: [UX_Task_Tracker.md](UX_Task_Tracker.md) · Scope: [UX_Requirements.md](UX_Requirements.md) ·
> Journey spec: [UX_Golden_Path.md](UX_Golden_Path.md) ·
> Design: [UX_Design.md](UX_Design.md) · Orientation: [UX_Programme_Briefing.md](UX_Programme_Briefing.md)

## How this doc works

One `<a id="uxt-nn">` anchored entry per task, so the tracker can deep-link every row (`#uxt-nn`).
**This doc holds the evidence and the outcome; the tracker holds only status.**

### Rules

1. **Every task traces upward.** A task with no `UXR` reference does not belong in this programme.
   A task whose design decision is still `OPEN` in [UX_Design.md](UX_Design.md) is **not ready to
   start** — say so in its Status rather than starting it.
2. **Evidence is code, not doc claims.** Cite `file.cs:line`. The register that opened this programme
   was assembled from a code scan, but *the blueprint audit was wrong ten times* — re-derive before
   building, and correct the entry in the same commit if it was wrong.
3. **`DONE` notes record what was actually observed**, including the visual check
   ([Briefing §5.11](UX_Programme_Briefing.md#511-visual-verification-is-mandatory)) and the
   revert-to-red confirmation ([§5.5](UX_Programme_Briefing.md#55-revert-to-watch-it-go-red)).
   A `DONE` note without a visual observation is incomplete.
4. **Wrong estimates get recorded**, not quietly corrected — see [Corrections](#corrections). The
   blueprint programme's estimate-failure table was one of its most useful artefacts.

### Complexity scale

Same scale as the blueprint programme, so estimates stay comparable:

| Code | Meaning |
|---|---|
| `WIRING` | Call existing code. No new logic. |
| `RW-L` | Real work, low — ≲150 lines, no new concepts. |
| `RW-M` | Real work, medium — new panel/component, some design. |
| `RW-H` | Real work, high — new subsystem, or an architect decision first. |

🔴 marks a correctness / data-loss / trust defect rather than an enhancement.

### Entry template

Copy this block verbatim for a new task.

```markdown
### <a id="uxt-nn"></a>UXT-nn — <short imperative title>

| | |
|---|---|
| **Requirement** | [UXR-nn](UX_Requirements.md#uxr-nn) — <one-line restatement> |
| **Design** | [UXD-nn](UX_Design.md#uxd-nn) (status must be `DECIDED` or `LEAN` to start) |
| **Question improved** | 1 Where am I / 2 What's in my world / 3 What is this / 4 What can I do / 5 Did it work |
| **Complexity** | `WIRING` \| `RW-L` \| `RW-M` \| `RW-H` |
| **Status** | `NOT READY` \| `READY` \| `IN PROGRESS` \| `DONE` \| `REFUTED` |
| **Delegation** | hands-on \| Sonnet subagent (per [Briefing §5.1](UX_Programme_Briefing.md#51-model-delegation-token-thrift)) |

**Evidence** — what is broken, with `file.cs:line` citations. Verified or ⚠ unverified.

**Scope** — what changes. Name every file expected to change, and every other host of a shared panel.

**Acceptance** — the observable test a person performs in the running editor. Must satisfy the
requirement's acceptance plus A1 (≤2 clicks, no window detour) and A2 (outcome stated).

**Out of scope** — what a reader might reasonably assume is included and is not.

**Gates** — which suites must be green.

**DONE (date, commit)** — what shipped, what the visual check showed, revert-to-red confirmation,
anything the task exposed. Added on completion.
```

---

## Open tasks

*None yet — the register opens after the golden-path walk.*

## Done tasks

*None yet.*

---

## Corrections

<a id="corrections"></a>

Where this programme's own claims turned out to be wrong. **Add a row rather than silently editing** —
the pattern of failure is more useful than a clean record.

| # | Claim | Reality | Found by |
|---|---|---|---|
| 1 | *"The MCP server does not exist yet"* — [Q25](Architect_Question_25_Scenario_Authoring_Golden_Path.md), pre-F-i riders | It exists on `origin/feat/ai-debug-api` (`d7b2a6e1`). **The claim was corrected in `UX_RESUME.md` on 2026-08-06 but Q25 was not updated with it**, so the architect question contradicted its own programme's entry doc for four days. ⚠ *The failure was not the wrong claim — it was correcting it in one doc only.* Grepping the old wording found **a second stale copy** in `UX_Design.md` §5 that the first correction also missed. When a fact flips, grep the programme folder for the old wording before committing | 2026-08-10 pre-seam check |
| 5 | 🔴 *"With no outliner and **no right-click affordances on objects**, choosing a window becomes the interaction model — that is the root cause"* — [RESUME §0](UX_RESUME.md), [Briefing §2](UX_Programme_Briefing.md) | **The second half is false.** ~26 production context-menu sites exist; the Editor alone registers **5** `IEntityContextMenuHandler`s, plus map menus that vary by entity state; the graph canvases hold the richest such system in the repo. True **only** of the 27-line `EditorOrbatPanel` the claim was generalised from. ⚠ *This was the programme's stated **root cause** — an absence claim generalised from one file, load-bearing for the whole design.* Restated: the affordances exist and hang off the **wrong surfaces** — placement, not absence, and much cheaper. Now [Trap U4](UX_RESUME.md#5-traps) | 2026-08-10 five-scan assessment |
| 6 | *"Modes like IOS and SimHost likely use different map rendering"* — user premise, 2026-08-10 | Symbol rendering is **shared and identical** across all map hosts — one `DebugGizmoLayer → DebugPrimitiveRenderer2D → DefaultEntityShapeLibrary` chain, data-driven by DIS enumeration, with an `IEntityShapeLibrary` seam **no host uses**. And **`ios` is a legacy alias for `excon`** (`HrotRunnerConfiguration.cs:85`), which has **no map at all** (`ExConSubsystem.cs:44`) — so it was never a map-customization case | 2026-08-10 map scan |
| 7 | Two **subagent** claims, caught before publication | (a) *"`Hrot.UI.Common` is listed in `IOS-IG-SimHost.sln`"* → it is in **no** solution; `grep` across every `.sln` returns empty. (b) *"`MessageLogPanel` (713 L) has no consumer"* → it **is** used, via `MessageLogWindow.cs:30`, which `LocalWindowController.cs:50` registers for **every** mode; the scan checked subsystems but not the host. ⚠ *Both would have shipped as fact if the orchestrator had trusted the reports — re-derive every delegated claim* | 2026-08-10, verified by the orchestrating session |
| 8 | *"Selection is fragmented three ways"* — [UX_Current_UI_Architecture §6](UX_Current_UI_Architecture.md#6-selection--one-shared-mechanism-two-hosts-outside-it), **published by this programme** | **Overstated.** SimHost was placed outside the shared mechanism; it in fact runs the same `SelectionInteractionSystem` on the same ECS `SelectionState` as Editor and IG (`SimHostVisualization.cs:250`). `SimHostSelectionManager` is a read-side UI mirror synced from `OnSelectionChanged` (`:253-260`), not a rival model. Real gap is narrower: **CGF and ExCon sit outside**. ⚠ *Mistook a per-host cache for a per-host mechanism — check whether a duplicate-looking type is a mirror before calling it fragmentation* | 2026-08-10 Editor/SimHost comparison |
| 9 | *"The Editor's real map sibling is IG, not SimHost"* — asserted by me in chat while scoping this scan | **Directionally right, wrong as a blanket claim.** The **interaction core** is shared three ways; only the **authoring-gizmo slice** is Editor+IG. The Editor explicitly imports SimHost's entire gizmo registrar (`EditorSubsystem.cs:1097-1098`), so it composes **SimHost ∪ IG**. ⚠ *Stated as fact from a partial scan before the pairwise comparison ran — it was a hypothesis and should have been labelled one* | 2026-08-10, same scan |
| 10 | Two more **subagent** claims, caught before publication | (a) *"Editor calls the ScenarioEditor registrar twice — once directly, once transitively via IG's"* → **false**. Editor calls `Hrot.IG.Gizmos.GizmoRegistrar.**RegisterAll**` (the source-generated method, IG's own attributed classes only); the chaining `Register` method is what `IgApplication` calls. (b) *"Editor registers ~5 context-menu handlers"* → **4**. ⚠ *Third and fourth delegated claims to fail verification this session — the rate is high enough that every cited claim must be re-derived* | 2026-08-10, verified by the orchestrating session |
| 11 | *"PACK2-E002 is tool migration — which is exactly UXI-07's work, so somebody already decided where the tool controller lives"* — asserted by me while designing Stage 0's B-list | **Wrong.** `ToolPresenceTests` states PACK2-E002 was **complete**: it *relocated* 10 tools out of `Hrot.IG` and finished by **converting them into gizmos**, deleting the `Tool` classes. It says nothing about a tool *abstraction*, so [UXI-07](UX_Issues.md#uxi-07) is genuinely new work with no pre-existing home. ⚠ *I read a stale in-code comment (`"populated in PACK2-E002"`) as a live plan without checking whether the work had already landed — a comment describing future work is not evidence that the work is pending* | 2026-08-10 UXI-02 design |
| 3 | *"Claude's lean: F3 → F1, staged"* — [Q25-F-i](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-i--where-does-the-seam-between-shared-init-and-new-shell-go) | **Withdrawn 2026-08-10 on measurement.** The lean rested on "the extraction is risky enough to defer" — a premise never measured. Measured, the minimum extraction is **2 types**, and `ClusterRunner` has **no production dependents**. F3 also spends its changes *inside* the host that hard-constraint #1 exists to protect, then deletes them. ⚠ *The failure mode: a lean argued from plausibility, written into an architect question as a recommendation, and nearly relayed unmeasured.* **Measure before leaning, not after the architect answers** | user challenged the lean; Sonnet measured |
| 4 | *"Delete/rename `imgui.ini` first"* — walk setup, [Golden Path](UX_Golden_Path.md) + RESUME §3.3 | Pointed at the **committed repo-root `imgui.ini`, which the app never reads**. The real file is `%LocalAppData%\HROT\imgui.ini`, plus `fdp_windows.json` next to the exe. An implementer following it would have deleted nothing, walked against their tuned layout, and reported a clean result. ⚠ *A handoff instruction that is wrong in a way that still "succeeds" is worse than one that fails* | 2026-08-10 extraction sizing |
| 12 | *"`EditorCommandDescriptor` is **the repo's mature API** — converge on it"* — [Q26-B](Architect_Question_26_Entity_Action_Model.md#q26-b--where-does-a-profile-live----the-question-was-malformed), published by this programme | **Provenance omitted, and it matters.** It lives in a **vendored** library — `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/IEditorCommands.cs:26` — is **shell/toolbar/hotkey scoped** (no entity or target parameter), and is registered **only by the Editor** (16 sites, 4 files). ⇒ its `Func<>`-predicate **shape** is still the right precedent, but it is not a repo-owned entity API to converge *onto*. ⚠ *"Mature API in this repo" read as "ours, and applicable here" — check whether a cited precedent is vendored and whether it is even in the same domain* | 2026-08-10 UXI-03 design |
| 13 | *"`GlobalActionRegistry` is constructed twice — Editor and SimHost"* — carried through the assessment and the glossary | **Three.** `ReplayBrowserSubsystem.cs:204` constructs one too, registering `OpenLayerControl` and a `CenterOnEntity` that **diverges from the Editor's** (direct `Camera.FocusOn` vs publishing `CenterOnEntityCommand`). CGF and IG remain at zero — that part held | 2026-08-10 UXI-03 design |
| 14 | **Subagent claim**, caught before publication | *"The `Hrot.Presentation` copy of `SharedContextMenuPopulator` has no caller; the `Hrot.UI.Common` copy is the one ExCon uses"* → **backwards.** Both files declare `namespace Hrot.UI.Common.Menus`, so the namespace cannot disambiguate them — the **csproj reference** does, and `Hrot.ExCon.csproj:15` references **only** `Hrot.Presentation`. ⇒ the live copy is `Hrot.Presentation/Menus/`, the dead one is in `Hrot.UI.Common`. 🔴 **Load-bearing for [UXI-01](UX_Feature_DeadUI_Removal.md):** acting on the report would have deleted the live file. ⚠ *Fifth-plus delegated claim to fail verification — and the first where the failure was resolving a duplicate type by **namespace** instead of by **project reference*** | 2026-08-10, verified by the orchestrating session |
| 15 | *"The graph independently surfaced `IHierarchyAdapter` at 1 implementer"* — [Seam Inventory](UX_Seam_Inventory.md), **published by this programme** | **It has ZERO.** The name appears **only in its own declaration file** — no implementer, no reference, not even in tests. 🔴 **Root cause is a query defect worth remembering:** `OPTIONAL MATCH (c)-[:IMPLEMENTS]->(i) RETURN count(c)` counts **rows, not bindings**, so a non-match still yields one row ⇒ **that query's floor is 1 and "0 implementers" is indistinguishable from "1"** — which is exactly the seam question it was asked to answer. ⚠ *Published the same day I wrote "a low number is a candidate, not a finding", and then cited a graph number without verifying it.* **Use `count(r)` on the relationship, and confirm every zero with a plain search** | 2026-08-10 UXI-04 design; subagent flagged, orchestrator verified |
| 16 | *"ExCon's 434-line ORBAT fork is redundant and collapses once the shared panel exists"* — carried since the Q26 round | **The shared panel is impoverished, not redundant.** `SharedOrbatPanel`'s entire context menu is **one item — Disembark** (`:115-121`), against ExCon's five (Select · Center · Delete · Edit Route · Abort Mission). Migrating today would **lose four of five items**, which is why the fork survives. ⚠ *"Duplicate" was inferred from two panels doing the same job; nobody compared what they offered.* Restated: the fork collapses as a **consequence** of giving the shared panel the shared menu, not as a task | 2026-08-10 UXI-04 design |
| 17 | *"The Editor's internal perspectives are Scenario, BTree, HSM and Blueprint"* — repeated across this programme | **Three, not four.** `"Scenario"` is a **display label** over the `Editor` perspective id — `RegisterPerspectiveLabel("Editor", "Scenario")` (`EditorSubsystem.cs:3449`). The id is always `Editor`; `SwitchPerspective` never sees `"Scenario"`. ⚠ *A label seam existed precisely to decouple the two, and I read the label as the identity* | 2026-08-10 UXI-06 design |
| 18 | **Subagent claim**, caught before publication | *"The restore failure also applies to `ReplayBrowser`"* → **false, and self-contradictory within the same report** — which listed `ReplayBrowser` among the `ISubsystem.Name` values two sections earlier. Verified `ReplayBrowserSubsystem.cs:40`: `Name => "ReplayBrowser"`, so it validates fine. ⚠ *An internally inconsistent report is a signal to re-derive **all** of its claims, not just the contradicted one* | 2026-08-10, verified by the orchestrating session |
| 19 | 🔴 *"Perspective restore silently drops `BTree`/`HSM`/`Blueprint` — fix the predicate so they are restored"* — [UXI-06](UX_Feature_Perspective_Restore.md) first draft, **and the fix was my published lean** | **The dropped restore is the DESIRED behaviour.** User, 2026-08-10: *"the way perspective switching works now in the Editor is exactly as I want — Scenario as the default, BTree/HSM/Blueprint as document-driven."* Documents are not persisted at all, so restoring `BTree` lands the user in an empty graph workspace. ⚠ **My proposed fix would have broken working behaviour**: validating against `GetPerspectives()` makes `BTree` valid, so restore would start honouring it. *I found a mechanism that looked wrong (validating perspectives against subsystem names) and assumed its every consequence was a defect, without asking which consequences the user actually experiences.* The real defect was elsewhere — the **default**, not the restore | 2026-08-10, user review of the UXI-06 design |
| 20 | *"`SurvivesActions` is a **tool** property — some tools survive an action fired mid-execution"* — [UXI-07](UX_Feature_Tool_Model.md) draft, **my own published shape** | **On the wrong object.** User, 2026-08-10: *"`SurvivesActions` can't be a tool property — it must be driven by **focus changes only**. Actions might need flagging if they steal focus."* ⇒ the flag belongs to the **action** (`StealsFocus`), because the action is what *causes* the effect; the tool merely experiences it. With N tools and M actions, a tool-side flag also cannot express *"this action is harmless"* — it forces every tool to predict every action. ⚠ *The reusable lesson: **attach a policy flag to the object that causes the effect, not the one that suffers it.*** Also collapsed an open sub-decision — the default is now obviously `false`, matching today's behaviour | 2026-08-10, user review of the UXI-07 design |
| 21 | 🔴 *"The two async handlers write to a **non-thread-safe** ECS bus from a thread-pool thread — a live data race"* — filed as **UXI-26** and made the headline of [UX_Interaction_API.md](UX_Interaction_API.md) | **False.** `NativeEventStream<T>.Write` is documented *"Thread-safe: multiple threads can write concurrently"*, uses `Interlocked.Increment` atomic reservation, locks only to resize (`NativeEventStream.cs:83-101`), and is double-buffered — **README §2.6 says so in one line**. ⚠ **Two compounding failures:** (a) I grepped `FdpEventBus.cs`, the dispatcher, and concluded *"zero synchronization"* without following `stream.Write` **one call deeper**; (b) I asserted an engine-level threading defect **without opening the engine's threading documentation** — `HROT-PROGRAMMERS-GUIDE.md` has a dedicated rule 7 (*background code uses `ISimulationView` + `IEntityCommandBuffer`*) and §8.1, both with `file:line` citations, and `README §2.4/2.6` covers the ECB and the bus. *The programme's own rule 6 says check prior art before designing; **the same discipline applies to claiming a defect** — read the invariants before asserting one is violated.* ⇒ UXI-26 refuted; the `SynchronizationContext` proposal **withdrawn** as inventing a mechanism parallel to a documented one | 2026-08-10, user challenged the claim |
| 22 | *"`DockspaceLayout.CentralSize` has **zero** production consumers — all 10 references are its own test file"* — published in the closing line before the UXI-09 design | **Refuted in the part that mattered to the claim.** It has **3 production call sites**, all in `Hrot.ClusterRunner/Program.cs:325,326,348`, sizing and positioning the `##DockSpace` host window. ⚠ **Root cause: I searched the *type* name and read the hit count without opening the call sites** — the tests dominate the count (8 tests vs 3 calls), and I stopped at the ratio. The *underlying* point survives and is what the design uses: **no camera, culling or centring code reads it** (0 hits in `MapCamera`, `MapCameraViewport`, `MapCullingSystem`, or any of the five centring methods). ⇒ the seam-law instance is real, but it is *"built for the dockspace, never extended to the camera"*, not *"built and never wired"* | 2026-08-12, self-check before writing [UXI-09](UX_Feature_Map_Viewport.md) |
| 23 | *"Camera setup is copy-pasted **4×**"* — [UXI-09](UX_Issues.md#uxi-09) as filed | **Five.** `Hrot.ReplayBrowser` (`ReplayBrowserSubsystem.cs:134`) is a fifth `MapCanvas`/`MapCamera` host and the most minimal of all — it never sets `Offset`, so its *Center on Entity* parks the entity at the **top-left pixel**. ⚠ The register's count came from a scan of the four *named* map subsystems; ReplayBrowser was invisible because it constructs `MapCanvas` (which default-constructs the camera) rather than `new MapCamera()` | 2026-08-12, camera scan |
| 2 | *"The editor discards the injected factory and hardcodes `OfflineNetworkFactory`"* — [SESSION_SYNC](../SESSION_SYNC.md), Q25, RESUME | True but **understated**: `_networkFactory` is never read either, so the editor uses **no** `INetworkFactory`. Not a refutation — but "hardcodes X" reads as "uses X", and the seam answer differs (`omit network composition` vs `keep the offline path`) | 2026-08-10 pre-seam check |

## Baseline evidence index

Findings from the opening audit (2026-08-06), hand-verified against code unless marked ⚠. These seed
the task register; each is restated in the `Now` column of the relevant
[requirement](UX_Requirements.md).

| Finding | Evidence |
|---|---|
| Outliner is a stub printing `• [entityId]` | `Hrot/Subsystems/Hrot.Editor/UI/EditorOrbatPanel.cs` — whole file, 27 lines |
| No scenario-side undo at all | Zero `Undo` matches in `Hrot/Subsystems/Hrot.Editor/`, `Hrot/Engine/Hrot.Presentation/`, `Hrot/Engine/Hrot.UI.Common/`. `Hrot/Subsystems/Hrot.Editor/Commands/` contains one file (`CenterOnEntityCommand.cs`) |
| Toolbar: 6 text buttons, no state/shortcuts/tooltips | `Hrot/Subsystems/Hrot.Editor/UI/EditorToolbarPanel.cs:35-47` |
| `New Scenario` produces a void | `Hrot/Subsystems/Hrot.Editor/EditorApplication.cs:138-143` |
| Scenario name only in a submenu | `Hrot/Subsystems/Hrot.Editor/WorkspaceMenuBuilder.cs:112-122` |
| No command palette | No `CommandPalette`/`QuickOpen` matches anywhere in the repo |
| Menu bar is thin | Registered paths: `File/{New Asset, Open Asset, Save, Save As, Save All}`, `File/Scenario/*`, `Blueprint/*`, `Assets/*`, plus auto-generated per-perspective window lists and `Perspective` |
| Two assignment models | `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` (OCC commit, conflict modal, Force Commit) vs `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EntityBlueprints/` |
| Allocator internals exposed to the author | `EntityBlueprintsEditModel.cs` — `Projection(Slots, Bytes, Tier, Status)`, `UsageStatus.OverCeiling`, `CommitPlan.UpgradeToTier`, Reality/Staging |
| Behavior list ungated for BTree assets | `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMissionService.cs:66-106`, incl. the `TODO (option c)` comment |
| Params fall back to raw JSON | `MissionPanel.cs:481-492` → `DrawRawJsonEditor`; typed forms via `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiCompiler.cs` + `BehaviorSchemaDiscovery.AutoRegister` |
| Params stored as escaped JSON-in-JSON | `scenarios/hill-attack/scenario.json` — `behaviorParams` is a `string` |
| Play/preview snapshot+rewind is correct | `Hrot/Subsystems/Hrot.Editor/Adapters/EditorPreviewAdapter.cs:54-67` |
| Transport controls exist in the status bar | `Hrot/Subsystems/Hrot.Editor/UI/TimeControlStatusBarSection.cs` → `ClusterTimeControlStatusBarSection` |
| Silent-failure house style | Blueprint audit: `BlueprintCommandSink.Apply` `default:` returns success; `MyBlueprintPanel.InvokeCreate` discards `EditorCommandResult`; `EditorCommandsImpl.Invoke` returns an unread `"Unknown command"` |
| README overstates the editor's ORBAT | README §11.4 claims "ORBAT drag-and-drop unit hierarchy"; that is ExCon's `OrbatPanel` (434 lines), not the editor's 27-line stub |

### Added 2026-08-10 — the pre-seam check for Milestone 0

Run from Linux (code only) to answer the two questions [RESUME §3.5](UX_RESUME.md#next-up) flags as
*establish before cutting the seam*. Both are answered; one turned up a defect.

| Finding | Evidence |
|---|---|
| ⚡ The editor consumes **no `INetworkFactory` at all** — `_networkFactory` is a **dead field**, declared and never read. Stronger than "discards the injected one": the offline one is unused too. Class is `sealed`, not `partial`, so the file is the whole class | `EditorSubsystem.cs:180` (sole occurrence in the file), `:165` (`sealed class`), `:557` (ctor ignores the parameter) |
| ⇒ **Shared init can omit network composition entirely for the editor preset** — it removes nothing the editor reads. Answers the F-i rider | as above + `Program.cs:202-207` |
| The DDS participant + factory are built for **every discovered subsystem** *before* the requested-subsystem filter, so `--mode editor` pays for a participant it never touches | `Program.cs:184-207` (inside the `Select`) vs `:213` (the `RequestedSubsystems` loop) |
| Default perspective = **first *requested* subsystem**. `Skip(1)` skips the runner's own `PerspectiveUpdateSubsystem`, prepended at `:212` — incidental, but less arbitrary than "skip one" reads | `LocalWindowController.cs:81-82`; `Program.cs:177`, `:212-213` |
| 🔴 **Persisted perspective is silently discarded for `BTree`/`HSM`/`Blueprint`.** The shell validates the restored id against **subsystem names**, but those are perspective ids registered by `EditorSubsystem`. Only `"Editor"` survives, coincidentally (`EditorSubsystem.Name => "Editor"`). ⚠ code-derived — confirm on the walk | `WindowManager.cs:368-382` (save) / `:388-411` (load) → `LocalWindowController.cs:83-84`; `EditorSubsystem.cs:172` |
| This is the code F-ii asked us to find before cutting the shell — and it is **shell-level**, so it is a *"the new shell must not reproduce this"* entry, not a repair task | [Q25-F-ii](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-ii-perspective-restore) |

### ⭐ Added 2026-08-10 — the UI sharing assessment

**Five-scan survey of what is shared across the 5 modes, what was forked, and why. Its findings are not
duplicated here — read [UX_Current_UI_Architecture.md](UX_Current_UI_Architecture.md).** It carries the
seam law, the full seam inventory, the duplication and rigidity registers, ~1.8k lines of dead UI
(including the 🔴 `Hrot.UI.Common` editing trap), the three-way selection split, and the three-tier gap
plan that now opens the programme as **Milestone S** in the [tracker](UX_Task_Tracker.md).

### Added 2026-08-10 — extraction sizing for Q25-F′ (Sonnet, reviewed)

Measured to test whether the F3→F1 staging lean was justified. **It was not** — see
[F′ measured](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-prime-measured).

| Finding | Evidence |
|---|---|
| **No production project references `Hrot.ClusterRunner`** — only its 2 test projects. It is a leaf, so extraction risk is contained | `Hrot.ClusterRunner.Tests.csproj:23`, `Hrot.ClusterRunner.Integration.Tests.csproj:30`; all other `.csproj` hits are `InternalsVisibleTo` granting ClusterRunner access to *their* internals |
| ⭐ **The minimum extraction is 2 types**, not a refactor. 7 `internal` types exist, but a curated shell wants only `IPresentationShell` + `RaylibPresentationShell` (Raylib/ImGui/font/icon bootstrap). `LocalWindowController` **is the aggregator being replaced**; `PerspectiveUpdateSubsystem` is multi-subsystem cluster coordination | `Presentation/IPresentationShell.cs:6`, `Presentation/RaylibPresentationShell.cs:7`, `Presentation/LocalWindowController.cs:12`, `Services/PerspectiveUpdateSubsystem.cs:15` |
| The bootstrap seam is **already test-driven through a fake** — the interface is stable | `Presentation/LocalWindowControllerTests.cs` (108 lines) drives it via a fake `IPresentationShell` |
| ~200 of `Program.cs`'s 484 lines are **droppable** for an editor-only exe: CI mode (`:127-156`), migrate mode (`:158-171`), DDS participant + factory selection (`:193-204`), perspective/gizmo cluster map (`:242-261`), node-id offsets (`:389-403`), reflection scan (`:410-482` → one `new EditorSubsystem()`) | `Program.cs`, phase map |
| ⚡ **Scenario load + Play/Stop are NOT in the host.** `Hrot.Editor` builds its **own in-process `ClusterMaster`** (`Mandatory = Array.Empty<string>()`) and ticks it per frame; intents are published from `EditorApplication.cs`. `Program.cs` contributes only generic hosting ⇒ **removes the main argument for "very much shared init"** | `EditorSubsystem.cs:1352`, `:1355-1384`, `:1634-1638`; `EditorApplication.cs:76-103`, `:155-167`; `PreviewClusterOpHandler` via `EditorSubsystem.cs:439-464` |
| F3's cost lands **inside the host constraint #1 protects**: 3 files, `Program.cs:236-370` (135 lines) plus conditionals through most of `OpenLocalWindow`'s 53-line body — then deleted again at F1 | `Program.cs:236-370`; `LocalWindowController.cs:36-88`; `HrotRunnerConfiguration.Validate()` |
| 🔴 **`imgui.ini` is machine-wide with no override seam** — `%LocalAppData%\HROT\imgui.ini`, hardcoded inside `SetupImGui()` (no parameter, no DI). Two shells collide **on one machine regardless of which exe they are**. `fdp_windows.json` is exe-directory-keyed and *is* overridable, but every call site omits the path | `RaylibPresentationShell.cs:128-136`; `WindowManager.cs:437-438` vs `LocalWindowController.cs:75,94` |
| ⇒ **A path seam on `SetupImGui()` is a prerequisite for the new shell under every F′ option**, and blocks [UXR-04](UX_Requirements.md#uxr-04) until it exists | as above |
| 🔴 The repo root contains a **committed `imgui.ini` that the app never reads** — the walk instruction "delete `imgui.ini` first" pointed at it. Deleting it does nothing and silently invalidates the walk | `imgui.ini` (tracked, last touched by commit `877fc7c`) vs the `%LocalAppData%` path above |
