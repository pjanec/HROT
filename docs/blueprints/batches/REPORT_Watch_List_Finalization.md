<!--STATUS
state: LIVE
build-state: BUILT — all four items. ⚠ Two halves deliberately deferred (BP-503), both for the same
  collision reason the dispatch named.
updated: 2026-08-24
current-answer: §1 per item · §2 the premise that did not hold · §3 obligation ③/⑤ (five deviations) ·
  §4 §Gates · §5 the mutation table · §6 the ids · §7 what is still open.
design-basis: 📄 docs/blueprints/DESIGN_Variable_Watch_Pinning.md §1/§1b/§3/§5 + its new § AS-BUILT
  (written by this batch) · Architect_Question_46 (value-feed rules) ·
  docs/blueprints/batches/HANDOFF_Watch_List_Finalization.md (dispatched 304b9180e).
known-rot: none. ⚠ EPHEMERAL — the durable record is DESIGN_Variable_Watch_Pinning.md § AS-BUILT.
-->
# REPORT — **the watch-variable list, finalized** *(`BP-499`…`BP-502`)*

> ⛔⛔ **This report is EPHEMERAL.** ⭐⭐⭐ The durable record is
> **[`DESIGN_Variable_Watch_Pinning.md` § AS-BUILT](../DESIGN_Variable_Watch_Pinning.md)** — written
> before this batch closed *(obligation ⑤)*.

⭐⭐ **Headline:** all four items shipped. ⚠ **One premise in the dispatch did not hold** *(there was no
group-by control to mirror — §2)*, and **two halves are deliberately deferred** *(`BP-503`)*, both for the
collision reason the handoff itself named.

## 1. ⭐⭐ PER ITEM

| # | item | state |
|---|---|---|
| ✅ **①** | wire grouping into the Watch | **BUILT.** The window built its model with **no `groupBy`** ⇒ it fell back to `DetailsDefault` (`[]`) and rendered **one flat list** — on the one surface that mixes assets and entities by design. One wiring line; ⛔ no second grouping path |
| ✅ **②** | group-by selector | **BUILT, and SHARED** — ⚠ but not by mirroring anything: §2 |
| ✅ **③** | persist the pin set | **BUILT** — a fourth list in the existing debug-session file. ⛔ `SaveWatches` NOT revived *(obsolete, breakpoint-only)*. 🔴 **No production caller saves one yet** — §7 |
| ✅ **④** | the concrete-vs-chameleon CHOICE | **BUILT.** `EntityBinding` + `Pin(row, binding)`. ⚠ Concrete = *"the entity selected right now"*, captured — ⛔ not the picker, ⛔ not the restart remap, exactly as carved out |

## 2. ⚠⚠ THE PREMISE THAT DID NOT HOLD — **item ②**

⭐ The dispatch says: *"a toolbar control **mirroring** `AiVariablesWindow.GroupBy` (`:145`)"*.

📐 **Measured:** that member is a **property forwarding to `_model.GroupBy`**, and a repo-wide search for
a writer found **only the model's own constructor**. ⇒ ⛔ **no group-by UI existed anywhere** — not in the
Variables window either. ⭐ There was nothing to mirror; this is the first one.

⇒ ⭐⭐ **So it was built SHARED** *(`VariableGroupBySelector`)* rather than inside the Watch window, because
the Variables window needs the identical control and would otherwise grow a divergent one *(ruling 9)*.
⚠ **Wired to the Watch only** — adopting it in Variables is a one-line change that window's own batch can
make; ⛔ doing it unasked would change a surface this batch was not sent to touch.

⭐ **One defect deliberately not repeated:** a grouping no mode names answers **`-1`**, not `0`. 📌 Falling
back to `0` is exactly what `BP-114` fixed in the type picker — it would show *"Asset, then entity"* while
the model was grouped some other way.

## 3. ⭐⭐⭐ OBLIGATIONS ③ AND ⑤

**③ — checked against the design.** ⭐ The dispatch waives new UML *(finalization/wiring on already-built,
already-designed machinery — the documented-recipe exception)*. §1/§1b/§3/§5 were read end-to-end and the
build matches them except where §"the deviations" says otherwise. **Five deviations:**

| # | the design said | 📐 what was built |
|---|---|---|
| **①** | mirror the existing group-by control | ⛔ **none existed** — §2 |
| **②** | §3: the binding is on the ROW | ⭐ **It is on the PIN.** `VariableRowOrigin` is unchanged; the source keeps a parallel key→binding map. ⭐ The binding is a property of *the designer's choice*, and widening the row identity would have touched every construction site and the highlight-cache key for a fact only the Watch has |
| **③** | §3/§5: concrete keys on the **STAGING** id via the published `oldToNewMap` | ⚠⚠ **It keys on the RUNTIME `NetworkIdentity`** ⇒ a concrete pin **does not survive a scenario restart**. ⛔ The remap is still a local inside `StagingEntityExtractor`; publishing it edits `EditorSubsystem`/`EditorApplication`, the allocator batch's files. ⇒ deferred by the handoff §2 and **said out loud in `EntityBinding`'s remarks** so nobody reads *"persisted"* as *"restart-proof"* |
| **④** | *(unstated)* | ⭐ **The chameleon REUSES the existing sentinel.** `EntityBinding.OriginEntity` projects it to `default(Entity)` — what `StagedWriteView.EntityFor` and `VariableChangeMonitor` already read ⇒ ⛔ nothing downstream changed, and there is no second encoding of *"follow the selection"* |
| **⑤** | §5: *"extend `SaveWatches`/`LoadWatches`"* | ⛔ Those route to the `[Obsolete]` `WatchPersistence` and are breakpoint-only ⇒ `DebugSessionPersistence` extended instead, **as the dispatch directed**. 🔴🔴 **CORRECTED `2026-08-25`:** this row's claim that it *"has no production caller"* was **FALSE** — see §7 |

**⑤ — folded into [`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md)** before this
batch closed: a new **§ AS-BUILT** carrying what shipped, all five deviations, the two persistence honesty
rules, and the named remaining work. ⭐ The STATUS block gains a `build-state` pointing at it.

## 4. ⭐⭐⭐ §GATES

| # | gate | verbatim command | `--no-build`? | result · delta vs `304b9180e` |
|---|---|---|---|---|
| 1 | build | `dotnet build IOS-IG-SimHost.sln --no-restore` | must build | ⭐ **0 errors** |
| 1 · 8 | ⭐⭐⭐ **the touched suite** | `dotnet test Hrot.Editor.AiShared.Tests --no-build` | `--no-build` | ⭐⭐ **1999 / 2000 pass, 0 fail, 1 skip** *(baseline **1989 / 1990**, MEASURED at a clean tree via `git stash push -u` ⇒ **+10**, this batch's rails)*. ⚠ The 1 skip is pre-existing and present at baseline |
| 8 | ⭐⭐ **the integration gate** *(the Watch `PanelSnapshot` rails live here)* | `bash scripts/run-system-tests.sh` | builds | ⭐ **83 / 83 pass, 0 fail, 0 skip** — unchanged |
| 2 | out-of-solution / stale bin | — | — | ⭐ both gated projects are in `IOS-IG-SimHost.sln`; every `--no-build` run followed a full build of the same tree |
| 3 | golden movement | `git status --short Hrot/Runner/Hrot.SystemTests/Goldens/` | — | ⭐ **ZERO — 0 files.** ⭐ Expected: the golden budget holds no Watch panel, and grouping changes the Watch's VIEW only |
| 4 | every RED pre-existing, by name | — | — | ⭐ **no reds anywhere on the final tree** |
| 5 | working tree clean after every suite | `git status --short` | — | ⭐ clean; all three mutation probes reverted by **inverse edit** and verified *(`grep -rc "MUTATION PROBE"` ⇒ nothing)* |
| 6 | quarantine counts | — | — | ⭐ **1 skip before, 1 skip after** *(the same pre-existing one)*. ⛔ No new filter |
| 7 | doc gates + ids | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` | — | ⭐ **OK (open 101 / done 337)** — ⚠ the count TABLE needed updating with the new rows, which is what `--check` is for · **24/24 verified, 3 known staleness WARNs** · **81 designs OK** |

## 5. ⭐⭐⭐ THE MUTATION TABLE — **the handoff's row 8**

| # | mutation *(reverted by inverse edit)* | what reddened | expected? |
|---|---|---|---|
| **M7** | ⭐⭐ **revert the `WatchDefault` wiring** — build the model with no `groupBy`, i.e. the pre-`BP-499` state | `TheWatchWindowGroupsByAssetThenEntity`: *"Expected: `[Asset, Entity]` · Actual: `[]`"* | ✅ yes — **this IS the defect item ① fixes**, quoted back |
| **M8** | ⭐⭐ **let a chameleon pin keep its concrete entity** *(drop the row rewrite)* | **two** rails: `AChameleonPinCannotKeepAConcreteEntityOnItsRow` and `AConcretePinKeepsItsEntityWhileAChameleonFollowsTheSelection` ⇒ the stored row and its binding would have disagreed — a silent lie in the *"looks fine"* direction | ✅ yes |
| **M9** | ⭐⭐ **coerce an unknown `BindingKind` instead of skipping it** | `AnUnknownBindingKindIsSkippedNotCoercedToConcrete` ⇒ a future kind would become a **concrete pin on entity 0** and show the wrong entity's value | ✅ yes |

⭐ Each probe rebuilt before a conclusion was drawn, and all three restored and re-verified.

## 6. ⭐ RULE 5 — the ids allocated

| id | |
|---|---|
| ✅ **`BP-499`** | grouping wired into the Watch |
| ✅ **`BP-500`** | the shared group-by selector |
| ✅ **`BP-501`** | `EntityBinding` — the two kinds and the choice |
| ✅ **`BP-502`** | the pin set persists |
| 🔴 **`BP-503`** | **open** — restart survival + no production save/load |
| ⚠ **`BP-504`** | **open** — seven copies of a 40-line `StubBreakpointManager` |

⭐ Series continued from `BP-498` as the dispatch instructed; ⛔ no `HN-` id touched, so no collision with
the concurrent allocator batch.

## 7. 🔴 WHAT IS STILL OPEN — **stated, not smoothed**

| ⛔ | ⭐ |
|---|---|
| **a concrete pin does not survive a scenario RESTART** *(`BP-503a`)* | §5's staging-id remap edits `EditorSubsystem`/`EditorApplication` — the allocator batch's files. ⇒ carved out by the dispatch §2, and the limitation is written into `EntityBinding`'s own remarks rather than left for a reader to discover |
| 🔴 **nothing in the editor SAVES a pin set** *(`BP-503b`)* | 🔴🔴 **THIS ROW WAS FALSE — CORRECTED `2026-08-25`.** It claimed *"`DebugSessionPersistence.Save` has NO production caller — only tests"*. `EditorSubsystem.SaveDebugSession()` has called it since `CF-8`; the measurement was a `grep` piped through `head`, truncated before it reached the caller. ⭐ The real defect was that the caller **did not pass the optional `pinnedVariables` argument** — the SILENT-DEFAULT pattern. Fixed by `BP-506`. 📄 [`REPORT_Watch_Entity_Pinning_Finish.md` §2](REPORT_Watch_Entity_Pinning_Finish.md) |
| **the map-picker** | ⭐ carved out for a short architect nod, which the coordinator has now written as **`AQ55`** *(merged during this batch's rule-4 pull)*. ⭐ `BP-501` ships the *"current selection"* concrete case, which needs no picker |
| ⚠ **Variables has not adopted the selector** | deliberate — §2 |
| ⚠ **seven `StubBreakpointManager` copies** *(`BP-504`)* | ⭐ counted while placing `BP-499`'s window rail, which went into the EXISTING Watch-window test file rather than adding an eighth |
