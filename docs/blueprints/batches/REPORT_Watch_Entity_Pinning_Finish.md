<!--STATUS
state: LIVE
build-state: BUILT — both items, plus the user's file-path ruling (which arrived after dispatch).
updated: 2026-08-25
current-answer: this file — what was built, the gates, and the ONE deviation from AQ55's classDiagram.
  ⛔ Design content lives in DESIGN_Variable_Watch_Pinning.md § AS-BUILT (BP-505..BP-507) and in
  Architect_Question_55 § AS-BUILT; this report POINTS at them.
known-conflict: none.
-->
# REPORT — **finish entity pinning in the Watch window** *(UI lane)*

> 📌 Dispatched at `c91b5c80f` · branch `claude/reset-working-branch-qd1qpv` · started-marker `eb10c9de9`.
> ⭐ **ids allocated: `BP-505`, `BP-506`, `BP-507`** *(rule 5)*. ⛔ No PR.

## 1. ⭐ What shipped

| # | | |
|---|---|---|
| 🔴 **①** | **`BP-506` — the inert pin persistence, fixed** | the Watch's pinned rows now actually reach the session file |
| ⭐ **②** | **`BP-507` — `AQ55`'s "Watch this variable on entity…"** | pick an entity on the map, pin the variable on **that** entity |
| 🔒 **③** | **`BP-505` — the user's `2026-08-24` file-path ruling** | user-local session file, force-reset from a git-maintained curated copy on start |

⚠ **③ was not in the handoff** — it is the user's ruling, given after dispatch, and it had to land with ①
because it moves the very file ① writes.

## 2. ⛔⛔ THE CORRECTION the handoff ordered — and it was worse than the handoff knew

The prior batch wrote, in **three** places, that *"`DebugSessionPersistence.Save` has **no production
caller** — only tests call it; the editor's live path still uses the obsolete `SaveWatches`."*

🔴 **FALSE.** `EditorSubsystem.SaveDebugSession()` has called it since `CF-8`, on a 500 ms debounce and
again at shutdown. 📐 **The "measurement" was a `grep` piped through `head`**, truncated at ten test-file
hits before it reached `EditorSubsystem.cs` — ⛔ **a truncated search presented as an exhaustive claim**,
which is precisely what `CLAUDE.md`'s inventory rule forbids.

⭐⭐ **And the real defect was narrower and more familiar:** the caller existed and **did not pass the
optional argument** ⇒ the **SILENT-DEFAULT PATTERN**. ⚠ The wrong diagnosis mattered: *"no caller"* reads
as *"a wire to add later"*, while *"a caller that doesn't pass it"* is a **shipped feature that does
nothing** — which is what it was.

**Corrected in all three places, plus a fourth found while doing it:**

| where | what |
|---|---|
| `DESIGN_Variable_Watch_Pinning.md` § AS-BUILT deviation ⑤ | row truncated + a `🔴🔴 CORRECTION` block below it; the STATUS header points at it |
| `Blueprint_Issues_Tracker.md` `BP-503` | rewritten; the false half removed and named as false |
| `REPORT_Watch_List_Finalization.md` §3/§7 | corrected in place with a pointer here |
| ⭐ **`PinnedVariablePersistence`'s own class remarks** *(not on the handoff's list)* | carried the same claim in code, where it would outlive every document |

## 3. ⭐⭐ Obligation ③ — the design's UML vs what was built

📄 **`AQ55` carries 1 `classDiagram` (5 classes) + 1 `sequenceDiagram` (4 participants).**
⭐ **Everything matches except ONE box, and the deviation is argued in `AQ55` § AS-BUILT:**

| drawn | built |
|---|---|
| `AiWatchWindow ..> IMapPickService : PickEntityAsync` | `AiWatchWindow` takes a **`WatchEntityPicker` delegate**; `EditorSubsystem` implements it with `IMapPickService.PickEntityAsync()` + `FindEntityByNetworkId` |

📐 **Why:** `IMapPickService` lives in `Hrot.Presentation`, and **`Hrot.Editor.AiShared` does not
reference it**. Adding that edge points the shared editor library at the application layer that composes
it. ⭐ The codebase's settled shape for *"an AiShared window needs a host capability"* is a host-installed
delegate — `SetRunStateSource`, `SetFacetEditService`, `SetFacetDispatcher`. ⇒ ⭐⭐ **`Q55-A`'s ruling
(REUSE the pick service) holds exactly; only the boundary crossing differs.**

⭐ `WatchPin`, `NetworkEntityMap` and `EditorMapPickAdapter` are as drawn. The sequence is as drawn, with
two behaviours the diagram did not state and the build had to decide: **a cancelled pick pins NOTHING**
*(⛔ no fallback to the selection)*, and **a chameleon from a picker is REFUSED**.

## 4. 🔒 `BP-505` — the file-path ruling, and the thing that forced the design

> **User, `2026-08-24`:** *"ad file path - user local folder; BUT during development we need clean env
> controlled from git only. let's apply same rule as for curated scenarios and imgui.ini - always
> overwrite the user's copy with git maintained curated copy on start."*

⛔ **The blocking measurement:** the current home is `<repo>/.debug/bpsession.json`, and **`.gitignore:65`
ignores `.debug/`** — 📐 `git ls-files .debug` returns nothing. ⇒ **that path cannot host a
git-maintained curated copy**, so the ruling's two halves are only satisfiable together by moving the
user copy out. ⭐ The per-user data dir is **the alternative `CF-8`'s own design already named**
*(`.dev/blueprint-dbg-1/TASK-DETAIL.md:699`)*.

⭐⭐ **Which of the two named patterns:** the **`imgui.ini`** one. `LayoutPaths.TryResetUserLayout` copies
from the **output directory**, so the reset holds in a deployed build and in CI; `CuratedScenarios` walks
up to the **source tree** and is dev-only by construction. A deterministic clean environment is wanted
everywhere — that is the point of the ruling.

| | |
|---|---|
| git home | ⭐ `debug/default/bpsession.json` *(tracked)* |
| build | `Hrot.ClusterRunner.csproj` `Content … Link="debug\bpsession.json"` — ⛔ the same shape `layout/default/*` already uses |
| user home | `LocalApplicationData/HROT/bpsession.json` *(`LayoutPaths.UserDirectory`)* |
| reset | `DebugSessionPaths.TryResetUserSession`, called **before** `TryLoad` in `RestoreDebugSession` |

⚠⚠ **A side effect worth naming:** 📄 `FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md` — a poisoned
session file killed the editor on **every** launch, recoverable only by deleting a gitignored file by
hand. ⭐ With the reset, the poison survives at most one run.

## 5. GATES *(rule 8 contract)*

| # | gate | command | `--no-build`? | result | delta vs `c91b5c80f` |
|---|---|---|---|---|---|
| 1 | UI-lane unit suite | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --no-build` | ✅ built first | **2007 passed / 0 failed / 1 skipped / 2008 total** | **+8 passed** *(the `AQ55` rails)*; base 1999/2000 |
| 2 | editor suite | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build` | ✅ built first | **245 passed / 0 failed / 1 skipped / 246 total** | **+6 passed** *(the pin/path rails)* |
| 3 | affected projects build | `dotnet build <proj> --no-restore` × `Hrot.Diagnostics.Breakpoints`, `Hrot.Editor.AiShared`, `Hrot.Editor`, `Hrot.ClusterRunner` | n/a | **succeeded, 0 errors** | — |
| 4 | tracker | `python3 scripts/tracker-counts.py --check` | n/a | **OK — open 101 / done 340 (+1 refuted)** | table corrected for the 3 new rows |
| 5 | rulings | `python3 scripts/rulings-check.py` | n/a | **24/24 verified** | ⚠ 3 pre-existing staleness WARNs *(`.claude/CLAUDE.md`, `DESIGN_Headless_Testability.md`, `SOLUTION-OVERVIEW.md`)* — ⛔ none touched by this batch |
| 6 | design docs | `python3 scripts/design-digest.py --check` | n/a | **82 documents OK; every buildable design carries both diagrams** | — |
| 7 | mermaid | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs …AQ55…` | n/a | **2/2 blocks parse** | — |

⭐ **Rows 4–7 of the contract:**
- **RED confirmed pre-existing:** ⛔ **there are none.** Both suites are **fully green**, so no red needed attributing.
- **Skips:** 1 in each suite, both pre-existing and unchanged *(`EditorSystemProfilerWindowDumpsItsModelTests` in the editor suite)*. ⛔ **No new skip.**
- **Goldens:** ⛔ **none moved.** No golden file is touched by this diff; the only new data file is the committed curated `bpsession.json` *(4 empty lists)*, which is a **new** file, not a regenerated one.
- **Working tree clean after every suite run:** ✅ verified — `git status --short` shows only the intended edits.
- **Row 8 (integration suite for a cross-cutting change):** ⚠ **this change is not cross-cutting** — it touches no clock, kernel schedule, orchestrator, transport or cross-node path. Its blast radius is the editor's own session file and one UI gesture, and both are gated by rows 1–2. ⛔ The T3 system suite was not run, per the handoff's *"the E2E/system suite is T3 — never a foreground blocker."*

### ⭐⭐ RED-ON-REVERT — **a rail never seen red is decoration**

| rail | reverted *(inverse edit)* | result |
|---|---|---|
| `ThePinnedRowsReachTheSessionFileTests` | dropped `CapturePinnedVariables(registrars)` from the `Save` call | 🔴 **2 failed / 4 passed** |
| `TheWatchPinsOnAPickedEntityTests` | disabled `Watch.SetEntityPicker(entityPicker)` in the registrar | 🔴 **3 failed / 5 passed** |

⭐ Both restored by the inverse edit *(⛔ never `git checkout --`)* and re-run green.

## 6. ⭐ Obligation ⑤ — the design carries the as-built

| doc | what landed |
|---|---|
| 📄 [`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md) | a second **AS-BUILT** section *(`BP-505`…`BP-507`)* + the `🔴🔴 CORRECTION` to deviation ⑤ + the prior "still open" table marked resolved. STATUS header updated and points at both |
| 📄 [`Architect_Question_55_…`](../Architect_Question_55_Watch_Concrete_Entity_Picker.md) | `build-state: BUILT`, an **AS-BUILT** section per sub-question, and the one deviation argued |

## 7. 🔴 Still open — **stated plainly**

| ⛔ | |
|---|---|
| **A concrete pin persists across editor SESSIONS but does NOT survive a scenario RELOAD** *(`94g` / `BP-503`)* | ⭐ **now unblocked** — `HN-037`'s remap merged — but it edits `DataBreakpointManager` *(`:1354` still throws for `NetworkId`)* ⇒ its own slice, as the handoff §3 directed |
| **A restored pin is not re-attached to a Watch window** | `PinnedVariablePersistence.Restore` yields descriptors and nothing consumes them: a row can only be rebuilt by the source that owns its asset, once that asset is open — **the same resolution problem as `94g`**, and it belongs in that slice |
| **The two ruling-9 duplicates** *(two `IMapPickService`, two `MapPickableEntityAttribute`)* | untouched, as `AQ55` directed. Each is its own cleanup |
| **`BP-504`** *(seven `StubBreakpointManager` copies)* | still open — ⚠ this batch added an **eighth**-style local fake in `Hrot.Editor.Tests` *(a `NoRefactor` + real `DataBreakpointManager`)* rather than an eighth stub; the count is unchanged |

## 8. ⭐ The id tripwire — **already resolved, and not by me**

⚠ The prior batch left `The_two_hosts_allocate_ids_from_different_authorities` as a tripwire that
instructed its own deletion once the allocators unified. 📐 **Checked:** `HN-037` reddened it on
`2026-08-24` and **deleted it as it asked**, replacing it with
`The_two_hosts_number_the_same_entities_identically`. ⛔ Nothing for this batch to do; recorded so the
loop is visibly closed rather than assumed.
