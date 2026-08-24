<!--STATUS
state: LIVE
updated: 2026-08-24
current-answer: the whole file — the batch report for HANDOFF_Gizmo_Reflection.md. All five items done.
  Design content is in DESIGN_Uniform_Gizmo_Membership.md §9.
known-conflict: none.
-->
# REPORT — gizmo membership by reflection

> 📌 **Dispatch:** [`HANDOFF_Gizmo_Reflection.md`](HANDOFF_Gizmo_Reflection.md), frozen at **`5db5c60bc`**.
> Branch `claude/blueprint-macro-feature-sdmspn` (BACKEND lane).
> **ids allocated: `ST-031` … `ST-035`** *(rule 5)*, filed in the commits that use them.

## 1. OUTCOME — **all five items; two of them corrected my own previous batch**

| item | outcome |
|---|---|
| **ⓠ** — `ST-032` | ✅ **measured, and BOTH exposures were LIVE.** `MapSchemaPack` deleted with all six call sites |
| **①** — `ST-031` | ✅ `GizmoReflectionRegistrar` in `Fdp.Toolkits`; **five hosts converted one at a time**. Matrix now **7/7 everywhere** |
| **②** — `ST-033` | ✅ completeness rail, **enumerated** not hardcoded. Red proven, naming `HealthBarGizmo` |
| **③** — `ST-034` | ✅ non-bloat rails. Red proven. ⚠ **the first version was a VACUOUS GREEN** — §4 |
| **④** — `ST-031` | ✅ cost measured: **inside variance** |
| ⚠ **filed** | `ST-035` — reflection can pin a collectible ALC; **not triggered today**, and that is measured |

⭐ **Obligation ③:** §8.3 carried **1 `classDiagram` (5 boxes)** + **1 `sequenceDiagram`**; both built as
drawn, with **one addition** (§9.4). Folded into
[`DESIGN_Uniform_Gizmo_Membership.md`](../../DESIGN_Uniform_Gizmo_Membership.md) **§9**
*(`build-state: BUILT`; §7.3/§7.4 closed, `MapGizmoPack`/Option B closed by §8)*.

## 2. 🔴🔴 ITEM ⓠ — **the measurement, and it is the serious part of this batch**

**Both halves of §8.4's concern were live, and my own `ST-027` armed the first one.**

| 📐 measured | |
|---|---|
| 🔴 **the TkbTemplate risk was NOT theoretical** | `EntityRepository.IsComponentTypeRegistered` is literally `_componentTables.ContainsKey(typeof(T))` — **table-based** — and **five** translators read exactly that as licence to add the component: `BehaviorTkbTranslator:52` *(`BehaviorState`)* · `:100` *(`BrainBlackboard`)* · `PerceptionTkbTranslator:29` *(`PerceptionReceptor`)* · `:39` *(`TargetMemory`)* · `VehicleKinematicsTkbTranslator:56` *(`NavigationIntent`)*. ⇒ ⛔ **spawned entities on IG/SimHost/CGF/replaybrowser would have GAINED brain and perception components they never carried** |
| 🔴 **id-only really is insufficient** | `AsyncRecorder.BuildSchemaManifest` iterates `GetRecordableTypeIds()` — **by ID, not by table** — and `GetOrRegisterManaged` defaults both flags to `true` (`ComponentType.cs:157-158`) |
| ✅ **the fix** | `MapSchemaPack` **deleted, all six call sites removed.** The registrar resolves each projector's ids itself, **id-only**, immediately before registering ⇒ there is no Phase-2 schema pre-pass left to get wrong, and §3 ③'s ordering constraint dissolves with it |

### ⭐⭐⭐ 2a. §8.4's prescription needed one refinement, and it is load-bearing

⛔ *"mark non-recordable on nodes that do not simulate them"* is **not directly expressible**:
`SetRecordable` is **process-global**, so under `--mode all` a co-tenant may genuinely simulate one of these
and clearing unconditionally would **drop that host's real data from the recording**.

⇒ ⭐ The registrar clears the flags **only for ids it CREATES** (`GetId(type) == -1` first). 📐 Order-safe
both ways: a simulating host that registered earlier keeps its policy, and one that registers later
re-applies `SetRecordable`/`SetSaveable` from its DataPolicy — `RegisterComponent` does that on **every**
call, not just first creation.

## 3. ⭐⭐ §GATES

| # | gate — verbatim command | `--no-build`? | result | delta vs `5db5c60bc` |
|---|---|---|---|---|
| 1 | `dotnet build IOS-IG-SimHost.sln` | builds | ✅ **0 errors** | none |
| 2 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter Category=SystemModes` | `--no-build` | ✅ **8 / 0** | ⭐ run **per host converted**, not once at the end |
| 3 | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build` | `--no-build` | ✅ **58 / 0** | ⭐ **exactly the stated baseline** |
| 4 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests --no-build --filter FullyQualifiedName~GizmoSchemaFollowsDeclarationRails` | `--no-build` | ✅ **4 / 0** | rails rewritten (§4) |
| 5 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests --no-build` | `--no-build` | ⚠ **271 / 2** | 2 pre-existing (row A) |
| 6 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build` | `--no-build` | ⚠ **233 / 1** or **234 / 0** | flaky **on both sides** — row B, A/B'd |
| 7 | `dotnet test Hrot/Subsystems/Hrot.IG.Tests --no-build --filter FullyQualifiedName~EntityInfoTranslatorTests` | `--no-build` | ⚠ **10 / 4** | none — `ST-026`'s stable four, by name |
| 8 | `python3 scripts/tracker-counts.py --check` | n/a | ✅ OK — open **99** / done **333** | ⚠ blind to `ST-` rows |
| 9 | `python3 scripts/rulings-check.py` | n/a | ✅ **24/24** | 2 known staleness WARNs, not mine |
| 10 | `python3 scripts/design-digest.py --check` | n/a | ✅ clean | none |

⭐ **Mode matrix**, re-run after each host conversion: `ig` ✅ · `simhost` ✅ · `cgf` ✅ · `all` ✅ ·
`editor` ✅ · `replaybrowser` ✅ · `excon` ✅ · `orchestrator` ✅.
⭐ **Item ④ cost:** bootstrap span (`--mode simhost`, banner → `SlaveSyncController` initialised, n=3)
**172 / 189 ms** against **172 / 178 / 176 ms** at the dispatch sha ⇒ **inside run-to-run variance.**
⚠ **`mermaid-check.mjs` SKIPPED** *(needs an `npm install` this session lacks)*. ⭐ **No Mermaid added or
edited** — §9 is prose and tables.

### Every RED, confirmed **by name**

| | red | evidence |
|---|---|---|
| **A** | `D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity` · `D003_Predicate_True_AllowsUpdateAndDraw` | pre-existing, confirmed in earlier batches at `c6f54318c` / `5963fffd4`; unchanged |
| **B** | `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | ⭐ **A/B'd in the main tree:** with my `EditorSubsystem` change **reverted** it still flakes — `234/0`, **`233/1`**, `234/0`. With it, `233/1`, `233/1`, `234/0`. ⇒ **pre-existing GC/ALC timing flake, present on both sides.** ⭐ Also passes **3/3 in isolation** on both trees, and 📐 **nothing in `Hrot.Editor.Tests` calls any gizmo registrar** (grep: zero hits), so the reflection path never runs in that process. ⚠ **Stated honestly: 2-of-3 vs 1-of-3 is not distinguishable at n=3, so I am not claiming my change has zero timing effect — only that the failure exists without it** |

⚠⚠ **A GATE LIMITATION worth recording:** a **worktree baseline for `Hrot.Editor.Tests` is not obtainable in
this environment.** 📐 Three attempts (per-project build, whole-suite, full solution build) all failed in the
worktree with source-generator resolution errors — `Generator 'BlueprintIncrementalGenerator' failed to
initialize … Could not load file or assembly 'Hrot.Blueprints.Compiler'` plus
`'Hrot.AI.Behaviors.Trees' does not exist`. ⇒ ⭐ **that is why row B was A/B'd by reverting in the main tree
instead** — a better measurement anyway, but the limitation will bite the next session too.

⭐ **Working tree CLEAN after every suite run.** No golden touched. **No skips added.**

## 4. ⚠⚠ THE VACUOUS GREEN — **the near-miss I would most want reviewed**

📐 My first non-bloat rail asserted over the **production** components and **stayed GREEN with the
production flag-clearing REMOVED.** Cause: by the time it ran, **all 7** projector-required components
visible in that process were already registered by something else, so the set it checked was **empty**.

⇒ ⛔ **A rail that cannot fail is worse than no rail — it reports safety it never established**, and only
the revert-probe exposed it. ⇒ ✅ Fixed **by construction**, not by a guard: a **test-only projector**
requiring a **test-only component** (`[ComponentId(500)]`, far above the highest production id **264**) that
nothing in production can register, and which still travels the real discovery path.

⚠ **A second order-trap on the way:** asserting *"not yet registered"* as a precondition **passed in
isolation and failed in the suite** — a sibling case runs `RegisterAll` first. ⇒ the assertion is on the
**outcome**, which is order-independent.

### Red proven, both rails

| probe | result |
|---|---|
| drop one projector after discovery | ⭐ completeness rail reddens: *"1 projector(s) were discovered but not registered: `Hrot.Common.Diagnostics.Gizmos.HealthBarGizmo`"* |
| remove `SetRecordable(false)`/`SetSaveable(false)` | ⭐ non-bloat rail reddens, naming the recorder mechanism |

## 5. ⚠ INVARIANT `A` IS RETIRED — **argued, not silently dropped**

The dispatch said *"`GizmoSchemaFollowsDeclarationRails` (invariant `A`, `ST-023`) stays"*. ⛔ **I retired
its per-host profile table**, and the reason is that `①` dissolved its premise: `A` asserted that a host's
**curated** family list was satisfied by that host's registration. There are no curated lists any more, and
the registrar resolves each projector's ids **immediately before registering it**, so *"declared but
unsatisfied"* is no longer a state the code can be in — it is closed by construction.

⇒ Keeping the table would have meant maintaining a fiction listing family subsets that no longer exist — and
📌 a stale profile in that very file already reddened a healthy host once (`ST-024`). The file keeps its name
and is now the home of `B` plus the non-bloat rails, per *"one mechanism, do not keep two"*.

## 6. WHAT I DID NOT DO

| ⛔ | |
|---|---|
| **did not reflect components or events** | 🔒 role-gated and untouched, per the dispatch's ⛔⛔ |
| **did not touch `RepositoryPriming`** | replaybrowser keeps it (inspection exception) — ⭐ it is also what answered `ST-024`'s open question |
| **did not remove the editor's inline `CullingState`/`VisualEffectState`** | ⭐ item ⓠ shows they are real tables the editor **does** simulate ⇒ **left**, as §2 instructed |
| **did not build the headless publish-gate** | §8.7 backlog; the user ruled the cost acceptable |
| **did not revert `VisualEffectState`'s move** | ⚠ no longer load-bearing once `MapSchemaPack` went, but consistent with its four siblings — reverting would be churn |
| **did not touch `MapInteractionPack`, `TagMask`, aggregation, `HN-011`** | out of scope per §3 |

⚠ **Shared-file touches, declared:** `EditorSubsystem.cs` (the registrar block at `:1459-1475`, **not** the
preview batch's `:525-560`) and `Hrot.SystemTests` was **not** touched at all this batch. Rule 4 re-pull done
before the final commit.

## 7. RULE COMPLIANCE

| rule | |
|---|---|
| **1b** started-marker | ✅ pushed before any code, naming `5db5c60bc` |
| **3 / 5** ids | ✅ `ST-031`…`ST-035`, starting at `ST-031` as the dispatch said |
| **4 / 7** re-sync | ✅ merged at the start and again before the final commit |
| **8** gate report | ✅ §3, integration gate run **per host converted** |
| ⭐ **not big-bang** | ✅ five hosts converted and verified individually |
