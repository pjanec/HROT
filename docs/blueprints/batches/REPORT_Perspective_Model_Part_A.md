<!--STATUS
state: LIVE
updated: 2026-08-23
current-answer: this whole file — the Part A batch report. ⛔ It carries NO design: every as-built fact was
  folded into DESIGN_Perspective_Unification.md §1/§3 before this was written (obligation ⑤), and this file
  POINTS there.
known-conflict: none.
-->
# REPORT — **perspective model, Part A**

> 📌 **Dispatch `89acf0f20`** *(scope frozen there)* · **base sha `c6f54318c`** *(the started-marker's
> parent — the coordinator head when this run began)* · started-marker **`8265ec97b`** *(rule 1b, pushed
> before any code)* · branch **`claude/reset-working-branch-qd1qpv`** *(rule 7: fast-forwarded from
> `claude/blueprint-authoring-status-6sr5ld`)* · ⛔ **no PR**.
>
> ⭐ **ids allocated: `BP-488` … `BP-497`** *(rule 3/5 — the handoff's `A0…A10` were placeholders)*.
> ⛔ **No `ST-` id was touched** — the parallel StrideMock lane owns those.

## 0. ⭐⭐⭐ OBLIGATION ③ — the design's diagrams, checked before building

📄 **[`DESIGN_Perspective_Unification.md`](../../DESIGN_Perspective_Unification.md)** carries **3** mermaid
blocks: a `graph TD` *(§1d, window-visibility resolution)*, **1 `classDiagram`** and **1
`sequenceDiagram`** *(§5)* — ⭐ **8 classes** in the class view.

| ⭐ | verdict |
|---|---|
| the `graph TD` visibility rule *(`Global \|\| pinned \|\| owning == current`)* | ✅ **matches** — `ManagedWindow.Render:160-162` is exactly it, unchanged by this batch. ⭐ It is also the reason `A0` matters: an unclaimed `current` makes the third arm false for every window |
| the `sequenceDiagram`'s `A0` refusal | ✅ **built as drawn** — ⚠ with one correction FOLDED BACK: its note said the claimed set is *"Scenario BTree HSM Blueprint Authoring Analysis"*. 📐 `Authoring`/`Analysis` are **not claimed at runtime** *(§1's own correction)*, so the note was self-contradictory. Now `Blueprint BTree HSM Scenario`, in the order `GetPerspectives()` returns |
| the `classDiagram`'s 8 classes | ⚠ **3 classes I edited were ABSENT and are now drawn** — `LocalWindowController` *(`A0`'s second half)*, `FindResultsWindow` *(`A5`/`A6` changed its signature)*, `AssetKindExtensions` *(gained a member)*. ⛔ A class diagram that omits the types a batch edits cannot make a duplicate visible, which is obligation ②'s whole point |
| `PerspectiveWorkspaceServices.CreateRegistrar` ×3 | ✅ **untouched** — Part B's vehicle, correctly out of scope |

⭐ All 3 blocks re-validated: `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs` ⇒ **`All 3 mermaid
block(s) parse.`**

## 1. ⭐⭐⭐ WHAT LANDED — and the three things the design had measured wrong

⭐ **Every item was built.** ⛔ **Nothing was descoped.** 📄 Per-item AS-BUILT notes live in the design's §3.

### 🔴 The three corrections — all of them corrections to MY OWN measurements, folded into §1

| # | the design said | 📐 measured |
|---|---|---|
| **①** | *"**8** window registrations use `"Editor"`"* | ⛔⛔ **21 perspective sites.** The `8` came from a `: base("id","title","Editor",…)` grep, which sees only `EditorWindows.cs`. It missed **13** in `EditorSubsystem.cs` that pass the perspective as an ordinary ctor ARGUMENT or compare it — and 🔴 one of those was `FdpEntityInspectorHelper`'s third parameter, **named `ownerName` and documented *"subsystem name shown in watch-window titles"***, when 📐 the titles never use it and it is assigned to `Reflector.EditOwningPerspective` and passed as the spawned window's `owningPerspective`. ⇒ ⭐ leaving it would have spawned every *"Inspect…"* watch window into a perspective nothing claims |
| **②** | *"`gizmoControllables` **keyed by perspective**"* *(and the field's own doc says so)* | 🔴🔴 **It was keyed by `ISubsystem.Name` and READ by `evt.NewPerspective`.** ⭐ Invisible because every perspective happened to be spelled like its subsystem. ⇒ `A9` would have shipped **a green map and dead gizmos**: `SwitchMapOwner` takes the mapped VALUE and keeps working, while `_gizmoControllables["Scenario"]` misses a dictionary keyed `["CGF"]`. ⚠⚠ **§1e withdrew `R1`/`R2` reasoning about the VALUE side and walked straight past the KEY** |
| **③** | `L6.1b` deferred *"because `CurrentPerspective` and every `OwningPerspective` are persisted"* | ⛔ **False.** 📐 `WindowManagerSettings` persists window **ids** with `IsOpen`/`IsPinned`, plus **exactly ONE** perspective name — `ActivePerspective`. `WindowInternalName` is `$"{Title}###{Id}"`, so the ImGui ini holds none. ⇒ ⭐ **the rename orphans ONE string**, and `A0` is what handles it. 📌 The rail that PINNED the deferral is inverted, not deleted |

📌 **All three have one shape: a claim about RUNTIME read off a LITERAL or a DOC COMMENT.** ⭐ §1's own
correction names it — *"I read constructor literals as a claim about runtime"* — and it recurred three more
times in the same document. ⇒ ⭐⭐ **the new rails assert `GetPerspectives()` after the REAL
`RegisterWindows`**, because that is the only statement of the claim that cannot be read wrong.

### ⭐⭐ Two defects found that the handoff did not name

| | |
|---|---|
| ⭐⭐⭐ **`BP-489` — the blank first launch is BIGGER than *"also fix the restore path"*** | 📐 `LocalWindowController` picked `_subsystems.Skip(1).First().Name` and validated the persisted value against subsystem names. ⛔ For `--mode all` that is **`"Orchestrator"`**, which claims nothing ⇒ 🔴 **22 perspective-bound windows invisible on first run**, on the `demo` shorthand a new user tries first. ⚠ **This is broken TODAY, with or without the rename** — 📄 and `UX_Feature_Perspective_Restore.md` had specified the fix on `2026-08-10` and it was never built |
| ⭐⭐ **`BP-490` — §1c's *"that is luck, not a control"* was optimistic** | 📐 `SharedAiEditorServiceCollectionExtensions:77` did `AddSingleton<FindResultsWindow>()`, resolving **every** argument to its default ⇒ **a SECOND site the latent generator was firing in.** ⭐ Found only because the signature change broke the build — ⛔ no review and no rail would have. ⚠ Harmless only because `AddSharedAiEditor` has no production caller *(measured: tests only)* |

### ⚠ One DEVIATION from a cited design, argued and folded back

📄 `UX_Feature_Perspective_Restore.md` §1 gives the default as `known.FirstOrDefault()` and excludes
document-driven perspectives from **restore only** *(§2)*.
⛔ **Measured `2026-08-23`: that breaks the moment `A1` lands.** 📐 `GetPerspectives()` is
`OrderBy(p => p)` — **culture** comparison, not ordinal — so `--mode editor` sorts to
**`[Blueprint, BTree, HSM, Scenario]`** ⇒ ⭐ a bare `known.First()` opens the editor in an **empty Blueprint
graph**: precisely the outcome that design exists to prevent.
⇒ ✅ **As built: document-driven names are excluded from BOTH halves**, composition order then merely
PREFERS one durable perspective over another, and *"only document-driven claimed"* returns `"Default"` so
`A0` refuses it **loudly** instead of guessing. 📄 Recorded in the design's `A0` AS-BUILT table.

### ⭐ Round-out beyond the items *(cheap, same machinery)*

⭐ `FindResultsWindow`'s ctor **refuses both incoherent pairings** — a `PerspectiveBound` window with an
empty perspective *(permanently invisible)* and a `Global` one that NAMES a perspective *(misleading)*.
⇒ ⛔ the phantom is unconstructible **in both directions**, not only when a caller forgets the argument.

## 2. ⛔⛔ SCOPE — what I touched OUTSIDE the handoff's stated surface, and why

⭐ The handoff's surface: `Fdp.Presentation/ImGui/WindowManager/` · `FindResultsWindow.cs` ·
`EditorSubsystem`'s registration sites · the layout defaults · the affected tests.

| file | why it was unavoidable |
|---|---|
| `Hrot.ClusterRunner/Presentation/LocalWindowController.cs` | ⭐ It **IS** the *"layout-restore path"* `A0` names. ⛔ Not listed by name, but `A0` cannot be done without it |
| `Hrot.ClusterRunner/Program.cs` | ⭐ `A9`'s `perspectiveMap` + `A10`, both explicitly assigned to this lane |
| `Hrot.CGF/CgfSubsystem.cs` | ⭐ `A9` |
| `Hrot.Editor.AiShared/Identity/AssetKindExtensions.cs` | ⭐ The document-driven set the restore design specifies by name *(`AllPerspectiveNames()`)*, built as `DocumentDrivenPerspectiveNames` and **derived from `ToPerspectiveName`** so there is no second list |
| `Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` | ⭐ `A6`'s second call site — the build does not compile without it |
| ⚠ **`Hrot.Presentation/Windows/FdpEntityInspectorHelper.cs`** | ⭐ A hidden `A1` site *(correction ① above)*. ⛔ **Not on the surface list and not on the other lane's either** — I renamed the parameter and corrected its false doc. **Flagging it rather than assuming it was fine to touch.** 📐 No behaviour change beyond the perspective value the two callers pass |

⭐⭐ **The freeze (`R-128`).** `A5`/`A6` are sanctioned in `Hrot.Editor.AiShared` as window-registration
plumbing. ⭐ The two further AiShared files above are the same class — **the perspective vocabulary and a DI
registration** — ⛔ **nothing touched the variable model, the Details panel or the blackboard surface**, and
no item turned into that.

⭐ **The parallel lane: zero conflict.** 📐 Our only shared file is `Program.cs`, and `A10` deleted the
`["StrideMock"] = "StrideMock"` line as instructed. ⚠ **If that lane reports the line still present, it is
looking at a tree without this commit.**

## 3. ⭐⭐⭐ GATES — the 8-row contract *(rule 8)*

⭐ **Base sha for every "pre-existing" claim: `c6f54318c`**, verified in a **clean git worktree**, not by
assertion. ⭐ **Working tree CLEAN after every suite run** *(row 5)* — `git status --porcelain` = the 33
intended files, before and after.

| # | gate — verbatim command | `--no-build` | result | Δ vs `c6f54318c` |
|---|---|---|---|---|
| **1** | `dotnet build IOS-IG-SimHost.sln --no-restore` | n/a | ✅ **0 errors**, 62 warnings | **0 / 0** |
| **2** | `dotnet test FDP/Engine/Fdp.Presentation.Tests/… --no-build --filter "…PerspectiveLabelTests\|…PerspectiveToolbar\|…WindowManagerTests\|…WindowManagerSettings"` | ✅ yes | ✅ **50 passed, 0 failed, 0 skipped** | **48 → 50: +2 tests** *(the two new `A0` rails)*, 0 red either side |
| **3** | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/… --no-build` | ✅ yes | ⚠ **269 passed / 2 failed / 0 skipped** | **264 → 271: +7 tests** *(`TheLayoutIsOneUnitTests`’ new `A0` rails)*; ⛔ **the SAME 2 reds at base — PRE-EXISTING, named below** |
| **4** | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/… --no-build` | ✅ yes | ✅ **1989 passed / 0 failed / 1 skipped** | **1986 → 1990: +4 tests** *(`FindResultsWindowScopeTests`)*, 0 red either side, skips **1 → 1** |
| **5** | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/… --no-build` | ✅ yes | ✅ **234 passed / 0 failed / 1 skipped** *(2 of 3 whole-suite runs; see the flake)* | **232 → 235: +3 tests** *(`ThePerspectiveNamesAreUnifiedTests`)*, skips **1 → 1** |
| **6** | `dotnet test …/Hrot.Blueprints.Tests/… --no-build --filter "FullyQualifiedName~Hrot.Blueprints.Tests.Editor"` ⭐ *the standing gate for anything touching `EditorSubsystem`* | ✅ yes | ✅ **1092 passed / 0 failed / 9 skipped** | **1092 → 1092**, 0 red either side, skips **9 → 9** |
| **7** | `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/… --no-build` | ✅ yes | ✅ **565 passed / 0 failed / 0 skipped** | **0 / 0** |
| **8** | ⭐⭐⭐ **the INTEGRATION invariant** — `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/… --no-build --filter "FullyQualifiedName~BreakpointSubsystemWiringTests"` | ✅ yes | ✅ **25 passed / 0 failed / 0 skipped** | **0 / 0** |
| **9** | `python3 scripts/tracker-counts.py --check` | n/a | ✅ `open 99 / done 333 (+1 refuted)` | table updated in the same commit |
| **10** | `python3 scripts/rulings-check.py` | n/a | ✅ **24/24 verified**, ⚠ **1 staleness WARN on `.claude/CLAUDE.md`** | ⛔ **caused by the COORDINATOR's `6b14d13fe`**, which edited that file during this run; matches the documented pre-existing WARN |
| **11** | `python3 scripts/design-digest.py --check` | n/a | ✅ **all green** — 71 docs, STATUS + INVENTORY + UML all present | **0 / 0** |
| **12** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Perspective_Unification.md` | n/a | ✅ **All 3 blocks parse** | the diagrams I edited |

### ⭐⭐ Row 8, stated honestly — **it gated, and it is the right suite**

⭐ This change is **window registration + perspective resolution**, i.e. cross-cutting UI. 📄 `R-131`
forbids a permanent filter-around, and `BP-378` makes the whole
`Hrot.ClusterRunner.Integration.Tests` un-gateable *(pre-existing DDS-allocator crash)*. ⇒ ⭐ **the
`--filter`ed run above is a REAL gate, not an excuse:** `BreakpointSubsystemWiringTests` asserts
`OwningPerspective` on a window registered through the production wiring, ⭐ and its assertion is one of the
values `A1` changed *(`"Editor"` → `"Scenario"`)* — so it would have gone red on a half-done rename. ⛔ I did
not attempt the whole suite; `BP-378` is unchanged and unaddressed by this batch.

⭐ **And the stale-layout rail** *(`TheDefaultLayoutIsNotStaleTests`, row 4)* — the one that catches a
layout/id mismatch — is **green**. 📐 Expected: this batch renames **perspectives**, and that file keys on
**window ids**, of which the only ones that moved are the runtime `*_watch_*` ids that embed a fresh
`Guid.NewGuid()` and were never listed.

### 🔴 Every RED, confirmed pre-existing **by name** *(row 4)*

| test | verdict |
|---|---|
| `DataDrivenGizmoPredicateTests.D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity` · `…D003_Predicate_True_AllowsUpdateAndDraw` | ⛔ **PRE-EXISTING.** `System.InvalidCastException: Unable to cast 'D003NoOpDrawBuilder' to 'DebugPrimitiveBuffer'` at `DataDrivenGizmoSystem.cs:314`. ⭐ **Reproduced identically in a clean worktree at `c6f54318c`** — same 2 failures, same exception, same line. ⚠ **Not this batch's subject**: `DataDrivenGizmoSystem` is not `PerspectiveCoordinatorSystem`, and nothing in the diff touches it. 📌 It is `R-131`-shaped debt and belongs to whoever owns the gizmo test doubles |
| `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | ⚠⚠ **FLAKY, and I am reporting it as flaky rather than as either colour.** 📐 Measured: **green at base** (1 run), and in my tree **green 2 of 3 whole-suite runs**, **green 3 of 3 when filtered in isolation**. ⭐ It asserts an `AssemblyLoadContext` was GC-collected — timing-dependent by construction. ⛔ Nothing in this diff touches ALC, hot reload or GC. ⇒ 📌 **neither a red nor a green here is evidence**, the `DEBT-AIB-030` shape |

### ⭐ Goldens — as a DIFF SHAPE *(row 3)*

⛔ **No golden moved, because none could:** 📐 the 33-file diff is **31 `.cs` + 2 `.md`** — **zero `.json`,
zero `.ini`, zero golden artefacts.** ⭐ `layout/default/fdp_windows.json` is **deliberately untouched**
*(`A3`: its `ActivePerspective` is `"Blueprint"`, rejected before this batch and rejected after — same
landing, now for a correct reason)*. ⭐ Shape: **937 insertions / 140 deletions**, of which the design doc is
`+128/-…` and the tracker `+19`.

### ⭐ Both quarantine counts *(row 6)*

| suite | skipped, mine | skipped, base | |
|---|---|---|---|
| `Fdp.Presentation.Tests` *(filtered)* | 0 | 0 | ⚠ the suite **cannot run whole** — `BP-419`, pre-existing test-host crash, unchanged |
| `Hrot.ClusterRunner.Tests` | 0 | 0 | |
| `Hrot.Editor.AiShared.Tests` | 1 | 1 | ⭐ unchanged |
| `Hrot.Editor.Tests` | 1 | 1 | ⭐ unchanged — `EditorSystemProfilerWindow…RegistersTheSystemProfilerWindow` |
| `Hrot.Blueprints.Tests` *(Editor filter)* | 9 | 9 | ⭐ unchanged |
| `Hrot.Hsm.Editor.Tests` | 0 | 0 | |

⛔ **No new skip was introduced.** ⭐ Every rail this batch adds runs.

## 4. ⭐ IDS ALLOCATED *(rule 5)* — `BP-488` … `BP-497`

| id | item | |
|---|---|---|
| `BP-488` | `A0` | `SwitchPerspective` refuses an unclaimed perspective — log once per name, no-op |
| `BP-489` | `A0` | the startup/restore perspective comes from `GetPerspectives()`, not `ISubsystem.Name` |
| `BP-490` | `A6` | `owningPerspective` REQUIRED + scope parameter + the two refusals; the DI second site |
| `BP-491` | `A5` | the phantom `Global` perspective, both bugs |
| `BP-492` | `A1`+`A3` | 21 sites renamed; `L6.1b`'s false premise corrected; no migration |
| `BP-493` | `A2` | the label alias dropped |
| `BP-494` | `A4` | the tests follow; one rail inverted; the new set rails |
| `BP-495` | `A9` | CGF's perspective is `Scenario`; no `CGF` perspective remains |
| `BP-496` | `A9` | 🔴 the gizmo map's KEY *(the finding)* |
| `BP-497` | `A10` | the `StrideMock` `perspectiveMap` line |

⛔ **No `ST-`, `TM-`, `HN-` or `MX-` id was allocated or touched.**

## 5. ⛔ WHAT I DID **NOT** DO

| | |
|---|---|
| ⛔ **`A7`** | dissolved before the build — nothing to delete, and ⛔ **the four dormant windows are untouched** *(§1g: ROUTE, don't DELETE — the comparison feature's backend is wired into every registrar and `UtilityDecisionWindow`'s project is referenced by `Fdp.Toolkits`)* |
| ⛔ **`A8`** | withdrawn by the user — ⭐ **no declaration API was built.** `GetPerspectives()` keeps its single derived-from-windows rule |
| ⛔ **Part B** | CGF hosting the editor's asset panels. `A9` gave CGF the perspective NAME; ⛔ no window moved |
| ⛔ **StrideMock removal** | the other lane's — only the one `perspectiveMap` line, per `A10` |
| ⛔ **`BP-378` / `BP-419`** | the two un-gateable suites are reported, ⛔ not fixed. 📌 `R-131` says they are debt to resolve; that is a batch, not a drive-by |
| ⛔ **the `--mode all` first-run fix was not observed RUNNING** | ⭐ It is railed behaviourally through the real `LocalWindowController` + a fake shell *(`TheDefaultIsNeverASubsystemThatClaimsNoPerspective`)*, ⛔ **but nobody has launched `--mode all` and looked.** 📌 `M-39`/`R-124` say that is now possible under Xvfb; ⚠ it did not happen here |

## 6. ⚠ TWO THINGS FOR THE COORDINATOR

| | |
|---|---|
| ⚠ **`.claude/CLAUDE.md`'s lane table still names `claude/blueprint-authoring-status-gm0akp` as the coordinator branch** | 📐 But the handoff, the dispatch and every push of this run are on **`…-6sr5ld`**, and the table's own note calls `6sr5ld` *"a different, now-retired session"*. ⇒ ⭐ **one of the two is stale and it is not mine to decide which.** ⭐ The same commit that re-pointed the two implementation lanes *(`6b14d13fe`)* left this row alone |
| ⚠ **`FdpEntityInspectorHelper.cs` is on neither lane's surface list** | ⭐ I edited it *(correction ①)* because `A1` cannot be complete without it. **Flagging, not defending** — if that file belongs to someone, say so |
