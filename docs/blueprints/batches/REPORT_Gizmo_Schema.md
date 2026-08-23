<!--STATUS
state: LIVE
updated: 2026-08-23
current-answer: the whole file — the batch report for HANDOFF_Gizmo_Schema.md.
  Design content is NOT here: it is folded into Architect_Question_52_Gizmo_Family_Composition.md §6.
known-conflict: none.
-->
# REPORT — gizmo schema follows declaration

> 📌 **Dispatch:** [`HANDOFF_Gizmo_Schema.md`](HANDOFF_Gizmo_Schema.md), frozen at **`7977adace`**.
> Branch `claude/blueprint-macro-feature-sdmspn` (BACKEND lane).
> **ids allocated: `ST-022`, `ST-023`, `ST-024`, `ST-025`** *(rule 5)*. Order per §1: **T1 → T3 → T2**.

## 1. OUTCOME

| item | outcome |
|---|---|
| **T1** — `ST-022` | ✅ **`--mode ig` starts and ticks.** `MapSchemaPack`, 5 registrations, zero new project edges |
| **T3** — `ST-023` | ✅ **per-host rail, 5 / 0.** Red proven. ⚠ Keyed on **namespace**, not assembly — see §4 |
| **T2** — `ST-025` | ✅ **tripwire removed** — and it **failed first**, which is how it left quarantine |
| ⭐⭐ **bonus, measured** | 🔴 **`Hrot.IG.Tests` 346 / 69 → 409 / 6.** The fix repaired **61** pre-existing failures |
| ⚠ **findings filed** | `ST-024` — the rail's first `editor` red was the RAIL's fault; `replaybrowser` not covered |

⭐ **Obligation ③:** the design carried **1 `classDiagram` (9 boxes)** and **1 `sequenceDiagram`**. Built
matches on mechanism, with **two deviations**, both argued and folded into
[`Architect_Question_52`](../Architect_Question_52_Gizmo_Family_Composition.md) **§6** *(now
`build-state: BUILT`)*.

## 2. ⭐⭐ §GATES

| # | gate — verbatim command | `--no-build`? | result | delta vs `7977adace` |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | builds | ✅ **0 errors**, 74 warnings | none |
| 2 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests --no-build --filter FullyQualifiedName~GizmoSchemaFollowsDeclarationRails` | `--no-build` | ✅ **5 / 0** | **+5 new** |
| 3 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter Category=SystemModes` | `--no-build` | ✅ **8 / 0** | `ig` **quarantined → healthy**; quarantine table gone |
| 4 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build` *(whole suite)* | `--no-build` | ✅ **52 / 0** | matches the dispatch's stated baseline exactly |
| 5 | `dotnet test Hrot/Subsystems/Hrot.IG.Tests --no-build` | `--no-build` | ⚠ **409 / 6** | 🔴 **from 346 / 69 — 61 failures REPAIRED** (row B) |
| 6 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests --no-build` *(whole)* | `--no-build` | ⚠ **272 / 2** | 2 pre-existing (row A) |
| 7 | `dotnet test Hrot/Engine/Hrot.Common.Tests --no-build` | `--no-build` | ⚠ **53 / 3** | none — pre-existing (row C) |
| 8 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ OK — open **99** / done **333** | ⚠ blind to `ST-` rows |
| 9 | `python3 scripts/rulings-check.py` | n/a | ✅ **24/24** | 2 staleness WARNs, already-known, not mine |
| 10 | `python3 scripts/design-digest.py --check` | n/a | ✅ clean (82 docs) | none |

**Mode matrix** *(gate 3 is the automated form)*: `editor` ✅ · `all` ✅ · `simhost` ✅ · `cgf` ✅ ·
`excon` ✅ · `orchestrator` ✅ · `replaybrowser` ✅ · ⭐ **`ig` ✅ (was 🔴)**. `stridemock` still refused.

⚠ **`mermaid-check.mjs` SKIPPED** — *"mermaid/jsdom not resolvable here"*, needs an `npm install` this
session does not have. ⭐ **No Mermaid block was added or edited** (§6 is prose and tables only), so nothing
new is unvalidated; the existing diagrams are as the coordinator pushed them.

### Every RED, confirmed pre-existing **by name**

| | red | evidence |
|---|---|---|
| **A** | `D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity` · `D003_Predicate_True_AllowsUpdateAndDraw` | confirmed pre-existing at `c6f54318c` and `5963fffd4` in earlier batches; unchanged here |
| **B** | `CS011_CommanderIdZero_WithExistingUnitSubordinate_PublishesCmdRemove` · `CS011_CommanderPresent_ImmediateCmdAssignSubordinate` · `CS011_CommanderUpdate_ScrubsOldPendingQueue` · `CS011_DeferredResolvedOnEntityRegistered` · `ProcessSample_WithSenderTracking_SetsOwnerId` · `SC_GZ021_HA_6_GizmoRegistrar_RegistersShowSlotsSetting` | ⭐ **all 6 are a strict SUBSET of the 67 failing at `7977adace`** (worktree run) — `comm -23` of mine against baseline is **empty** |
| **C** | 3 scenario-migration cases in `Hrot.Common.Tests` | confirmed pre-existing at `c6f54318c` in an earlier batch; unchanged |

⭐ **Working tree CLEAN after every suite run.** No golden added or touched *(the parallel batch owns
`Goldens/`)*. **Quarantine counts: 1 → 0** — the `ig` tripwire and its table are deleted, no skip added.

## 3. ⭐⭐⭐ RED PROVEN — twice, and the second one exposed a limit

| probe | result |
|---|---|
| remove `EqsSensor` from `MapSchemaPack`, rebuild, re-run | ⭐ **exactly the `ig` case reddens**, naming *"EqsSensor (required by EqsSensorGizmo in Hrot.IG.Gizmos)"* — 4 / 1. Restored → 5 / 0 |
| ⛔ **comment out IG's `MapSchemaPack.RegisterAll(world)` call** | ⚠⚠ **the gizmo rail stayed entirely GREEN**, while `ModeStartupRails`'s `ig` case reddened (7 / 1) |

⇒ ⭐⭐⭐ **The second probe is the useful one.** The gizmo rail's profiles call the registries directly, so it
asserts the **schema set is complete** and is blind to whether a host **wires** it. ⇒ **the two rails are
complementary and neither suffices alone** — this one catches a projector whose component nothing
registers, the mode rail catches a host that stops calling what it needs. ⛔ Not papered over: written into
the rail's own summary and into Q52 §6.5, so a green is not read as more than it is.

## 4. ⚠ TWO CORRECTIONS TO THE DESIGN'S OWN INVENTORY

| ⛔ | |
|---|---|
| ⭐⭐ **the rail must key on NAMESPACE, not assembly** | 📐 `GizmoRegistrarGenerator` emits *"one source file per namespace group"* (`:136`), so the **`Hrot.Presentation` assembly carries TWO registrars** — `Hrot.Presentation.Gizmos` and `Hrot.ScenarioEditor.Gizmos`. A host declaring one does **not** get the other's projectors ⇒ assembly-grouping would over-state every host's declarations and redden hosts that are fine |
| ⚠ **§1's *"`Hrot.ScenarioEditor` declares ZERO projectors"* is true of the PROJECT, false of the NAMESPACE** | 📐 there is no `Hrot.ScenarioEditor` project — the namespace lives inside `Hrot.Presentation` and holds **7** projector files. The inventory's grep looked for a directory |

⭐ Both folded into Q52 §6.3. ⛔ Neither changes the fix; both would have made the **rail** wrong.

## 5. WHAT I DID NOT DO, AND WHY

| ⛔ | |
|---|---|
| **`replaybrowser` is NOT in the rail's host table** | 📐 It declares four families (`ReplayBrowserSubsystem.cs:165-171`) and a grep for `RegisterComponent<`/`ComponentRegistry` across that subsystem returns **nothing** — there is no registration path to call. It **boots** (mode rail ✅), so it inherits a world registered elsewhere. ⛔ **The entry point was not guessed at**; filed as `ST-024`. ⚠ This is an omission from coverage, stated rather than hidden — the handoff's *"do not narrow the rail's scope to hide it"* |
| **did not "fix" the `editor` red** | ⭐ It was the RAIL's fault, and the decisive evidence was already in hand: **`--mode editor` boots**, so a host that would throw in bootstrap cannot be starting. My profile stopped at `EditorSubsystem.cs:857-858`; it also registers `CullingState`/`VisualEffectState` inline at `:864`/`:868`. ⇒ profile corrected to the host's real code, **not loosened** |
| **did not unify the editor's inline IG registrations** | ⭐ A real observation: the editor hand-picks IG components instead of calling `IgRoleComponentRegistry`, so that list is a second place that can drift. ⛔ Not this batch's surface — noted in `ST-024` |
| **did not touch `StatelessGizmoRegistry`** | 🔒 *"no losening"* — it keeps throwing |
| **did not add a golden, did not touch `Hrot.SystemTests/Goldens`** | the parallel regression-net batch owns them |
| **did not build `MapInteractionPack` / `TagMask`** | UXI-23 / UXI-28, out of scope |

## 6. RULE COMPLIANCE

| rule | |
|---|---|
| **1b** started-marker | ✅ pushed **before any code**, naming `7977adace` |
| **3 / 5** ids | ✅ **I** allocated `ST-022`…`ST-025`; the series stood at `ST-021` as the dispatch said |
| **4 / 7** re-sync | ✅ merged the coordinator branch at the start **and** again before the final commit |
| **8** gate report | ✅ §2 |
