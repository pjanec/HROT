<!--STATUS
state: LIVE
build-state: BUILT — items ①②③④. Item ⑤ SPLIT with the measurement, as the handoff instructed.
updated: 2026-08-25
current-answer: this file — what was built, the gates, and the THREE deviations (including §8a's open
  decision resolved differently from BOTH options it offered).
  ⛔ Design content lives in DESIGN_Variable_Watch_Pinning.md § AS-BUILT 94g; this report points at it.
known-conflict: none.
-->
# REPORT — **94g: a concrete watch pin survives a scenario reload** *(UI lane)*

> 📌 Dispatched at `544bf52b3` · branch `claude/reset-working-branch-qd1qpv` · started-marker `b52ebac46`.
> ⭐ **ids allocated: `BP-508`, `BP-509`, `BP-510`, `BP-511`, `BP-512`** *(built)* and
> **`BP-513`, `BP-514`** *(open findings)* — rule 5. ⛔ No PR.

## 1. ⭐ What shipped

| item | | id |
|---|---|---|
| **①** | **`oldToNewMap` is PUBLISHED** — an `OnRemap` callback sink on the extractor, wired to the control-plane bus by **all three** subsystems that construct one | `BP-509` |
| **②** | **ONE `NetworkIdResolver`** — the four `FindEntityByNetworkId` copies route through it | `BP-508` |
| **③** | **A concrete pin stores the AUTHORED id and RE-BINDS on a reload** | `BP-510`, `BP-511` |
| **④** | **`DataBreakpointManager`'s `NetworkId` arm ANSWERS instead of throwing** | `BP-512` |
| **⑤** | ⛔ **SPLIT — measured, it is not a wiring line** *(§4)* | `BP-513` |

📄 **The as-built is in the design**, per obligation ⑤ and the "design content in the design" rule:
**[`DESIGN_Variable_Watch_Pinning.md` § AS-BUILT — slice 3 / `94g`](../DESIGN_Variable_Watch_Pinning.md)**.

## 2. ⭐⭐ Obligation ③ — §8a's UML vs what was built

📄 **§8a carries 1 `classDiagram` (7 classes) + 1 `sequenceDiagram` (7 participants).**
⭐ The FLOW is as drawn. **Three deviations**, all argued in the design's as-built section:

| # | drawn / asked | built |
|---|---|---|
| **①** | `EntityBinding ..> EditorApplication : staging to runtime via published map` | ⛔ `EditorApplication` is **not in the path**. `EntityBinding` is a `readonly record struct` in `AiShared` and cannot reach the application layer. ⭐ Built as **`StagingRemapView`** *(the pure table)* + **`WatchEntityIdentity`** *(table + two host ECS delegates)*, and **`EditorSubsystem`** drains the bus — because the three Watch windows hang off **its** registrars |
| **②** | §8a: *"the `NetworkIdResolver` scan or the `NetworkEntityMap` index — report which you chose and WHY"* | ⭐⭐⭐ **NEITHER** — see §3 |
| **③** | §8 ②: *"there are FOUR copies"* | ⚠⚠ **still an undercount** — see §5 |

## 3. ⭐⭐⭐ §8a's OPEN DECISION, resolved — **and it is neither option**

> §8a: *"whether `DataBreakpointManager:1354` wants the consolidated `NetworkIdResolver` scan or the
> maintained `NetworkEntityMap` index. ⛔ Report which, and why — do not silently pick."*

📐 **Measured:** `EvaluateLifecycleTrackers` calls `MatchesLifecycleCriteria` **once per active entity,
per tracker, per tick.**

| candidate | verdict |
|---|---|
| the `NetworkIdResolver` scan | ⛔ **O(entities²) per tick** |
| the `NetworkEntityMap` index *(in-degree 541, measured — the design said 131)* | ⛔ **unnecessary** |
| ⭐⭐⭐ **the entity's OWN `NetworkIdentity`** | ✅ **O(1), nothing to keep in step, nothing to go stale** |

⭐⭐ **Why the third answer is the right one and not a shortcut:** the predicate does not ask *"which
entity has id N?"* — it asks *"is **THIS** entity the one with id N?"*, of an entity it was **handed**.
⭐ It is also the shape the sibling arms already have: `EcsHandle` compares the handle, `NameSubstring`
reads the entity's own name component. ⇒ ⛔ a lookup service here would have been the **only** arm that
reached outside the entity it was given.

⚠⚠ **And the comment prescribing the fix was a dead lead:** it said *"pass an `INetworkEntityMap` to the
`DataBreakpointManager` constructor"*. 📐 **`INetworkEntityMap` does not exist** — the name appears only
in that file's own comments. ⛔ Following it would have meant inventing an interface to satisfy a comment.

⚠ **It threw from inside the tick loop**, so authoring one of these breakpoints did not merely fail to
work — it took the frame down. 📄 Cf. `FINDINGS_Empty_Breakpoint_Bricks_The_Editor.md`.

## 4. ⛔ Item ⑤ — **SPLIT, with the measurement the handoff asked for**

> Handoff ⑤: *"If this is bigger than a wiring line, SPLIT it and say so — do not stall ①–④."*

📐 **Measured:** row sources are constructed **inside window draw paths** — `BlueprintMyBlueprintWindow`
`:567`/`:600` build `SectionVariableRowSource` while drawing, and the registrar's `_sectionSource`
factory is keyed by **SECTION, not asset**. ⇒ ⛔ **nothing in the editor can answer *"give me the row for
`(AssetId, Section, VariablePath)`"***, which is exactly what re-hydrating a restored pin needs.

⇒ it needs **either** a deferred-pin queue that sources drain when they next build rows, **or** an
asset-keyed row-source registry — with real lifetime questions *(what happens when the asset closes?)*.
⭐ **Filed as `BP-513`.** ⛔ Items ①–④ were not stalled for it.

⭐ **State plainly, as the handoff asked:** **a concrete pin now persists across editor sessions AND
survives a scenario reload within a session; a pin restored FROM THE FILE is not yet re-attached to a
window.**

## 5. ⚠⚠ A FINDING — **`R-77`'s count was still wrong, and the reason generalises**

`R-77` was corrected once already *(two → four)*. 📐 **Scanning for the lookup SHAPE** *(a
`NetworkIdentity` read whose `.Value` is compared)* **rather than the method NAME** found more:

| where | what |
|---|---|
| 🔴 **`EditorSubsystem.cs` ×3** *(`:2511`, `:4940`, `:4967`)* | **inline** lookup loops — ⚠ in the **same file** that also held a named copy. ⭐ **Routed by this batch** |
| ⚠ `MissionControlBehaviorParamsHelper.cs` ×2 | the same FILE NAME in `Hrot.SimHost/Systems/` and `Hrot.Core/Systems/Common/` — a ruling-9 duplicate in its own right, both out of lane. ⛔ Routing one without the other would make them diverge ⇒ `BP-514` |
| ⚠ `EqsResultUpdateSystem`, `EqsResultIngressTranslator` | a **different** shape — *"find the child whose PARENT is N"* over `PartMetadata` ⇒ needs its own seam, not the resolver ⇒ `BP-514` |

⇒ ⭐⭐ **The lesson: a NAME scan cannot count a duplicated MECHANISM.** ⭐ The anti-fifth rail therefore
matches the SHAPE and carries an explicit allow-list — **the measured set, each entry with its reason** —
so a NEW inline lookup reddens it while the filed ones stay filed. ⛔ Asserting the aspirational answer
would have meant quarantining the rail until an out-of-lane cleanup landed, and a quarantined rail
catches nothing.

## 6. 🔴 A defect the design did not name, found while building

⛔⛔ **The ORDINARY "Watch this variable" gesture made a pin that could NEVER persist.**
📐 `PinnedVariableRowSource.Pin(row)` with no binding **infers** one, and its concrete arm has **no id
source**, so it wrote `Concrete(0, entity)` — every time. ⚠ Honest *(`IsPersistable` answered false)* and
useless: the gesture a designer actually uses produced a within-session pin.
⭐ `AiWatchWindow.BindingFor` now supplies the authored id and the registrar's `ToggleWatch` **passes**
it. 📐 Red on revert.

## 7. GATES *(rule 8 contract)*

| # | gate | command | `--no-build` | result | delta vs `544bf52b3` |
|---|---|---|---|---|---|
| 1 | UI-lane unit suite | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --no-build` | ✅ built first | **2016 passed / 0 failed / 1 skipped / 2017** | **+9 passed** *(the reload rails)*; base 2007/2008 |
| 2 | breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests --no-build` | ✅ | **164 passed / 0 failed / 0 skipped** | **+7** *(6 new + 1 INVERTED rail split into 2)*; base 163 with **1 red** *(the throw-pinning rail — see §8)* |
| 3 | editor | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build` | ✅ | **247 passed / 1 failed / 1 skipped / 249** | **+3** *(the resolver rails)*. ⚠ The 1 red is a **pre-existing flake** — see §8 |
| 4 | replay browser | `dotnet test Hrot/Subsystems/Hrot.ReplayBrowser.Tests --no-build` | ✅ | **30 / 0 / 0** | unchanged |
| 5 | presentation | `dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build` | ✅ | **55 / 0 / 0** | unchanged |
| 6 | extractor *(item ①)* | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter StagingEntityExtractorTests` | ✅ | **21 passed / 0 failed** | **+3**. ⚠ The FULL suite is order-flaky — see §8 |
| 7 | affected projects build | `dotnet build <proj> --no-restore` × `Fdp.Toolkits`, `Hrot.Presentation`, `Hrot.ReplayBrowser`, `Hrot.CGF`, `Hrot.Diagnostics.Breakpoints`, `Hrot.Editor.AiShared`, `Hrot.Editor` | n/a | **succeeded, 0 errors** | — ⛔ **No full-solution build in the fix loop** *(the `2026-08-24` rule)* |
| 8 | tracker | `python3 scripts/tracker-counts.py --check` | n/a | **OK — open 102 / done 346 (+1 refuted)** | table updated for 7 new rows |
| 9 | rulings | `python3 scripts/rulings-check.py` | n/a | **24/24 verified** | ⚠ 3 pre-existing staleness WARNs *(`.claude/CLAUDE.md`, `DESIGN_Headless_Testability.md`, `SOLUTION-OVERVIEW.md`)* — none touched here |
| 10 | design docs | `python3 scripts/design-digest.py --check` | n/a | **82 documents OK** | — |
| 11 | mermaid | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs …DESIGN_Variable_Watch_Pinning.md` | n/a | **2/2 blocks parse** | — |

⭐ **Contract rows:**
- **Goldens:** ⛔ **none moved.** No golden file is touched by this diff and no new data file is added.
- **Working tree clean after every suite run:** ✅ verified.
- **New skips:** ⛔ **none.** The 1 skip in gates 1 and 3 is pre-existing and unchanged.

### ⭐⭐ RED-ON-REVERT — every rail seen red, by INVERSE EDIT *(⛔ never `git checkout --`)*

| item | inverse edit | result |
|---|---|---|
| **③** rebind | `RebindConcretePins` returns 0 immediately | 🔴 **2 failed / 7 passed** |
| **③** durable key | `BindingFor` drops the authored id *(back to `Concrete(0, entity)`)* | 🔴 **2 failed / 7 passed** |
| **①** sink | `OnRemap?.Invoke(…)` commented out | 🔴 **3 failed / 18 passed** |
| **④** | the `NetworkId` arm restored to `throw` | 🔴 **6 failed / 0 passed** |
| **②** anti-fifth | ⚠ **seen red twice during the build itself**, naming `EditorSubsystem.cs` *(before the three inline loops were routed)* and `DataBreakpointManager.cs` *(before it was allow-listed with its reason)*. ⭐ Genuine red-on-revert evidence from the actual build, ⛔ not a contrived edit afterwards |

⭐ All inverse edits restored and re-run green; `grep -rn "INVERSE EDIT"` is clean.

### ⛔⛔ Row 8 — the CROSS-CUTTING integration suite

The reload path crosses **CGF → control-plane bus → editor**, so per the contract it needs an integration
suite, not only unit rails.

| suite | verdict |
|---|---|
| ⭐ **`Hrot.SimHost.Tests` → `StagingEntityExtractorTests`** *(gate 6)* | ✅ **the CGF half, run and green (21/21)** — the sink fires, the table is a copy, an empty scenario publishes an empty table |
| ⭐ **`Hrot.Editor.AiShared.Tests` → `AConcretePinSurvivesAReloadTests`** *(gate 1)* | ✅ **the bus + editor half, run and green** — including a rail that publishes `StagingRemapPublishedEvent` through a **real `FdpEventBus`** with the real `OrchestrationEventRegistry` and reads it back whole. ⭐ That is the transport contract, asserted rather than trusted |
| ⭐⭐ **`Hrot.SimHost.Tests` → `EditLoadClusterOpHandlerTests`** | ✅ **RUN AND GREEN, 3/3 in isolation** — the **editor's own load handler → `StagingEntityExtractor` → spawn** path, i.e. the load this slice re-binds against. ⭐ This is the integration gate for the editor half |
| 🔴 **`Hrot.ClusterRunner.Integration.Tests` → `AllSubsystemsClusterTransitionTests`** | ⛔ **CANNOT GATE — and that is a reported FINDING with base-sha evidence, not an omission.** 📐 Measured **2 failed / 2 total** *(`AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate`, `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle`)* — and **IDENTICALLY 2 failed / 2 at the base** *(`git stash push -u` + rebuild at `544bf52b3`)*. ⇒ pre-existing, unchanged by this batch. 📌 Consistent with `CLAUDE.md`'s note that this project's DDS-allocator crash makes it un-gateable |
| ⛔ **`Hrot.ClusterRunner.Integration.Tests` → `DebugApiScenarioLoadTests`** | ⛔ **CANNOT GATE — it is EXCLUDED FROM COMPILATION.** 📐 `Hrot.ClusterRunner.Integration.Tests.csproj:72` — `<Compile Remove="DebugApiScenarioLoadTests.cs" />`, one of 15 files quarantined under **`DEBT-MCP-001`** *(a diverged `EditorHarness`)*. ⚠ It is otherwise the ideal suite here — it pumps the real `EditorSubsystem.Update()` frame order through a load — ⭐ so it is named rather than passed over, and un-quarantining it would gate this slice properly |

## 8. ⚠ EVERY RED, NAMED AND ATTRIBUTED

| red | verdict |
|---|---|
| `Hrot.Editor.Tests.AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | ⚠ **PRE-EXISTING FLAKE, not mine.** 📐 It is a GC/`AssemblyLoadContext`-collection test; it **passed in isolation twice** and passed in an earlier full-suite run of the same binary. ⛔ Nothing in this batch touches hot reload |
| `Hrot.SimHost.Tests` — **12 red at the base, 9 red with this batch** | ⚠⚠ **PRE-EXISTING and ORDER-FLAKY.** 📐 **Measured both ends:** base *(`git stash push -u` + rebuild)* = **12 failed / 657**; with this batch = **9 failed / 660**. ⚠ **The failing SET rotates between runs of the same binary** — `CgfLogicPackTests`, `JsonToRecordCompilerTests`, `FullBranchPipelineTests`, `HierarchySerializationIntegrationTests`, `GenesisIntentComponentsTests`, `EditLoadClusterOpHandlerTests` and various `StagingEntityExtractorTests` all appear in some runs and not others. ⇒ a static `ComponentTypeRegistry` order dependency, the same family as `DEBT-AIB-030`. ⭐ **The count went DOWN by 3**, and my three new rails pass in isolation (gate 6). ⛔ This suite cannot gate at suite level, and that is a pre-existing property |
| `Hrot.Diagnostics.Breakpoints.Tests.LifecycleNetworkIdTests.Lifecycle_NetworkId_NoMapWired_ThrowsNotSupportedException` | ⭐⭐ **INVERTED, not deleted** — 📌 the `BP-494` precedent: *a test asserting the old behaviour is corrected to the measured set*. It pinned the `throw`, which `BP-512` deliberately removes. ⭐ It is now **two** facts: the arm RESOLVES the entity, and it does NOT fire for an entity without that id. ⭐ The original P11 setup is kept so the inversion is visible in one diff |
| `Hrot.ClusterRunner.Integration.Tests.AllSubsystemsClusterTransitionTests` ×2 | ⚠ **PRE-EXISTING, proven against the base sha.** 📐 2 failed / 2 with this batch **and** 2 failed / 2 at `544bf52b3` *(stash + rebuild)*, same two names. ⇒ unchanged by this batch |
| `Hrot.Editor.Tests.Adapters.EditorMissionServiceTests` ×3 | 🔴 **A REAL REGRESSION I INTRODUCED, and then corrected — reported rather than quietly fixed.** 📐 The fixture used `(long)entity.Index` as the network id, and the FIRST entity in a fresh repo has `Index == 0` ⇒ the tests were exercising **network id 0**. The consolidated resolver refuses `id ≤ 0` *(a guard two of the four copies already had)*. ⚠ **0 is the "no network identity" sentinel everywhere else** — `IsPersistable` treats it as not durable, the allocator starts far above it. ⇒ ⭐ a **fixture artefact, not a product requirement**: the tests are corrected to a real non-zero id *(still derived from the entity, so they still assert the lookup finds the right one)*, ⛔ the guard was **not** dropped to keep them green |

## 9. ⭐ WHEN YOU ARE DONE — obligation ⑤

| doc | what landed |
|---|---|
| 📄 [`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md) | a new **AS-BUILT — slice 3 / `94g`** section: what shipped, the three deviations, the flow as built, and what is still open. STATUS header now says the whole design is BUILT and points at this section first |

## 10. 🔴 Still open

| ⛔ | |
|---|---|
| **`BP-513`** — a pin RESTORED from the session file is not re-attached to a window | §4's measurement; needs a deferred-pin queue or an asset-keyed row-source registry |
| **`BP-514`** — more inline entity-by-network-id lookups outside the UI lane | §5; each named in the rail's allow-list with its reason |
| **`BP-504`** — seven `StubBreakpointManager` copies | unchanged |
| the two ruling-9 duplicates `AQ55` flagged · `HsmDebugSession`/`BTreeDebugSession` wiring *(§8 slice 4)* | unchanged, as the handoff directed |
