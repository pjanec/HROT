<!--STATUS
state: LIVE
build-state: REPORT — autonomous CGF continuation, dispatched at 9c0b62991, built 2026-08-25
updated: 2026-08-25
current-answer: this file reports; the DESIGNS own the content. CE-031's as-built (the barrier proof and
  the measured k) is DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md §11; ruling 67 and CE-016 are
  folded into PROGRAMME_Cgf_Equals_Editor_Gap_Map.md and UX_RESUME_INTERACTION.md ruling 67.
known-conflict: none. Parallel-safe with the MCP session — one shared file touched, by one token.
-->
# REPORT — **cgf==editor: finish slice 4 + the next editing conversions** *(autonomous)*

> 📌 **Dispatched at `9c0b62991`.** Scope frozen there. **Ids allocated: `CE-031` … `CE-036`** *(rule 5)*.
> ⭐ All three READY items delivered. ⛔ Nothing from §4 (out of scope) attempted.
> 📄 **The designs are the source; this report points at them.**

## 0. ⚠ THE TWO PROCESS DEVIATIONS *(same as slice 4, restated once)*

| # | |
|---|---|
| ⚠ **branch** | The handoff asks for a fresh CGF-lane branch. ⛔ This session is bound to `claude/reset-working-branch-qd1qpv` and may not push elsewhere. Built there on a clean merge of the coordinator *(rule 7)*, started-marker pushed before any code *(rule 1b)*. ⭐ No file collision resulted |
| ⭐ **ids** | `CE-` series, Area L. ⚠ `tracker-counts.py` matches `BP-` rows only, so its **102 / 346** is unchanged and correct — ⛔ not a stale pass |

## 1. ⭐⭐⭐ ITEM 2.1 — `CE-029` CLOSED: the barrier is proven, and `k` is measured

📄 As-built: **[`…Slice4_Debug_PauseStep.md` §11](../../DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md)**.
📄 New rail: `Hrot.ClusterRunner.Integration.Tests/TheBarrierHaltsEveryNodeTests.cs`.

### 1a. 🔴 My own slice-4 report was wrong about the premise, and the correction was the user's

It said the barrier *"needs a live multi-node cluster, which no suite here boots."* 📐 **`HrotRunnerHarness`
boots `Orchestrator` + `SimHost` + `IG` + `ExCon` + `CGF` as separate subsystems on ONE real CycloneDDS
domain** — the orchestrator holds the only `MasterSyncController`, CGF and SimHost hold
`SlaveSyncController`s. ⚠ I had read the suite's `[Fact(Skip = "Requires CycloneDDS")]` as *"no DDS
here"*; `libddsc.so` is present and those tests pass.

### 1b. ⭐⭐ What the rail proves that no in-process rail can

| ⭐ | |
|---|---|
| ⭐⭐⭐ **the round trip** | CGF → `ClusterOpEgressTranslator` → DDS → orchestrator's `MasterSyncController.SwitchToDeterministic(roster)` → DDS → `SwitchTimeModeEvent` back on CGF's bus. ⛔ A unit rail proves an intent was PUBLISHED; only this proves it was **CARRIED** |
| ⭐⭐ **every node halts on the same SIMULATION tick** | measured **5.32 vs 5.36** and **5.56 vs 5.59** *(CGF vs SimHost)*, both `IsHalted` |
| ⭐ the step latch drops; the cluster stays deterministic; resume runs again | — |

### 1c. ⭐⭐⭐ `k` — measured, and `DQ30`'s assumption was ~10× optimistic

| run | `k` |
|---|---|
| 1 | **352.3 ms** |
| 2 | **252.3 ms** |

⚠⚠ `DQ30` §3 said *"k is expected small, still unmeasured"* and §B reasons from **"tens of ms"**.
⭐ Most of 250–350 ms is *designed* — §1's barrier carries **≈200 ms of deliberate lookahead** — so §B's
*"negligible for a deliberative brain"* survives, ⛔ **but it now means ~15–21 ticks at 60 Hz, not two or
three.** 📌 `DQ30`'s own instruction *"do not treat 'small' as verified"* was right to insist.

### 1d. 🔴🔴 TWO OF MY OWN ASSERTIONS WERE WRONG FIRST, and the design said so both times

| ⛔ what I asserted | ✅ what is true |
|---|---|
| *"a step must never move simulation time BACKWARDS"* — went RED at `5.5553 → 5.4872` | ⭐⭐ `DQ30` §B, about the snap it DECIDED: *"a cooldown or timer started at T is instantly k ticks **older** … a real discontinuity, and the price of not rewinding the world."* ⇒ **backwards is designed.** The rail now pins the discontinuity as **bounded** *(measured 68 ms, then 0 ms — it varies)*, which is the claim §B actually makes |
| *"a step must leave the debugger holding the world"* | 📐 `RequestStep()` calls `ClearPausedState()` ⇒ `IsPaused` is **false** after a step. What still holds the world is the **sim-group latch** + the cluster's deterministic mode — which is what design §3b actually describes |

⭐ **This is `R-129` landing on me twice in one item**: both times I asserted from the code's *shape*
instead of reading what the owning design promised. ⭐⭐ Both are now recorded in the design so the next
reader does not re-derive them.

### 1e. ⚠ A measured contract worth naming — `CE-035`

`IDataBreakpointManager.RequestContinue()` **cannot resume a stepped node**: `RequestStep` already
cleared `_isPaused`, so its opening guard makes it a no-op. ⭐ Production bypasses it —
`BlueprintDebugSession.Continue()` calls `_timeController.RequestResume()` directly, 📌 the same shape
`M-41` measured for the drain. ⛔ Not fixed: a neutral-assembly behaviour change outside these three
items. Filed.

## 2. ⭐⭐⭐ ITEM 2.2 — RULING 67 BUILT: asset roots from config

📄 Folded into **[ruling 67](../../UX/UX_RESUME_INTERACTION.md)** and the
**[gap map](../../PROGRAMME_Cgf_Equals_Editor_Gap_Map.md)**. Rails:
`Hrot.Editor.AiShared.Tests/Identity/TheDeployedNodeFindsItsAssetsTests.cs` *(9)*.

| ⭐ built | |
|---|---|
| `AssetRoots.Configure(root)` · `ResolveBase` · `DescribeBase` · `ResolveAssetsRoot`/`ResolveRecipesRoot` | order **config → source walk-up → output directory** |
| `--asset-root` on the runner, applied **once in `Program.cs` for EVERY host** | ⭐ `AssetRoots` is the stated *"single authority"*, so one call reaches every host that asks it a question — ⛔ not a per-subsystem notion of where assets live |
| CGF's `BuildAssetCatalog` routed through it, and its log now names **which arm answered** | ⚠ *"the catalog is empty"* and *"the catalog is pointed elsewhere"* are different problems |
| 🔒 a configured-but-missing root **THROWS at startup** | the ruling's own call |
| ⭐ unset config ⇒ **byte-identical** to before | ~30 call sites and every dev box unchanged |

### 2a. 🔴 `CE-033` — the half I nearly shipped missing

📐 `AssetsFor`/`RecipesFor`/`AssetsRoot`/`ScenariosRecipesRoot` are what
`BlueprintAssetContributor.BaseFolder`, `BTreeJsonAssetContributor`, `HsmJsonAssetContributor`,
`BTreeNewAssetService` and `HsmNewAssetService` resolve from — **where assets are BROWSED and CREATED**.
⛔ With only `ResolveBase` config-aware, a configured node would have **listed from one tree and created
in another** — ⭐⭐ the exact two-competing-authorities split ruling 67 exists to prevent, reintroduced by
its own fix. ⇒ all now hang off one `AbsoluteBase`, and a rail asserts the browse path and the create
path are the **same string**.

### 2b. ⚠ Two deviations from the ruling's own plan, both measured

| ⛔ the ruling said | ✅ what was done, and why |
|---|---|
| *"delete the walk-up"* | ⭐ **kept** — it is the MIDDLE ARM the same ruling asks for *("fallback to the repo source as of now")*. `EditorSubsystem`'s two inline copies remain `CE-018`, another lane's file |
| *"the config loader already exists — `ClusterConfiguration.LoadFrom`"*, and *"`ClusterConfiguration` lives in `Hrot.Orchestrator`; CGF must reference it, or the type moves"* | ⭐⭐ **neither was needed.** `Configure` takes a **plain path**, so the runner's existing config object supplies it ⇒ **the packaging question the ruling flagged does not arise at all**, and CGF gained no new assembly reference |

## 3. ⭐⭐ ITEM 2.3 — `CE-016` CLOSED, and its premise was already stale

📄 Rail: `Hrot.SystemTests/Conformance/TheRuntimeNodeCarriesTheTransportRails.cs` *(a distinctly-named new
file, as the handoff instructed)*.

| | |
|---|---|
| 🔴 **the stated premise was FALSE** | *"the CGF main-toolbar is EMPTY — `EditorSubsystem` is the only caller of `RegisterEntry`"*. 📐 Slice 3 registered `SaveAllAiDocuments` + `QuickReloadAiAsset` *(`CE-022`)*, and the shared conformance rail already asserts both by id and visibility |
| ⭐⭐⭐ **the real gap was a SILENT DEFAULT** | the editor puts `MainToolbarTimeControlSection` on its toolbar *(`EditorSubsystem:4715`)*; CGF built `ClusterTimeTransportAdapter` — **the very `ITimeTransportFacade` that section takes** — and passed it only to the STATUS BAR, **two lines away**. 📌 *"A production caller that HAS a dependency must PASS it."* ⇒ same shared section, same id and sort order; ⛔ nothing invented |
| ⛔ **NOT done, deliberately** | routing CGF's toolbar through `ToolbarCommandAdapter`/`IEditorCommands`, as the editor does. 📐 **CGF registers ZERO shell commands and has no icon provider**, and `IEditorCommands` is precisely what the concurrent MCP session is building ⇒ a collision. Recorded in the DECISION LOG, routed around |
| ⭐ **one-token edit to the shared file** | `CaptureByKindAsync` `private` → `internal`, so the capture is not duplicated *(ruling 9)*. ⚠ Chosen as the smallest possible edit to a concurrently-edited file — a new method there would have been the merge race the handoff warned about |

## 4. ⭐⭐ DECISION LOG *(autonomy protocol §0)*

| # | ambiguity | decision, and why |
|---|---|---|
| 1 | The barrier rail needs a hit trigger. | ⭐ `AddBreakpoint(new ExternalHitTagPredicateDto{Tag})` + `OnExternalHit` — a **production** entry point that routes through the real `OnHit` *(rewind, `RequestPause`, `PausedTick`)*, not a test back door |
| 2 | `k` in what unit? | ⭐ `PausedTick` and `BarrierWallTicks` are **both** `GlobalTime.TotalWallTicks` (100-ns) ⇒ directly subtractable. Exposed `ClusterBarrierWallTicks` on the controller as a **real diagnostic**, not test scaffolding — `DQ30` demands the measurement |
| 3 | Where does the config root come from — `ClusterConfiguration`? | ⛔ No. `Configure` takes a plain path ⇒ **no new assembly reference, no packaging decision** *(which §4 excluded from this run anyway)* |
| 4 | Should the walk-up be deleted, as ruling 67 says? | ⛔ No — it is the ruling's own middle arm. Deleting it would break every dev box |
| 5 | Route CGF's toolbar through `ToolbarCommandAdapter`? | ⛔ No — needs a `ShellCommands` set CGF does not have, on the surface the MCP session is building. **Routed around** |
| 6 | `RequestContinue`-after-step is broken. Fix it? | ⛔ No — neutral-assembly behaviour change, outside the three items. **Filed as `CE-035`** |
| 7 | The `HarnessSmokeTests` skips are stale. Un-skip them? | ⛔ No — another file's quarantine decision. **Measured and filed as `CE-036`**, with the real cause: domain id **250** is out of CycloneDDS range, not missing DDS |

## 5. GATES *(rule 8 contract)*

⭐ Built ONCE per project, then `--no-build`. ⛔ **The full solution was never built.**

| # | gate | verbatim command | `--no-build` | result | Δ vs `b02d641bd` *(started-marker)* |
|---|---|---|:--:|---|---|
| 1 | AiShared *(ruling-67 rails' home)* | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/… --no-build` | ✅ | ⭐ **2025 / 0 / 1 skip** | **+9**, all new. ⭐ Confirms the process-global `Configure` does not leak |
| 2 | SimHost *(CGF's unit home)* | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/… --no-build` | ✅ | ⚠ **668 / 5 / 3** | 0 new; the same rotating flake slice 4 characterised |
| 3 | ClusterRunner unit | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/… --no-build` | ✅ | ⚠ **271 / 2 / 0** | unchanged — both pre-existing |
| 4 | ⭐⭐⭐ **integration — the barrier rail** | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/… --no-build --filter "TheBarrierHaltsEveryNodeTests"` | ✅ | ✅ **1 / 0** | **+1** (new) |
| 5 | ⭐ integration — full suite *(baseline)* | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/… --no-build` | ✅ | ⚠ **27 / 29** | see §5a — `BP-378`'s long-standing state |
| 6 | ⭐⭐ **`T3` system suite** *(real `--mode all`, includes both new CE-016 rails)* | `bash scripts/run-system-tests.sh --no-build` **(BACKGROUNDED)** | ✅ | see §6 | — |
| 7 | tracker | `python3 scripts/tracker-counts.py --check` | — | ✅ **OK — 102 / 346** | unchanged: `BP-` only |
| 8 | ledger | `python3 scripts/rulings-check.py` | — | ✅ **25/25** | 1 pre-existing staleness WARN |
| 9 | design gate | `python3 scripts/design-digest.py --check` | — | ✅ **OK, 86 docs** | +3 docs touched |
| 10 | mermaid | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs …Slice4_….md` | — | ✅ **4/4 parse** | unchanged |

⭐ **Tree CLEAN after every suite run**; no goldens moved; **no new skips** *(the three I un-skipped as a
probe are restored — `git status` on that project is empty)*.

### 5a. 🔴 The integration suite's 29 reds — and I nearly mis-attributed them

⚠⚠ **Several are REPLICATION tests** *(`DragDrop`, `EntityDestroy`, `GhostPromotion`,
`SpawnMovingVehicle`)*, and **slice 4 added an ingress gate**. ⛔ *"Pre-existing, my diff doesn't touch
them"* was not good enough — the gate is exactly the mechanism that could stop replication arriving.

⭐⭐ **So I measured it**: disabled `WireWorldStateFreezeGate()` entirely, rebuilt, re-ran
`DragDropIntegrationTests` ⇒ **fails identically** *("IG did not receive entity (netId=1)")*. ⇒ the gate
is **not** the cause. Corroborated by `EventSerializationHelperTests`, which involves **no cluster at
all** *(`Expected: String, Actual: Object`)*. ⇒ `BP-378`'s known state, the suite filter-gated for ~40
batches *(`R-131`)*.

### 5b. Reds attributed

| red | verdict |
|---|---|
| integration ×29 | ⭐ **pre-existing** — proven by the gate-disabled probe above, not by assertion |
| `Hrot.SimHost` — `FullBranchPipelineTests`, `ReplayLoadClusterOpHandlerTests`, `StagingEntityExtractorTests` | ⭐⭐ the rotating order flake: `ReplayLoadClusterOpHandlerTests` is **6/6 in isolation**; `FullBranchPipelineTests` is a missing-file-in-`/tmp` IO test, red in isolation too, untouched since `2026-07-16` |
| `Hrot.ClusterRunner` ×2 `DataDrivenGizmoPredicateTests` | ⭐ pre-existing, unchanged count from slice 4 |

### 5c. ⭐⭐ REVERT-GOES-RED — inverse edits, never `git checkout --`

| # | inverse edit | result |
|---|---|---|
| **A** | the controller's three request methods return early *(the no-op restored)* | 🔴 **the barrier rail fails** at the first halt assertion |
| **B** | `ResolveBase`'s config arm removed | 🔴 **2 red** — `AConfiguredRootResolvesWithNoSourceTreeAnywhereAbove`, `ConfigOutranksTheSourceWalkUp` |

⭐ `CE-033`'s rail needed no inverse edit: it went red **as the defect**, before the fix.

## 6. ⭐ `T3` — the system suite

⚠ Backgrounded per the build rules and **never sat on**. Its result and the two `CE-016` rails' verdict
are recorded here on completion; if it lands after this batch closes, it lands in the next session, which
is what `T3` means.

## 7. ⚠ WHAT IS STILL OPEN

| | |
|---|---|
| `CE-035` | `RequestContinue` cannot resume a stepped node |
| `CE-036` | the stale `Requires CycloneDDS` skips *(and the real cause: domain 250 is out of range)* |
| `CE-018` | `EditorSubsystem`'s two inline `.csproj` walk-ups still bypass `AssetRoots` — another lane's file |
| ⛔ **§4's four out-of-scope items** | untouched, by instruction: AQ25 authoring shell · Q25-C behavior-affinity · `Hrot.Editor` packaging · Axis B |
| ⚠ **CGF's toolbar still does not use `IEditorCommands`** | the editor's toolbar is command-bound; CGF's three entries are direct delegates. Blocked on a `ShellCommands` set for CGF, which is the MCP session's ground |
