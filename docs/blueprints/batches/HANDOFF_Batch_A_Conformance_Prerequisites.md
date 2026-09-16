<!--STATUS
state: WITHDRAWN
build-state: WITHDRAWN-BEFORE-START
updated: 2026-08-23
current-answer: dispatch pointer for the MCP / headless-testability lane — Batch A, the cross-host
  conformance PREREQUISITE chain: the perspective endpoint, lifting the debug API to the runner host,
  the frame boundary in every host, and the two-mode differential suite. §1 carries ten MEASURED
  findings that change the batch's shape versus the design docs; read §1 before §2.
known-conflict: none. §1-F1 corrects a mode name that three design docs got wrong; the corrections
  landed in those docs in the same commit as this handoff, so canon and this dispatch agree.
-->
# ⛔⛔ WITHDRAWN BEFORE START — do NOT execute this handoff

> ⛔⛔⛔ **WITHDRAWN `2026-08-23`, before the lane started it** *(verified: `80a98f627` is NOT an ancestor
> of the lane branch, and no started-marker exists ⇒ rule 1a, not rule 1c)*.
>
> ⭐⭐ **Why:** the user corrected the PREMISE, not the execution. This handoff took the editor's and the
> cluster's perspective sets as **permanently disjoint** and worked around it *(discover per mode, diff by
> `PanelKind`)*. ⛔ **The actual intent is the opposite: make the perspective names COMPATIBLE** — when the
> runner hosts CGF it should present the **asset perspectives** *(Scenario · BTree · HSM · Blueprint)*
> rather than one `CGF` perspective, because CGF is meant to be as capable as the editor in a distributed
> setup. ⇒ **conformance then compares like with like**, and my workaround becomes unnecessary.
>
> ⭐⭐ **And the ORDERING was wrong.** This batch went straight at cross-host conformance. ⛔ The regression
> net that protects the refactor is **editor-mode only** and blocked on nothing — it must come first.
>
> ⭐ **§1's ten measured findings REMAIN VALID and are the reason this was catchable** — they carry forward
> into the replacement dispatch. ⛔ Nothing below is deleted; it is kept for the record.
>
> 📄 **Replaced by:** the Phase-0 dispatch *(editor-mode perspective reach + the granular regression net)*,
> plus a design round on the perspective-compatibility question. ⚠ **`F10` is SOFTENED** — see the
> replacement: a shared `PanelIds` constant class already exists and cross-host hosts already cite it.

## ⛔ HISTORY — the withdrawn dispatch follows, unedited

# HANDOFF — MCP lane · **Batch A: the conformance prerequisite chain**

> 📌 **Dispatched at `80a98f627`.** ⛔ **Scope FROZEN at this sha.** ⭐ Branch fresh from the coordinator
> branch *(rule 7)*; **rule 1b: started-marker FIRST, before any code.** ⭐ ids **`HN-`/`MX-`**, tracker
> **Area J**. ⛔ **No PR.**
>
> ⚠ **The coordinator branch is now `claude/blueprint-authoring-status-6sr5ld`** *(user ruling,
> `2026-08-23`)*. The older `…-gm0akp` head is an ancestor of it — identical content, no divergence.
> ⛔ Merge **`6sr5ld`** for rule 7, not `gm0akp`.

## 0. ⭐ WHY THIS BATCH

Cross-host conformance — *"do the editor host and the cluster host SHOW the same thing?"* — is the
payoff of the whole testability programme. It is blocked on four things, none of them large. This batch
unblocks it and lands the first differential suite.

📄 **Design basis** *(cite these, not this handoff, when you fold findings back)*:

| doc | what it owns |
|---|---|
| [`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md) | the taxonomy + one-binary architecture; **sequencing steps 5/6/7 ARE this batch** |
| [`TESTING_Harness_And_Goldens.md`](../../TESTING_Harness_And_Goldens.md) | ⭐ the RUNBOOK — the perspective protocol §3, conformance §5, **the per-batch obligation §6** |
| [`DESIGN_UI_Observability_Snapshot.md`](../../DESIGN_UI_Observability_Snapshot.md) | the `PanelSnapshot` contract |
| [`MCP_Integration.md`](../../MCP_Integration.md) | the API; **Group T** is the panel read surface |

## 1. ⭐⭐⭐ TEN MEASURED FINDINGS — **read these first; several change the work**

⛔ **All measured at `80a98f627`, not remembered.** ⚠ Where a finding contradicts a design doc, the doc
was corrected in this same commit — so ⛔ **a contradiction you find is a NEW one, and rule 1c applies
(STOP and report), not "adapt".**

| # | finding | evidence |
|---|---|---|
| 🔴🔴 **F1** | ⛔⛔ **`--mode cluster` DOES NOT EXIST.** Valid modes: `simhost · ig · excon · orchestrator · cgf · ci · editor · stridemock · replaybrowser · migrate`, plus the shorthands **`all`**/`demo`. ⭐ **`all` expands to `orchestrator,simhost,ig,excon,cgf`** — **FIVE** subsystems, not the doc's *"CGF + SimHost + Orchestrator"*. ✅ An unknown mode **throws** *(fails fast, not a silent default)* | `HrotRunnerConfiguration.cs:104-123` |
| ⭐⭐ **F2** | ⛔ **"Lift `DebugApiHost` one level up" is FOUR wiring points, not one.** All four sit in `EditorSubsystem` and **all four must exist in the runner host** or the API answers wrongly: ① `PanelSnapshot.CaptureEnabled = true` · ② the host construct + `AttachService` + `Start` · ③ `PanelSnapshot.ClearCaptured()` **as the first line of the frame** · ④ `_debugApiJobQueue?.DrainAll()` once per frame. ⛔⛔ **Forget ④ and every `RunMain` route HANGS** — the most expensive thing to debug in this list | `EditorSubsystem.cs:1620` · `:1629`+`:1674` · `:1893` · `:1897` |
| ⭐⭐⭐ **F3** | ⭐ **The HOST is already mode-agnostic — the SERVICE is not.** `DebugApiHost(int port, MainThreadJobQueue, Action shutdown)` takes nothing editor-shaped, and `AttachService` is late-bound *(capability routes 503 until attached; `/status` + `/shutdown` work regardless)*. ⛔ **But `DebugApiService` has 9 REQUIRED ctor params** including editor-only `IPreviewController` + `IEditorLogic`, each `throw`-guarded ⇒ **it cannot be constructed in `--mode all`.** ⭐⭐ **AND YET `GetPanels()`/`GetPanel(id)` touch ZERO instance state** — pure static `PanelSnapshot` reads; only `GetGizmoFrame` uses an instance field *(`_primitiveBuffer`)*. ⇒ ⭐⭐⭐ **the panel/perspective routes need no editor dependency at all.** ⛔⛔ **Therefore the design's recorded lean on OQ2 — *"reuse the host with a read-only route filter"* — DOES NOT SOLVE THIS:** a route filter still requires a constructed service. **The split is by DEPENDENCY, not by verb** | `DebugApiHost.cs:42`,`:53` · `DebugApiService.cs:209-239` · `DebugApiService.Panels.cs:30-107` |
| 🔴 **F4** | ⭐⭐ **`PanelSnapshot.ClearCaptured()` has exactly ONE production caller** — `EditorSubsystem.cs:1893`. ⇒ **editor mode has a frame boundary; a host that never calls it keeps latest-wins forever** *(deliberate — the contract test says so)*. ⛔⛔ **CONSEQUENCE FOR CONFORMANCE: a fresh frame diffed against an accumulated latest-wins set is a FALSE-DIVERGENCE GENERATOR.** ⇒ ⭐⭐⭐ **this is a PREREQUISITE of the differential suite, not a trailing nice-to-have** *(it was filed as a trailing item; that was wrong)* | `grep ClearCaptured` = 1 prod site · `PanelSnapshotTests.cs:238-243` |
| ⚠ **F5** | **The API states the opposite of the truth.** `GetPanels()`'s `["staleness"]` string says clearing per frame *"needs a captured-only clear the contract does not yet expose"* — ⭐ **`ClearCaptured` shipped (MX-006).** A conformance author reading the API is told there is no frame boundary when there is one | `DebugApiService.Panels.cs:65-70` |
| 🔴 **F6** | ⛔⛔ **`WindowManager.SwitchPerspective` VALIDATES NOTHING** — any string sets `CurrentPerspective` and fires the event. ⇒ a typo'd perspective name **succeeds**, every bound window stops drawing, and `GET /panels/{id}` then answers *"instrumented but has published no model — its window is probably closed"* ⇒ ⭐ **a plausible-sounding wrong explanation for a typo.** 📌 `UX_Feature_Perspective_Restore.md` §3 **designs** the refusal *("Log and no-op instead of silently hiding every bound window")* — **it is not implemented.** ⛔ **`WindowManager` is `FDP/Engine/Fdp.Presentation` = shared/UI-lane** ⇒ **validate in YOUR DebugApi layer; do NOT edit `WindowManager`** *(`R-128`)* | `WindowManager.cs:234-241` · `UX_Feature_Perspective_Restore.md:106-109` |
| ⚠ **F7** | **TWO notions of "current perspective."** `WindowManager.CurrentPerspective` defaults to `"Default"`; `PerspectiveCoordinatorSystem.CurrentPerspective` is `""` until the first event, and **it** is what drives `SwitchMapOwner` in cluster mode. ⇒ ⭐ **`GET /perspectives` must state WHICH it reports**, or the two modes answer about different things. 📌 Same defect class as `M-40`/`M-41` *(twelve notions of "paused")* | `WindowManager.cs:221` · `PerspectiveCoordinatorSystem.cs:23`,`:56`,`:83` |
| ⭐⭐ **F8** | ⛔ **The two modes' perspective sets are DISJOINT** — editor `{Editor, BTree, HSM, Blueprint}`, cluster `{IG, SimHost, ExCon, CGF, StrideMock}` — and **`editor` may not be combined with the cluster flags** *(throws)*. ⇒ ⛔ **"switch to the same perspective in both modes" is IMPOSSIBLE.** ⭐ Conformance must discover **per-mode** perspective→kind mapping; `GetPanels()`'s `kinds` map is the intended mechanism and already exists | `HrotRunnerConfiguration.cs:155-162` · `Program.cs:248-254` · `UX_Feature_Perspective_Restore.md:119-121` |
| 🔴🔴 **F9** | ⛔⛔ **THE DESIGN'S RECORDED LEAN ON OQ1 IS IMPOSSIBLE.** It says start conformance with *"the unified variable/Details/blackboard panels"* — ⭐ **those live in `Hrot/Editor/Hrot.Editor.AiShared/{Windows,Variables}`, editor-only assemblies `--mode all` cannot host** ⇒ following the lean yields an **EMPTY** comparison set. ⭐⭐ **The genuinely shared candidates are the ones both hosts draw:** `Hrot/Engine/Hrot.Presentation/Panels` *(`MissionPanel` · `SpawnerPanel` · `PreviewPanel` · `DataBreakpointManagerPanel` · `ZoneEditorPanel` · `ConfigPanel` · `SharedOrbatPanel`)* and `FDP/Engine/Fdp.Presentation/ImGui/Panels` | panel sweep by assembly, `2026-08-23` |
| 🔴🔴 **F10** | ⛔⛔⛔ **`PanelKind` IS SUPPLIED BY THE HOST, NOT BY THE PANEL TYPE** — it is a ctor parameter on each view-model record, and the IG panels' own comments say so verbatim: *"no `PanelId`/`PanelKind` of its own — the HOST supplies…"*. ⇒ ⭐⭐⭐ **diff-by-`PanelKind` rests on an UNENFORCED CONVENTION:** two hosts may register the same panel under different kind strings, the intersection silently becomes empty, **and the conformance suite PASSES having compared nothing.** ⇒ ⛔ **the non-vacuity rail in §4 is mandatory, not optional** | `IgDebugPanel.cs:11` · `MissionPanel.cs:32` · `SharedOrbatPanel.cs:19` |

## 2. ⭐⭐ THE TASKS

> ⛔⛔ **`A1…A5` are PLACEHOLDERS, not ids** *(rule 3 — the coordinator allocates NO ids)*. ⭐ **YOU number
> them** as `HN-`/`MX-` rows in **Area J** and **state the ids you allocated** in your report *(rule 5)*.
> ⚠ **`BP-487` is referenced below as the ORIGIN of A3's finding — ⛔ do NOT tick it or write a `BP-` row.**
> It is the UI lane's prefix *(`R-128`)*; cross-reference it from your own row instead.

| # | task | design basis | gate |
|---|---|---|---|
| **A1** | ⭐ **`GET /perspectives` + `POST /perspective {name}`** on the DebugApi. ⭐ **Pure plumbing over three methods that already exist** — `WindowManager.GetPerspectives()` *("the testable seam")*, `SwitchPerspective(name)`, `CurrentPerspective`. ⛔⛔ **`POST` MUST VALIDATE `name` against `GetPerspectives()` and 400 with the valid list** *(**F6** — an unvalidated switch turns a typo into a plausible empty snapshot)*. ⭐ Report `current` + `available`, and **say which notion of "current" you report** *(**F7**)*. ⛔ **Do NOT edit `WindowManager`** — validate in the API layer | runbook §3 · taxonomy step 5 | a case that switches to a **bogus** name gets **400 + the list**, and the snapshot is unchanged |
| **A2** | ⭐⭐ **Lift the debug API to the `ClusterRunner` host** so `/panels` + `/perspective` answer in **`--mode all`**. ⛔ **All FOUR wiring points of F2**, ④ *(the per-frame `DrainAll`)* included. ⭐⭐ **The service split is by DEPENDENCY, not by verb (F3):** the panel/perspective handlers need **no** editor services, so give the host something it can attach in either mode — ⭐ **lean: extract the dependency-free handlers behind a small interface the editor service also satisfies**, so there is ONE implementation of "what a panel shows" *(⛔ a second interpretation would be wrong invisibly — the reason `DebugApiService.Panels` is thin by design)*. ⚠ **Trap:** `DebugApiCompositionTests.DebugApiServiceConstruction` asserts on the **TEXT** of the `new DebugApiService(…)` call — touching that call site breaks a string-matching test; update it deliberately | taxonomy step 6 · runbook §1 | `--mode all` + `HROT_DEBUG_API_PORT` ⇒ `GET /status` **200** and `GET /panels` reports `captureEnabled: true` with a **non-empty** `registered` |
| **A3** | 🔴 **The frame boundary in EVERY host that publishes** — `ClearCaptured` as the first line of the frame, plus the gizmo publish, wherever a host draws panels *(**F4**; origin: `BP-487`)*. ⭐ **The four gizmo hosts** are IG · CGF · ReplayBrowser · SimHost. ⛔⛔ **`ClearCaptured`, NEVER `Clear`** — `Clear()` also drops the INSTRUMENTED set, which is declared once at panel construction, so per-frame `Clear` empties `RegisteredPanels` permanently after frame one. ⭐⭐ **This BLOCKS A4:** without it, cluster mode accumulates latest-wins while editor mode reports one frame, and the diff invents divergence | `MCP_Integration.md` Group T · `EditorSubsystem.cs:1875-1893` *(the comment block explains the ordering — read it, do not re-derive)* | a case proving a panel that **stopped drawing** disappears from `captured` in the lifted host, as it already does in the editor |
| **A4** | ⭐⭐⭐ **`ClusterRunnerFixture(mode)` + the differential conformance suite.** ⭐ Mirror the existing `EditorProcessFixture` *(it already owns Xvfb directly — ⛔ **do not "simplify" it back to `xvfb-run`**, which leaks a display server per run)*. ⛔ **Perspectives are mode-specific (F8)** ⇒ **discover** per mode: `GET /perspectives` → switch → step → `GET /panels` → intersect the `kinds` maps. ⭐ **First targets: the shared-presentation panels of F9** — ⛔ **NOT** the variable/Details/blackboard panels the doc's lean named; they cannot appear in cluster mode. ⭐ Tolerance: **exact model equality, with an explicit documented ignore-list** for legitimately host-specific fields | taxonomy step 7 · runbook §5 | §4's rail, plus a real diff on at least one shared `PanelKind` |
| **A5** | ⚠ **Correct the API's own stale claim** — `GetPanels()`'s `["staleness"]` string predates `ClearCaptured` and now says the opposite of the truth *(**F5**)*. ⭐ Make it state what is actually true **per host**: bounded where the boundary is wired, latest-wins where it is not | `DESIGN_UI_Observability_Snapshot.md` | the string matches measured behaviour in both modes |

## 3. ⛔⛔ §4 — THE NON-VACUITY RAIL *(mandatory, from F10)*

⭐⭐⭐ **The conformance suite must be incapable of passing without comparing something.**

| rail | why |
|---|---|
| ⭐⭐ **Assert the compared-kind count is `> 0`** and **report the number + the kind names** | ⛔ **F10**: `PanelKind` is host-supplied, so an intersection can silently become empty and every assertion then holds vacuously |
| ⭐ **An EMPTY intersection is a FINDING, not a pass** — report it as a defect with the two `kinds` maps | ⭐ It would mean the two hosts label the same panel differently — **exactly the divergence conformance exists to catch**, and the one shape a naive suite cannot see |
| ⭐ **Report which kinds you compared and which you SKIPPED**, with the reason | ⛔ silent truncation reads as "covered everything" |

## 4. ⛔ LANE & NOT-THIS-BATCH

⭐ **Your surface:** `Hrot.Editor/DebugApi/*` · the runner host wiring *(`Hrot.ClusterRunner/Program.cs`,
the composition root)* · `Hrot.SystemTests` · `tools/ai-debug-mcp/`.
⛔ **Do NOT touch:** `WindowManager` *(shared/UI lane — **F6**)* · the variable model / `Hrot.Editor.AiShared`
/ the panels themselves · the parked Stride tree.
⚠ **A3 reaches into four host classes** to add a frame-boundary call. ⭐ That is wiring, not panel work —
⛔ but if it turns into editing a panel or the `PanelSnapshot` contract, **STOP and report** *(`R-128`)*.

## 5. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs base
`80a98f627`** · a `--no-build` column · every RED confirmed pre-existing **by name** · goldens as a **diff
shape** · `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · **the `HN-`/`MX-`
ids you allocated** *(rule 5)*.

⭐⭐ **Row 8 — the integration invariant:** the harness smoke suite, headless under Xvfb, green — with the
`--filter` and the Xvfb launch quoted. ⭐⭐ **Plus a row for the NEW two-mode suite**, carrying §3's
compared-kind count.

⚠ **Known baseline quirks** *(do not re-derive)*: `tracker-counts.py --check` counts only `**BP-` rows ⇒ **it
is blind to your `HN-`/`MX-` rows**, and its OK is not evidence about them. `tools/ai-debug-mcp` `verify.mjs`
fails pre-existing *(needs `npm install`; `node_modules` is gitignored)*.

⭐ **Rule 4/7:** re-sync and **pull the coordinator branch again before your final commit** — late additions
are exactly what rule 4 exists to catch. ⭐ **Rule 1b:** started-marker before code.

## 6. ⭐ FOLD BACK BEFORE THE BATCH CLOSES *(obligation ⑤)*

⛔ **The batch report is ephemeral; the design is not.** As-built deviations go into the OWNING design:
`DESIGN_Headless_Testability.md` *(steps 5/6/7 + the three open questions)*, `MCP_Integration.md`
*(Group T's as-built)*, `TESTING_Harness_And_Goldens.md` *(the conformance protocol, if the discovery
step changes it)*. ⭐ **Diagrams live in designs, never in batches** — validate with
`MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>`.
